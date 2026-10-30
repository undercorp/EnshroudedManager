using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using EnshroudedManager.Shared;

namespace EnshroudedManager.Service;

/// <summary>Values chosen in the installer, handed over once through data\setup.json.</summary>
public class SetupRequest
{
    public string? Language { get; set; }
    public string? InstallDir { get; set; }
    public bool? WebEnabled { get; set; }
    public int? Port { get; set; }
    public string? Password { get; set; }
    public string? ServerName { get; set; }
    public int? SlotCount { get; set; }
    public int? QueryPort { get; set; }
    public string? AdminPassword { get; set; }
    public string? FriendPassword { get; set; }
    public string? GuestPassword { get; set; }
}

/// <summary>
/// Makes a machine ready: steamcmd, server files, a first enshrouded_server.json and the firewall rule of each instance.
/// Everything is idempotent, so it simply runs at every start.
/// </summary>
public class Provisioner
{
    private const string SteamCmdUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
    private const string FirewallRule = "Enshrouded Server (EnshroudedManager)";
    private readonly SettingsStore _settings;
    private readonly LogBuffer _log;
    private readonly SteamCmd _steam;

    public Provisioner(SettingsStore settings, LogBuffer log, SteamCmd steam)
    {
        _settings = settings;
        _log = log;
        _steam = steam;
    }

    private ManagerSettings S => _settings.Current;

    public string SetupPath => Path.Combine(_settings.DataDir, "setup.json");

    /// <summary>Applies data\setup.json written by the installer, then deletes it (it holds a password in clear).</summary>
    public void ApplySetupFile(Auth auth)
    {
        if (!File.Exists(SetupPath)) return;
        try
        {
            var req = JsonSerializer.Deserialize<SetupRequest>(File.ReadAllText(SetupPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                      ?? new SetupRequest();
            _settings.Update(s =>
            {
                if (req.Language is "fr" or "en") s.Language = req.Language;
                if (!string.IsNullOrWhiteSpace(req.InstallDir)) s.Main.InstallDir = req.InstallDir.Trim();
                if (req.WebEnabled != null) s.WebEnabled = req.WebEnabled.Value;
                if (req.Port is > 0 and < 65536) s.Port = req.Port.Value;
            });
            if (!string.IsNullOrEmpty(req.Password)) auth.SetPassword(req.Password);
            var main = S.Main;
            if (!string.IsNullOrWhiteSpace(main.InstallDir))
            {
                Directory.CreateDirectory(main.InstallDir);
                if (!File.Exists(main.ConfigPath))
                    WriteInitialConfig(main.ConfigPath, req.ServerName, req.SlotCount, req.QueryPort, groups =>
                    {
                        SetPassword(groups, "Admin", req.AdminPassword);
                        SetPassword(groups, "Friend", req.FriendPassword);
                        SetPassword(groups, "Guest", req.GuestPassword);
                    });
            }
            _log.Info(L.T("Paramètres de l'installation appliqués.", "Installer settings applied."));
        }
        catch (Exception ex)
        {
            _log.Error(L.T("Lecture de setup.json impossible : ", "Could not read setup.json: ") + ex.Message);
        }
        finally
        {
            try { File.Delete(SetupPath); } catch { }
        }
    }

    private static void SetPassword(JsonArray groups, string group, string? password)
    {
        if (string.IsNullOrEmpty(password)) return;
        foreach (var g in groups.OfType<JsonObject>().Where(g => g["name"]?.ToString() == group)) g["password"] = password;
    }

    private static string RandomPassword(string prefix) =>
        prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(6)).Replace('+', 'x').Replace('/', 'y');

    /// <summary>
    /// First enshrouded_server.json: the game's own defaults (v1.0), random group passwords like the game does
    /// for a fresh file, then <paramref name="customize"/> for the groups.
    /// </summary>
    public void WriteInitialConfig(string path, string? name, int? slots, int? queryPort, Action<JsonArray> customize)
    {
        using var stream = typeof(Provisioner).Assembly.GetManifestResourceStream("DefaultConfig.json")!;
        var root = JsonNode.Parse(stream)!.AsObject();
        if (!string.IsNullOrWhiteSpace(name)) root["name"] = name.Trim();
        if (slots is >= 1 and <= 16) root["slotCount"] = slots.Value;
        if (queryPort is > 0 and < 65536) root["queryPort"] = queryPort.Value;
        var groups = root["userGroups"]!.AsArray();
        foreach (var g in groups.OfType<JsonObject>()) g["password"] = RandomPassword(g["name"]!.ToString());
        customize(groups);
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        _log.Info(L.T($"Configuration de base du serveur créée ({path}).", $"Base server configuration created ({path})."));
    }

    /// <summary>
    /// Groups of a resource depot: Admin with its password, and an open Visitor group that can take from the
    /// chests but not change the bases (the world is put back on every reset anyway).
    /// </summary>
    public static void DepotGroups(JsonArray groups, string adminPassword, string visitorPassword)
    {
        groups.Clear();
        groups.Add(new JsonObject
        {
            ["name"] = "Admin", ["password"] = adminPassword, ["canKickBan"] = true, ["canAccessInventories"] = true,
            ["canEditWorld"] = true, ["canEditBase"] = true, ["canExtendBase"] = true, ["reservedSlots"] = 1,
        });
        groups.Add(new JsonObject
        {
            ["name"] = "Visitor", ["password"] = visitorPassword, ["canKickBan"] = false, ["canAccessInventories"] = true,
            ["canEditWorld"] = true, ["canEditBase"] = false, ["canExtendBase"] = false, ["reservedSlots"] = 0,
        });
    }

    /// <summary>Downloads steamcmd from Valve when it is missing.</summary>
    public async Task EnsureSteamCmdAsync(Action<string, double?> progress, CancellationToken ct)
    {
        if (File.Exists(S.SteamCmdPath)) return;
        var dir = Path.GetDirectoryName(S.SteamCmdPath)!;
        Directory.CreateDirectory(dir);
        progress(L.T("Téléchargement de SteamCMD", "Downloading SteamCMD"), null);
        _log.Info(L.T("Téléchargement de SteamCMD depuis Valve…", "Downloading SteamCMD from Valve…"));
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        var zip = Path.Combine(dir, "steamcmd.zip");
        await using (var file = File.Create(zip))
            await (await http.GetStreamAsync(SteamCmdUrl, ct)).CopyToAsync(file, ct);
        ZipFile.ExtractToDirectory(zip, dir, overwriteFiles: true);
        File.Delete(zip);
        _log.Info(L.T("SteamCMD installé.", "SteamCMD installed."));
    }

    /// <summary>First download of the dedicated server into an instance folder, with the progress reported by steamcmd.</summary>
    public async Task<bool> InstallServerAsync(Instance instance, Action<string, double?> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(instance.InstallDir);
        progress(L.T("Mise à jour de SteamCMD et préparation (quelques minutes au premier lancement)",
                     "Updating SteamCMD and preparing (a few minutes on first run)"), null);
        long expected = 0;
        try { expected = (await _steam.GetRemoteInfoAsync(ct)).Size; } catch { }
        instance.Log.Info(L.T(
            $"Téléchargement du serveur Enshrouded dans {instance.InstallDir} (~{expected / 1e9:0.0} Go)…",
            $"Downloading the Enshrouded server to {instance.InstallDir} (~{expected / 1e9:0.0} GB)…"));

        var ok = await _steam.UpdateAsync(instance, null, ct, (phase, pct, done, total) =>
        {
            var text = phase.Contains("verif", StringComparison.OrdinalIgnoreCase)
                ? L.T("Vérification des fichiers", "Verifying files")
                : L.T($"Téléchargement du serveur : {done / 1e9:0.0} / {total / 1e9:0.0} Go",
                      $"Downloading server: {done / 1e9:0.0} / {total / 1e9:0.0} GB");
            progress(text, total > 0 ? pct : null);
        });
        ok = ok && instance.Installed;
        var build = _steam.GetLocalBuild(instance.InstallDir);
        if (ok) instance.Log.Event(L.T($"Serveur Enshrouded installé (build {build}).", $"Enshrouded server installed (build {build})."));
        else instance.Log.Error(L.T("Le téléchargement du serveur a échoué, nouvel essai au prochain contrôle.", "Server download failed, will retry at the next check."));
        return ok;
    }

    private static string RuleName(Instance instance) => instance.IsMain ? FirewallRule : $"{FirewallRule} - {instance.Id}";

    /// <summary>Inbound rule for the game executable of an instance (all ports it opens), created once.</summary>
    public void EnsureFirewall(Instance instance)
    {
        if (!instance.Installed) return;
        try
        {
            var rule = RuleName(instance);
            if (Run("netsh", $"advfirewall firewall show rule name=\"{rule}\" verbose").Contains(instance.ExePath, StringComparison.OrdinalIgnoreCase)) return;
            Run("netsh", $"advfirewall firewall delete rule name=\"{rule}\"");
            Run("netsh", $"advfirewall firewall add rule name=\"{rule}\" dir=in action=allow program=\"{instance.ExePath}\" enable=yes profile=any");
            if (Run("netsh", $"advfirewall firewall show rule name=\"{rule}\" verbose").Contains(instance.ExePath, StringComparison.OrdinalIgnoreCase))
                instance.Log.Info(L.T("Règle de pare-feu Windows ajoutée pour le serveur.", "Windows firewall rule added for the server."));
            else
                instance.Log.Warn(L.T("La règle de pare-feu du serveur n'a pas pu être créée (droits administrateur requis).",
                                      "The server firewall rule could not be created (administrator rights required)."));
        }
        catch (Exception ex)
        {
            instance.Log.Warn(L.T("Règle de pare-feu non créée : ", "Firewall rule not created: ") + ex.Message);
        }
    }

    public static void RemoveFirewall(Instance instance)
    {
        try { Run("netsh", $"advfirewall firewall delete rule name=\"{RuleName(instance)}\""); } catch { }
    }

    private static string Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }
}
