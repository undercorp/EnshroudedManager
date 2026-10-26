using System.Text.Json.Nodes;
using EnshroudedManager.Shared;

namespace EnshroudedManager.Service;

/// <summary>
/// Installs the server on first run, runs the update loop and serialises every operation that touches
/// the server (install, start, stop, update, config change, restore) so they never overlap.
/// </summary>
public class Supervisor : BackgroundService
{
    private readonly SettingsStore _settings;
    private readonly LogBuffer _log;
    private readonly GameServer _server;
    private readonly SteamCmd _steam;
    private readonly ConfigStore _config;
    private readonly BackupStore _backups;
    private readonly Provisioner _provisioner;
    private readonly SemaphoreSlim _op = new(1, 1);
    private readonly Queue<DateTimeOffset> _crashes = new();
    private CancellationTokenSource _wake = new();
    private CancellationToken _stopping;
    private bool _warnedNotConfigured;
    private bool _stoppedByUser; // a manual stop must not be undone by the next check

    public string? Busy { get; private set; }
    public double? Progress { get; private set; }
    public string? RemoteBuild { get; private set; }
    public DateTimeOffset? LastCheck { get; private set; }
    public DateTimeOffset? NextCheck { get; private set; }
    public PlayersDto? Players { get; private set; }

    public Supervisor(SettingsStore settings, LogBuffer log, GameServer server, SteamCmd steam, ConfigStore config,
        BackupStore backups, Provisioner provisioner)
    {
        _settings = settings;
        _log = log;
        _server = server;
        _steam = steam;
        _config = config;
        _backups = backups;
        _provisioner = provisioner;
        _server.Crashed += _ => Task.Run(OnCrashAsync);
    }

    private ManagerSettings S => _settings.Current;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;
        _log.Info(L.T("EnshroudedManager démarré.", "EnshroudedManager started."));
        if (_provisioner.ServerInstalled) _server.TryAdopt();

        _ = Task.Run(() => PollPlayersAsync(stoppingToken), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var ready = await EnsureReadyAsync();
            if (ready) await CheckAndUpdateAsync(auto: true);
            // A failed first install is retried soon; afterwards the normal check interval applies.
            var wait = ready || !_provisioner.Configured ? TimeSpan.FromMinutes(Math.Max(1, S.CheckIntervalMinutes)) : TimeSpan.FromMinutes(2);
            NextCheck = DateTimeOffset.Now + wait;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _wake.Token);
            try { await Task.Delay(wait, linked.Token); }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested) { _wake = new(); }
            catch (OperationCanceledException) { }
        }
    }

    /// <summary>steamcmd, server files, firewall, then start. Returns true when the server is installed.</summary>
    private async Task<bool> EnsureReadyAsync()
    {
        if (!_provisioner.Configured)
        {
            if (!_warnedNotConfigured)
                _log.Warn(L.T("Dossier du serveur non configuré : relancez l'installeur.", "Server folder not configured: run the installer again."));
            _warnedNotConfigured = true;
            return false;
        }

        if (!_provisioner.ServerInstalled)
        {
            var installed = false;
            await RunOp(L.T("Installation du serveur", "Server installation"), async () =>
            {
                _server.SetState(ServerState.Installing);
                try
                {
                    void Report(string text, double? pct) { Busy = text; Progress = pct; }
                    await _provisioner.EnsureSteamCmdAsync(Report, _stopping);
                    installed = await _provisioner.InstallServerAsync(Report, _stopping);
                }
                finally
                {
                    Progress = null;
                    _server.SetState(ServerState.Stopped);
                }
                return "";
            });
            if (!installed) return false;
        }
        else
        {
            try { await _provisioner.EnsureSteamCmdAsync((t, _) => Busy = t, _stopping); }
            catch (Exception ex) { _log.Warn("SteamCMD : " + ex.Message); }
        }

        _provisioner.EnsureFirewall();
        if (S.AutoStart && !_stoppedByUser && !_server.IsRunning && _server.State is ServerState.Stopped)
            await RunOp(L.T("Démarrage", "Starting"), () => { _server.Start(); return Task.FromResult(""); });
        return true;
    }

    /// <summary>Restarts the wait so a new check interval takes effect immediately.</summary>
    public void Reschedule() => _wake.Cancel();

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // The service stopping (reboot, manual stop) must not leave the world unsaved.
        await _op.WaitAsync(TimeSpan.FromSeconds(10));
        try { await _server.StopAsync(); } catch { }
        await base.StopAsync(cancellationToken);
    }

    private async Task PollPlayersAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Players = _server.IsRunning ? await SteamQuery.GetPlayersAsync(_config.QueryPort(), ct) : null;
            try { await Task.Delay(TimeSpan.FromSeconds(15), ct); } catch { }
        }
    }

    private string NotInstalled() => L.T("Le serveur n'est pas encore installé.", "The server is not installed yet.");

    public async Task<string> CheckAndUpdateAsync(bool auto)
    {
        if (!_provisioner.ServerInstalled) return NotInstalled();
        string? remote;
        try
        {
            remote = await _steam.GetRemoteBuildAsync(_stopping);
            RemoteBuild = remote;
            LastCheck = DateTimeOffset.Now;
        }
        catch (Exception ex)
        {
            _log.Warn(L.T("Vérification des mises à jour impossible : ", "Update check failed: ") + ex.Message);
            return L.T("Vérification impossible : ", "Check failed: ") + ex.Message;
        }

        var local = _steam.GetLocalBuild();
        if (local == remote)
        {
            var upToDate = L.T($"Serveur à jour (build {local}).", $"Server up to date (build {local}).");
            if (!auto) _log.Info(upToDate);
            return upToDate;
        }
        var available = L.T($"Mise à jour disponible : build {local ?? "?"} → {remote}.", $"Update available: build {local ?? "?"} → {remote}.");
        if (!auto || !S.AutoUpdate)
        {
            _log.Warn(auto ? available + L.T(" (mise à jour automatique désactivée)", " (automatic updates disabled)") : available);
            return available;
        }
        _log.Event(available);
        return await UpdateAsync(remote, force: false);
    }

    /// <summary>Stop → backup → steamcmd → start. Aborts before touching the files if the backup fails.</summary>
    public Task<string> UpdateAsync(string? target, bool force) => RunOp(L.T("Mise à jour", "Update"), async () =>
    {
        if (!_provisioner.ServerInstalled) return NotInstalled();
        var local = _steam.GetLocalBuild();
        if (!force && target != null && local == target) return L.T($"Déjà à jour (build {local}).", $"Already up to date (build {local}).");

        var wasRunning = _server.IsRunning;
        _server.SetState(ServerState.Updating);
        try
        {
            Busy = L.T("Arrêt du serveur", "Stopping the server");
            await _server.StopAsync();

            Busy = L.T("Sauvegarde", "Backup");
            try
            {
                _backups.Create("avant-maj");
            }
            catch (Exception ex)
            {
                _log.Error(L.T("Sauvegarde impossible, mise à jour annulée : ", "Backup failed, update cancelled: ") + ex.Message);
                return L.T("Mise à jour annulée : la sauvegarde a échoué (", "Update cancelled: the backup failed (") + ex.Message + ").";
            }

            Busy = L.T("Téléchargement de la mise à jour", "Downloading the update");
            _log.Info(L.T("Mise à jour via steamcmd…", "Updating with steamcmd…"));
            var ok = await _steam.UpdateAsync(target, _stopping);
            var now = _steam.GetLocalBuild();
            if (ok)
            {
                _settings.Update(s => s.LastUpdate = DateTimeOffset.Now);
                _log.Event(L.T($"Mise à jour installée : build {local ?? "?"} → {now}.", $"Update installed: build {local ?? "?"} → {now}."));
                return L.T($"Mise à jour installée (build {now}).", $"Update installed (build {now}).");
            }
            _log.Error(L.T($"La mise à jour a échoué (build local {now ?? "?"}). Le serveur redémarre sur la version actuelle.",
                           $"The update failed (local build {now ?? "?"}). The server restarts on the current version."));
            return L.T("La mise à jour a échoué, voir le journal.", "The update failed, see the log.");
        }
        finally
        {
            _server.SetState(ServerState.Stopped);
            if (wasRunning || (S.AutoStart && !_stoppedByUser))
            {
                Busy = L.T("Démarrage du serveur", "Starting the server");
                try { _server.Start(); } catch (Exception ex) { _log.Error(L.T("Démarrage impossible : ", "Could not start: ") + ex.Message); }
            }
        }
    });

    /// <summary>Validates, then stop → backup → write → start.</summary>
    public async Task<ApplyResultDto> ApplyConfigAsync(JsonObject values, bool restart)
    {
        var check = _config.Validate(values, out _);
        if (!check.Ok) return check;

        string? error = null;
        await RunOp(L.T("Application des paramètres", "Applying settings"), async () =>
        {
            var wasRunning = _server.IsRunning;
            Busy = L.T("Arrêt du serveur", "Stopping the server");
            if (restart) await _server.StopAsync();
            Busy = L.T("Sauvegarde", "Backup");
            try { _backups.Create("avant-config"); }
            catch (Exception ex) { error = L.T("Sauvegarde impossible, rien n'a été modifié : ", "Backup failed, nothing was changed: ") + ex.Message; }

            if (error == null)
            {
                // Re-validate against the file as it is now (the server may have rewritten it on shutdown).
                _config.Validate(values, out var root);
                _config.Write(root);
                _log.Event(L.T("Paramètres du serveur enregistrés.", "Server settings saved."));
            }
            if (restart && (wasRunning || (S.AutoStart && !_stoppedByUser)) && _provisioner.ServerInstalled)
            {
                Busy = L.T("Démarrage du serveur", "Starting the server");
                _server.Start();
            }
            return "";
        });
        if (error != null) _log.Error(error);
        return error == null ? check : new ApplyResultDto(false, [error], check.Warnings);
    }

    public Task<string> RestoreAsync(string name, bool includeConfig) => RunOp(L.T("Restauration", "Restore"), async () =>
    {
        _backups.ResolvePath(name);
        var wasRunning = _server.IsRunning;
        Busy = L.T("Arrêt du serveur", "Stopping the server");
        await _server.StopAsync();
        Busy = L.T("Sauvegarde de l'état actuel", "Backing up the current state");
        _backups.Create("avant-restauration");
        Busy = L.T("Restauration", "Restoring");
        _backups.Restore(name, includeConfig);
        if (wasRunning || (S.AutoStart && !_stoppedByUser)) { Busy = L.T("Démarrage du serveur", "Starting the server"); _server.Start(); }
        return L.T("Sauvegarde restaurée.", "Backup restored.");
    });

    public Task<string> BackupNowAsync() => RunOp(L.T("Sauvegarde", "Backup"), () =>
        Task.FromResult(L.T("Sauvegarde créée : ", "Backup created: ") + _backups.Create("manuelle").Name));

    public Task<string> StartAsync() => RunOp(L.T("Démarrage", "Starting"), () =>
    {
        _stoppedByUser = false;
        _server.Start();
        return Task.FromResult(L.T("Serveur démarré.", "Server started."));
    });

    public Task<string> StopServerAsync() => RunOp(L.T("Arrêt", "Stopping"), async () =>
    {
        _stoppedByUser = true;
        await _server.StopAsync();
        return L.T("Serveur arrêté.", "Server stopped.");
    });

    public Task<string> RestartAsync() => RunOp(L.T("Redémarrage", "Restarting"), async () =>
    {
        _stoppedByUser = false;
        await _server.StopAsync();
        _server.Start();
        return L.T("Serveur redémarré.", "Server restarted.");
    });

    private async Task OnCrashAsync()
    {
        if (!S.AutoRestart) return;
        var now = DateTimeOffset.Now;
        lock (_crashes)
        {
            _crashes.Enqueue(now);
            while (_crashes.Count > 0 && now - _crashes.Peek() > TimeSpan.FromMinutes(15)) _crashes.Dequeue();
            if (_crashes.Count > 3)
            {
                _log.Error(L.T("Le serveur a planté plus de 3 fois en 15 minutes : redémarrage automatique suspendu.",
                               "The server crashed more than 3 times in 15 minutes: automatic restart suspended."));
                return;
            }
        }
        _log.Info(L.T("Redémarrage automatique dans 10 secondes…", "Automatic restart in 10 seconds…"));
        await Task.Delay(TimeSpan.FromSeconds(10));
        if (!_server.IsRunning) await StartAsync();
    }

    private async Task<string> RunOp(string name, Func<Task<string>> op)
    {
        if (!await _op.WaitAsync(TimeSpan.FromSeconds(1)))
            return L.T($"Une autre opération est en cours ({Busy}).", $"Another operation is in progress ({Busy}).");
        Busy = name;
        try
        {
            return await op();
        }
        catch (Exception ex)
        {
            _log.Error($"{name} : {ex.Message}");
            return L.T($"{name} : erreur — {ex.Message}", $"{name}: error — {ex.Message}");
        }
        finally
        {
            Busy = null;
            _op.Release();
        }
    }
}
