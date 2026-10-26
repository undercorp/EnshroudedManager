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
/// Makes a fresh machine ready: steamcmd, server files, a first enshrouded_server.json and the firewall rule.
/// Everything is idempotent, so it simply runs at every start.
/// </summary>
public class Provisioner
{
    private const string SteamCmdUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
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
                if (!string.IsNullOrWhiteSpace(req.InstallDir)) s.InstallDir = req.InstallDir.Trim();
                if (req.WebEnabled != null) s.WebEnabled = req.WebEnabled.Value;
                if (req.Port is > 0 and < 65536) s.Port = req.Port.Value;
            });
            if (!string.IsNullOrEmpty(req.Password)) auth.SetPassword(req.Password);
            if (!string.IsNullOrWhiteSpace(S.InstallDir))
            {
                Directory.CreateDirectory(S.InstallDir);
                if (!File.Exists(S.ConfigPath)) WriteInitialConfig(req);
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

    /// <summary>First enshrouded_server.json: the game's own defaults (v1.0) plus the installer choices.</summary>
    private void WriteInitialConfig(SetupRequest req)
    {
        using var stream = typeof(Provisioner).Assembly.GetManifestResourceStream("DefaultConfig.json")!;
        var root = JsonNode.Parse(stream)!.AsObject();
        if (!string.IsNullOrWhiteSpace(req.ServerName)) root["name"] = req.ServerName.Trim();
        if (req.SlotCount is >= 1 and <= 16) root["slotCount"] = req.SlotCount.Value;
        if (req.QueryPort is > 0 and < 65536) root["queryPort"] = req.QueryPort.Value;

        // Like the game does for a fresh file: a random password for every group that was not chosen.
        string Pass(string? chosen, string prefix) => !string.IsNullOrEmpty(chosen) ? chosen
            : prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(6)).Replace('+', 'x').Replace('/', 'y');
        foreach (var g in root["userGroups"]!.AsArray().OfType<JsonObject>())
        {
            var name = g["name"]!.ToString();
            g["password"] = name switch
            {
                "Admin" => Pass(req.AdminPassword, "Admin"),
                "Friend" => Pass(req.FriendPassword, "Friend"),
                "Guest" => Pass(req.GuestPassword, "Guest"),
                _ => Pass(null, name),
            };
        }
        File.WriteAllText(S.ConfigPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        _log.Info(L.T($"Configuration de base du serveur créée ({S.ConfigPath}).", $"Base server configuration created ({S.ConfigPath})."));
    }

    public bool Configured => !string.IsNullOrWhiteSpace(S.InstallDir);
    public bool ServerInstalled => Configured && File.Exists(S.ExePath);

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

    /// <summary>First download of the dedicated server, with the progress reported by steamcmd.</summary>
    public async Task<bool> InstallServerAsync(Action<string, double?> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(S.InstallDir);
        progress(L.T("Mise à jour de SteamCMD et préparation (quelques minutes au premier lancement)",
                     "Updating SteamCMD and preparing (a few minutes on first run)"), null);
        long expected = 0;
        try { expected = (await _steam.GetRemoteInfoAsync(ct)).Size; } catch { }
        _log.Info(L.T(
            $"Téléchargement du serveur Enshrouded dans {S.InstallDir} (~{expected / 1e9:0.0} Go)…",
            $"Downloading the Enshrouded server to {S.InstallDir} (~{expected / 1e9:0.0} GB)…"));

        var ok = await _steam.UpdateAsync(null, ct, (phase, pct, done, total) =>
        {
            var text = phase.Contains("verif", StringComparison.OrdinalIgnoreCase)
                ? L.T("Vérification des fichiers", "Verifying files")
                : L.T($"Téléchargement du serveur : {done / 1e9:0.0} / {total / 1e9:0.0} Go",
                      $"Downloading server: {done / 1e9:0.0} / {total / 1e9:0.0} GB");
            progress(text, total > 0 ? pct : null);
        });
        ok = ok && File.Exists(S.ExePath);
        if (ok) _log.Event(L.T($"Serveur Enshrouded installé (build {_steam.GetLocalBuild()}).", $"Enshrouded server installed (build {_steam.GetLocalBuild()})."));
        else _log.Error(L.T("Le téléchargement du serveur a échoué, nouvel essai au prochain contrôle.", "Server download failed, will retry at the next check."));
        return ok;
    }

    /// <summary>Inbound rule for the game executable (all ports it opens), created once.</summary>
    public void EnsureFirewall()
    {
        if (!ServerInstalled) return;
        try
        {
            const string rule = "Enshrouded Server (EnshroudedManager)";
            var check = Run("netsh", $"advfirewall firewall show rule name=\"{rule}\" verbose");
            if (check.Contains(S.ExePath, StringComparison.OrdinalIgnoreCase)) return;
            Run("netsh", $"advfirewall firewall delete rule name=\"{rule}\"");
            Run("netsh", $"advfirewall firewall add rule name=\"{rule}\" dir=in action=allow program=\"{S.ExePath}\" enable=yes profile=any");
            if (Run("netsh", $"advfirewall firewall show rule name=\"{rule}\" verbose").Contains(S.ExePath, StringComparison.OrdinalIgnoreCase))
                _log.Info(L.T("Règle de pare-feu Windows ajoutée pour le serveur.", "Windows firewall rule added for the server."));
            else
                _log.Warn(L.T("La règle de pare-feu du serveur n'a pas pu être créée (droits administrateur requis).",
                              "The server firewall rule could not be created (administrator rights required)."));
        }
        catch (Exception ex)
        {
            _log.Warn(L.T("Règle de pare-feu non créée : ", "Firewall rule not created: ") + ex.Message);
        }
    }

    private static string Run(string exe, string args)
    {
        using var p = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true })!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }
}
