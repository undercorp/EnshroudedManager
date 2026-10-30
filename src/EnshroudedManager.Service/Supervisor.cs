using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using EnshroudedManager.Shared;

namespace EnshroudedManager.Service;

/// <summary>
/// Owns the instances: installs them, checks Steam for updates and updates every instance, triggers depot
/// resets and reads player counts.
/// </summary>
public class Supervisor : BackgroundService
{
    private readonly SettingsStore _settings;
    private readonly LogBuffer _log;
    private readonly SteamCmd _steam;
    private readonly Provisioner _provisioner;
    private readonly Dictionary<string, InstanceRunner> _runners = new();
    private readonly object _lock = new();
    private readonly CancellationTokenSource _stoppingSource = new();
    private CancellationTokenSource _wake = new();

    public string? RemoteBuild { get; private set; }
    public DateTimeOffset? LastCheck { get; private set; }
    public DateTimeOffset? NextCheck { get; private set; }

    public Supervisor(SettingsStore settings, LogBuffer log, SteamCmd steam, Provisioner provisioner)
    {
        _settings = settings;
        _log = log;
        _steam = steam;
        _provisioner = provisioner;
        foreach (var s in settings.Current.Instances) AddRunner(s.Id);
    }

    private ManagerSettings S => _settings.Current;
    private CancellationToken Stopping => _stoppingSource.Token;

    private InstanceRunner AddRunner(string id)
    {
        var runner = new InstanceRunner(new Instance(_settings, _log, id), _steam, _provisioner, Stopping);
        lock (_lock) _runners[id] = runner;
        return runner;
    }

    public IReadOnlyList<InstanceRunner> Runners
    {
        get
        {
            lock (_lock)
                return S.Instances.Select(s => _runners.GetValueOrDefault(s.Id)).OfType<InstanceRunner>().ToList();
        }
    }

    public InstanceRunner? Get(string? id)
    {
        lock (_lock) return _runners.GetValueOrDefault(string.IsNullOrEmpty(id) ? InstanceSettings.MainId : id);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        stoppingToken.Register(_stoppingSource.Cancel);
        _log.Info(L.T("EnshroudedManager démarré.", "EnshroudedManager started."));
        foreach (var r in Runners.Where(r => r.I.Installed)) r.I.Server.TryAdopt();

        _ = Task.Run(() => TickLoopAsync(stoppingToken), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var ready = true;
            foreach (var r in Runners) ready &= await r.EnsureReadyAsync();
            if (Runners.Any(r => r.I.Installed)) await CheckAndUpdateAsync(auto: true);
            // A failed first install is retried soon; afterwards the normal check interval applies.
            var wait = ready ? TimeSpan.FromMinutes(Math.Max(1, S.CheckIntervalMinutes)) : TimeSpan.FromMinutes(2);
            NextCheck = DateTimeOffset.Now + wait;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _wake.Token);
            try { await Task.Delay(wait, linked.Token); }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { _wake = new(); }
            catch (OperationCanceledException) { }
        }
    }

    /// <summary>Restarts the wait: a new check interval or a new instance is taken into account at once.</summary>
    public void Reschedule() => _wake.Cancel();

    /// <summary>Player counts every 15 s, depot schedules every 15 s.</summary>
    private async Task TickLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var r in Runners)
            {
                try
                {
                    r.Players = r.I.Server.IsRunning ? await SteamQuery.GetPlayersAsync(r.I.Config.QueryPort(), ct) : null;
                    await r.DepotTickAsync();
                }
                catch (Exception ex) when (!ct.IsCancellationRequested) { r.I.Log.Warn(ex.Message); }
            }
            try { await Task.Delay(TimeSpan.FromSeconds(15), ct); } catch { }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // The service stopping (reboot, manual stop) must not leave any world unsaved.
        await Task.WhenAll(Runners.Select(r => r.ShutdownAsync()));
        await base.StopAsync(cancellationToken);
    }

    /// <summary>One Steam check for all instances; each instance out of date is updated in turn.</summary>
    public async Task<string> CheckAndUpdateAsync(bool auto)
    {
        string remote;
        try
        {
            remote = await _steam.GetRemoteBuildAsync(Stopping);
            RemoteBuild = remote;
            LastCheck = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            _log.Warn(L.T("Vérification des mises à jour impossible : ", "Update check failed: ") + ex.Message);
            return L.T("Vérification impossible : ", "Check failed: ") + ex.Message;
        }

        var outdated = Runners.Where(r => r.I.Installed && r.LocalBuild != remote).ToList();
        if (outdated.Count == 0)
        {
            var upToDate = L.T($"Serveur(s) à jour (build {remote}).", $"Server(s) up to date (build {remote}).");
            if (!auto) _log.Info(upToDate);
            return upToDate;
        }
        var available = L.T($"Mise à jour disponible : build {outdated[0].LocalBuild ?? "?"} → {remote}.",
                            $"Update available: build {outdated[0].LocalBuild ?? "?"} → {remote}.");
        if (!auto || !S.AutoUpdate)
        {
            _log.Warn(auto ? available + L.T(" (mise à jour automatique désactivée)", " (automatic updates disabled)") : available);
            return available;
        }
        _log.Event(available);
        var results = new List<string>();
        foreach (var r in outdated) results.Add(await r.UpdateAsync(remote, force: false));
        return string.Join(" ", results);
    }

    // ---------- instances ----------

    public string? ValidateNewInstance(CreateInstanceDto dto, out string installDir)
    {
        installDir = "";
        if (string.IsNullOrWhiteSpace(dto.Name)) return L.T("Donnez un nom à l'instance.", "Give the instance a name.");
        if (string.IsNullOrWhiteSpace(dto.InstallDir)) return L.T("Choisissez un dossier.", "Choose a folder.");
        try { installDir = Path.GetFullPath(dto.InstallDir.Trim()); }
        catch { return L.T("Dossier invalide.", "Invalid folder."); }
        var dir = installDir;
        if (S.Instances.Any(i => string.Equals(Path.GetFullPath(i.InstallDir is { Length: > 0 } d ? d : "?"), dir, StringComparison.OrdinalIgnoreCase)))
            return L.T("Ce dossier est déjà utilisé par une autre instance.", "This folder is already used by another instance.");
        if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any() && !File.Exists(Path.Combine(dir, "enshrouded_server.exe")))
            return L.T("Ce dossier n'est pas vide : choisissez un dossier vide ou un serveur Enshrouded existant.",
                       "This folder is not empty: choose an empty folder or an existing Enshrouded server.");
        if (dto.QueryPort is < 1024 or > 65535) return L.T("Port de requête invalide.", "Invalid query port.");
        if (Runners.Any(r => r.I.Config.QueryPort() == dto.QueryPort))
            return L.T($"Le port {dto.QueryPort} est déjà utilisé par une autre instance.", $"Port {dto.QueryPort} is already used by another instance.");
        if (IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(e => e.Port == dto.QueryPort))
            return L.T($"Le port UDP {dto.QueryPort} est déjà utilisé sur cette machine.", $"UDP port {dto.QueryPort} is already in use on this machine.");
        if (dto.SlotCount is < 1 or > 16) return L.T("Joueurs max : entre 1 et 16.", "Max players: between 1 and 16.");
        if (string.IsNullOrEmpty(dto.AdminPassword)) return L.T("Choisissez un mot de passe administrateur.", "Choose an admin password.");
        if (dto.AdminPassword == dto.VisitorPassword) return L.T("Le mot de passe admin doit être différent de celui des visiteurs.", "The admin password must differ from the visitor password.");
        if (dto.Depot && dto.ResetEveryMinutes is < 5 or > 1440) return L.T("Reset : entre 5 et 1440 minutes.", "Reset: between 5 and 1440 minutes.");
        if (!File.Exists(Path.Combine(dir, "enshrouded_server.exe")))
        {
            // A full server takes about 8.2 GB; keep some room for saves and updates.
            var free = new DriveInfo(Path.GetPathRoot(dir)!).AvailableFreeSpace;
            if (free < 10L * 1024 * 1024 * 1024)
                return L.T($"Espace disque insuffisant sur {Path.GetPathRoot(dir)} : {free / 1e9:0.0} Go libres, 10 Go nécessaires.",
                           $"Not enough disk space on {Path.GetPathRoot(dir)}: {free / 1e9:0.0} GB free, 10 GB needed.");
        }
        return null;
    }

    public string CreateInstance(CreateInstanceDto dto, string installDir)
    {
        var id = Slug(dto.Name);
        var instance = new InstanceSettings
        {
            Id = id, Name = dto.Name.Trim(), InstallDir = installDir,
            Depot = new DepotSettings { Enabled = dto.Depot, ResetEveryMinutes = dto.ResetEveryMinutes, OnlyWhenEmpty = dto.OnlyWhenEmpty },
        };
        _settings.Update(s => s.Instances.Add(instance));
        var runner = AddRunner(id);

        Directory.CreateDirectory(installDir);
        if (!File.Exists(instance.ConfigPath))
            _provisioner.WriteInitialConfig(instance.ConfigPath, dto.Name, dto.SlotCount, dto.QueryPort, groups =>
            {
                if (dto.Depot) Provisioner.DepotGroups(groups, dto.AdminPassword, dto.VisitorPassword);
                else foreach (var g in groups.OfType<System.Text.Json.Nodes.JsonObject>().Where(g => g["name"]?.ToString() == "Admin")) g["password"] = dto.AdminPassword;
            });
        runner.I.Log.Event(L.T($"Instance « {instance.Name} » créée dans {installDir}.", $"Instance “{instance.Name}” created in {installDir}."));
        Reschedule(); // installs it now
        return id;
    }

    private string Slug(string name)
    {
        var slug = Regex.Replace(name.Normalize(System.Text.NormalizationForm.FormD), @"[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();
        if (slug is "" or InstanceSettings.MainId) slug = "instance";
        var id = slug;
        for (var n = 2; S.Instances.Any(i => i.Id == id); n++) id = $"{slug}-{n}";
        return id;
    }

    /// <summary>Stops and forgets an instance. Its server files are only deleted when asked.</summary>
    public async Task<string> RemoveInstanceAsync(string id, bool deleteFiles)
    {
        if (id == InstanceSettings.MainId) return L.T("Le serveur principal ne peut pas être supprimé.", "The main server cannot be removed.");
        var runner = Get(id);
        if (runner == null) return L.T("Instance introuvable.", "Instance not found.");
        await runner.ShutdownAsync();
        Provisioner.RemoveFirewall(runner.I);
        var name = runner.I.DisplayName;
        var dir = runner.I.InstallDir;
        var dataDir = runner.I.DataDir;
        lock (_lock) _runners.Remove(id);
        _settings.Update(s => s.Instances.RemoveAll(i => i.Id == id));
        if (deleteFiles)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception ex) { _log.Warn(ex.Message); }
            try { if (Directory.Exists(dataDir)) Directory.Delete(dataDir, true); } catch (Exception ex) { _log.Warn(ex.Message); }
        }
        _log.Event(L.T($"Instance « {name} » supprimée{(deleteFiles ? " avec ses fichiers" : "")}.", $"Instance “{name}” removed{(deleteFiles ? " with its files" : "")}."));
        return L.T("Instance supprimée.", "Instance removed.");
    }
}
