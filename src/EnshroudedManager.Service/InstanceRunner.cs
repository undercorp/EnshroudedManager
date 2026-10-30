using System.IO.Compression;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EnshroudedManager.Shared;

namespace EnshroudedManager.Service;

/// <summary>
/// Operations on one instance (install, start, stop, update, config change, restore, depot reset), serialised
/// so they never overlap, plus crash handling.
/// </summary>
public class InstanceRunner
{
    private readonly SteamCmd _steam;
    private readonly Provisioner _provisioner;
    private readonly SemaphoreSlim _op = new(1, 1);
    private readonly Queue<DateTimeOffset> _crashes = new();
    private readonly CancellationToken _stopping;
    private bool _warnedNotConfigured;
    private bool _stoppedByUser; // a manual stop must not be undone by the next check
    private bool _warnedDepotWaiting;

    public InstanceRunner(Instance instance, SteamCmd steam, Provisioner provisioner, CancellationToken stopping)
    {
        I = instance;
        _steam = steam;
        _provisioner = provisioner;
        _stopping = stopping;
        I.Server.Crashed += _ => Task.Run(OnCrashAsync);
    }

    public Instance I { get; }
    public string? Busy { get; private set; }
    public double? Progress { get; private set; }
    public PlayersDto? Players { get; set; }
    private ILog Log => I.Log;
    private InstanceSettings S => I.Settings;
    private bool ShouldRun => S.AutoStart && !_stoppedByUser;

    public string? LocalBuild => _steam.GetLocalBuild(I.InstallDir);

    private string NotInstalled() => L.T("Le serveur n'est pas encore installé.", "The server is not installed yet.");

    /// <summary>Server files, firewall, then start. Returns true when the server is installed.</summary>
    public async Task<bool> EnsureReadyAsync()
    {
        if (!I.Configured)
        {
            if (!_warnedNotConfigured)
                Log.Warn(L.T("Dossier du serveur non configuré : relancez l'installeur.", "Server folder not configured: run the installer again."));
            _warnedNotConfigured = true;
            return false;
        }

        if (!I.Installed)
        {
            var installed = false;
            await RunOp(L.T("Installation du serveur", "Server installation"), async () =>
            {
                I.Server.SetState(ServerState.Installing);
                try
                {
                    void Report(string text, double? pct) { Busy = text; Progress = pct; }
                    await _provisioner.EnsureSteamCmdAsync(Report, _stopping);
                    installed = await _provisioner.InstallServerAsync(I, Report, _stopping);
                    // A depot whose master was imported before the download finished starts on it.
                    if (installed && S.Depot.Enabled && HasMaster) CopyMasterToSave();
                }
                finally
                {
                    Progress = null;
                    I.Server.SetState(ServerState.Stopped);
                }
                return "";
            });
            if (!installed) return false;
        }

        _provisioner.EnsureFirewall(I);
        if (ShouldRun && !I.Server.IsRunning && I.Server.State is ServerState.Stopped)
            await RunOp(L.T("Démarrage", "Starting"), () => { I.Server.Start(); return Task.FromResult(""); });
        return true;
    }

    /// <summary>Stop → backup → steamcmd → start. Aborts before touching the files if the backup fails.</summary>
    public Task<string> UpdateAsync(string? target, bool force) => RunOp(L.T("Mise à jour", "Update"), async () =>
    {
        if (!I.Installed) return NotInstalled();
        var local = LocalBuild;
        if (!force && target != null && local == target) return L.T($"Déjà à jour (build {local}).", $"Already up to date (build {local}).");

        var wasRunning = I.Server.IsRunning;
        I.Server.SetState(ServerState.Updating);
        try
        {
            Busy = L.T("Arrêt du serveur", "Stopping the server");
            await I.Server.StopAsync();

            Busy = L.T("Sauvegarde", "Backup");
            try
            {
                I.Backups.Create("avant-maj");
            }
            catch (Exception ex)
            {
                Log.Error(L.T("Sauvegarde impossible, mise à jour annulée : ", "Backup failed, update cancelled: ") + ex.Message);
                return L.T("Mise à jour annulée : la sauvegarde a échoué (", "Update cancelled: the backup failed (") + ex.Message + ").";
            }

            Busy = L.T("Téléchargement de la mise à jour", "Downloading the update");
            Log.Info(L.T("Mise à jour via steamcmd…", "Updating with steamcmd…"));
            var ok = await _steam.UpdateAsync(I, target, _stopping);
            var now = LocalBuild;
            if (ok)
            {
                if (I.IsMain) I.Update(_ => I.Global.LastUpdate = DateTimeOffset.Now);
                Log.Event(L.T($"Mise à jour installée : build {local ?? "?"} → {now}.", $"Update installed: build {local ?? "?"} → {now}."));
                return L.T($"Mise à jour installée (build {now}).", $"Update installed (build {now}).");
            }
            Log.Error(L.T($"La mise à jour a échoué (build local {now ?? "?"}). Le serveur redémarre sur la version actuelle.",
                          $"The update failed (local build {now ?? "?"}). The server restarts on the current version."));
            return L.T("La mise à jour a échoué, voir le journal.", "The update failed, see the log.");
        }
        finally
        {
            I.Server.SetState(ServerState.Stopped);
            if (wasRunning || ShouldRun)
            {
                Busy = L.T("Démarrage du serveur", "Starting the server");
                try { I.Server.Start(); } catch (Exception ex) { Log.Error(L.T("Démarrage impossible : ", "Could not start: ") + ex.Message); }
            }
        }
    });

    /// <summary>Validates, then stop → backup → write → start.</summary>
    public async Task<ApplyResultDto> ApplyConfigAsync(JsonObject values)
    {
        var check = I.Config.Validate(values, out _);
        if (!check.Ok) return check;

        string? error = null;
        await RunOp(L.T("Application des paramètres", "Applying settings"), async () =>
        {
            var wasRunning = I.Server.IsRunning;
            Busy = L.T("Arrêt du serveur", "Stopping the server");
            await I.Server.StopAsync();
            Busy = L.T("Sauvegarde", "Backup");
            try { I.Backups.Create("avant-config"); }
            catch (Exception ex) { error = L.T("Sauvegarde impossible, rien n'a été modifié : ", "Backup failed, nothing was changed: ") + ex.Message; }

            if (error == null)
            {
                // Re-validate against the file as it is now (the server may have rewritten it on shutdown).
                I.Config.Validate(values, out var root);
                I.Config.Write(root);
                Log.Event(L.T("Paramètres du serveur enregistrés.", "Server settings saved."));
            }
            if ((wasRunning || ShouldRun) && I.Installed)
            {
                Busy = L.T("Démarrage du serveur", "Starting the server");
                I.Server.Start();
            }
            return "";
        });
        if (error != null) Log.Error(error);
        return error == null ? check : new ApplyResultDto(false, [error], check.Warnings);
    }

    public Task<string> RestoreAsync(string name, bool includeConfig) => RunOp(L.T("Restauration", "Restore"), async () =>
    {
        I.Backups.ResolvePath(name);
        var wasRunning = I.Server.IsRunning;
        Busy = L.T("Arrêt du serveur", "Stopping the server");
        await I.Server.StopAsync();
        Busy = L.T("Sauvegarde de l'état actuel", "Backing up the current state");
        I.Backups.Create("avant-restauration");
        Busy = L.T("Restauration", "Restoring");
        I.Backups.Restore(name, includeConfig);
        if (wasRunning || ShouldRun) { Busy = L.T("Démarrage du serveur", "Starting the server"); I.Server.Start(); }
        return L.T("Sauvegarde restaurée.", "Backup restored.");
    });

    public Task<string> BackupNowAsync() => RunOp(L.T("Sauvegarde", "Backup"), () =>
        Task.FromResult(L.T("Sauvegarde créée : ", "Backup created: ") + I.Backups.Create("manuelle").Name));

    public Task<string> StartAsync() => RunOp(L.T("Démarrage", "Starting"), () =>
    {
        _stoppedByUser = false;
        I.Server.Start();
        return Task.FromResult(L.T("Serveur démarré.", "Server started."));
    });

    public Task<string> StopServerAsync() => RunOp(L.T("Arrêt", "Stopping"), async () =>
    {
        _stoppedByUser = true;
        await I.Server.StopAsync();
        return L.T("Serveur arrêté.", "Server stopped.");
    });

    public Task<string> RestartAsync() => RunOp(L.T("Redémarrage", "Restarting"), async () =>
    {
        _stoppedByUser = false;
        await I.Server.StopAsync();
        I.Server.Start();
        return L.T("Serveur redémarré.", "Server restarted.");
    });

    /// <summary>Stops the server for good (instance removal or service shutdown).</summary>
    public async Task ShutdownAsync()
    {
        await _op.WaitAsync(TimeSpan.FromSeconds(10));
        try { await I.Server.StopAsync(); } catch { }
    }

    // ---------- resource depot ----------

    private static readonly Regex OldWorldFile = new(@"^(?<id>[0-9a-f]{8})(?<suffix>-\d+|-index)?$", RegexOptions.IgnoreCase);
    private const string ServerWorldId = "3ad85aea"; // name the dedicated server gives its world in the pre-1.0 format

    public bool HasMaster => Directory.Exists(I.MasterDir) && Directory.EnumerateFiles(I.MasterDir, "*", SearchOption.AllDirectories).Any();
    public DateTimeOffset? MasterTime => HasMaster ? Directory.GetLastWriteTime(I.MasterDir) : null;

    /// <summary>Resets happen on round times (every 30 min: hh:00 and hh:30).</summary>
    public DateTimeOffset? NextReset
    {
        get
        {
            if (!S.Depot.Enabled) return null;
            var every = TimeSpan.FromMinutes(Math.Max(5, S.Depot.ResetEveryMinutes));
            return CurrentSlot(every) + every;
        }
    }

    private static DateTimeOffset CurrentSlot(TimeSpan every)
    {
        var now = DateTimeOffset.Now;
        var midnight = new DateTimeOffset(now.Date, now.Offset);
        return midnight + TimeSpan.FromTicks((now - midnight).Ticks / every.Ticks * every.Ticks);
    }

    /// <summary>Called every few seconds by the supervisor.</summary>
    public async Task DepotTickAsync()
    {
        var depot = S.Depot;
        if (!depot.Enabled || !I.Installed || !HasMaster || Busy != null || _stoppedByUser) return;
        if (depot.LastReset == null) { I.Update(s => s.Depot.LastReset = DateTimeOffset.Now); return; }
        var slot = CurrentSlot(TimeSpan.FromMinutes(Math.Max(5, depot.ResetEveryMinutes)));
        if (depot.LastReset >= slot) return;
        if (depot.OnlyWhenEmpty && Players is { Current: > 0 } p)
        {
            if (!_warnedDepotWaiting) Log.Info(L.T($"Reset du dépôt reporté : {p.Current} joueur(s) connecté(s).", $"Depot reset postponed: {p.Current} player(s) online."));
            _warnedDepotWaiting = true;
            return;
        }
        _warnedDepotWaiting = false;
        await ResetDepotAsync();
    }

    public Task<string> ResetDepotAsync() => RunOp(L.T("Reset du dépôt", "Depot reset"), async () =>
    {
        if (!HasMaster) return L.T("Aucun monde modèle : importez-en un ou définissez le monde actuel comme modèle.",
                                   "No master world: import one or set the current world as master.");
        var wasRunning = I.Server.IsRunning;
        Busy = L.T("Arrêt du serveur", "Stopping the server");
        await I.Server.StopAsync();
        Busy = L.T("Remise en place du monde modèle", "Restoring the master world");
        CopyMasterToSave();
        I.Update(s => s.Depot.LastReset = DateTimeOffset.Now);
        Log.Info(L.T("Dépôt remis à zéro (monde modèle restauré).", "Depot reset (master world restored)."));
        if (wasRunning || ShouldRun) { Busy = L.T("Démarrage du serveur", "Starting the server"); I.Server.Start(); }
        return L.T("Dépôt remis à zéro.", "Depot reset.");
    });

    /// <summary>The current world becomes the reference (e.g. after filling the chests in game).</summary>
    public Task<string> SetMasterFromCurrentAsync() => RunOp(L.T("Définition du modèle", "Setting the master"), async () =>
    {
        var wasRunning = I.Server.IsRunning;
        Busy = L.T("Arrêt du serveur (sauvegarde du monde)", "Stopping the server (saving the world)");
        await I.Server.StopAsync();
        var save = I.Backups.SaveDir;
        if (!Directory.Exists(save) || !Directory.EnumerateFiles(save).Any())
            return L.T("Le monde actuel est vide.", "The current world is empty.");
        ReplaceDir(I.MasterDir, tmp => CopyWorld(save, tmp));
        I.Update(s => s.Depot.LastReset = DateTimeOffset.Now);
        Log.Event(L.T("Le monde actuel est désormais le modèle du dépôt.", "The current world is now the depot master."));
        if (wasRunning || ShouldRun) { Busy = L.T("Démarrage du serveur", "Starting the server"); I.Server.Start(); }
        return L.T("Monde modèle enregistré.", "Master world saved.");
    });

    /// <summary>
    /// Imports a world archive (a dedicated server savegame or a solo world from Steam\userdata\…\1203620\remote)
    /// as the depot master, then puts it in place.
    /// </summary>
    public Task<string> ImportMasterAsync(string zipPath) => RunOp(L.T("Import du monde modèle", "Importing the master world"), async () =>
    {
        var staging = Path.Combine(I.DataDir, "depot-import");
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        try
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var e in zip.Entries.Where(e => e.Name != ""))
                {
                    var target = Path.GetFullPath(Path.Combine(staging, e.FullName));
                    if (!target.StartsWith(staging + Path.DirectorySeparatorChar)) continue; // no path traversal
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    e.ExtractToFile(target, true);
                }
            }
            var summary = "";
            ReplaceDir(I.MasterDir, tmp => summary = ExtractWorld(staging, tmp));
            Log.Event(L.T($"Monde modèle importé ({summary}).", $"Master world imported ({summary})."));
        }
        catch (InvalidDataException ex) { return ex.Message; }
        finally
        {
            try { Directory.Delete(staging, true); } catch { }
        }

        if (!I.Installed) return L.T("Monde modèle importé : il sera utilisé dès la fin de l'installation du serveur.",
                                     "Master world imported: it will be used as soon as the server is installed.");
        var wasRunning = I.Server.IsRunning;
        Busy = L.T("Arrêt du serveur", "Stopping the server");
        await I.Server.StopAsync();
        CopyMasterToSave();
        I.Update(s => s.Depot.LastReset = DateTimeOffset.Now);
        if (wasRunning || ShouldRun) { Busy = L.T("Démarrage du serveur", "Starting the server"); I.Server.Start(); }
        return L.T("Monde modèle importé et mis en place.", "Master world imported and put in place.");
    });

    /// <summary>
    /// Finds the world in an extracted archive and copies it to <paramref name="dest"/> under the names the
    /// dedicated server expects. Returns a short description of what was found.
    /// </summary>
    private static string ExtractWorld(string source, string dest)
    {
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}backup{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // 1.0 format: one .ksd file per world.
        var ksd = files.Where(f => f.EndsWith(".ksd", StringComparison.OrdinalIgnoreCase)).ToList();
        if (ksd.Count == 1)
        {
            File.Copy(ksd[0], Path.Combine(dest, "world0.ksd"));
            return Path.GetFileName(ksd[0]);
        }
        if (ksd.Count > 1)
            throw new InvalidDataException(L.T(
                $"L'archive contient plusieurs mondes ({string.Join(", ", ksd.Select(Path.GetFileName))}) : n'en gardez qu'un.",
                $"The archive holds several worlds ({string.Join(", ", ksd.Select(Path.GetFileName))}): keep only one."));

        // Pre-1.0 format: <id>, <id>-1 … <id>-9, <id>-index. The server converts it on first start.
        var worlds = files.Select(f => (File: f, Match: OldWorldFile.Match(Path.GetFileName(f))))
            .Where(x => x.Match.Success)
            .GroupBy(x => x.Match.Groups["id"].Value.ToLowerInvariant())
            .Where(g => g.Any(x => x.Match.Groups["suffix"].Value == "-index"))
            .ToList();
        if (worlds.Count == 1)
        {
            foreach (var (file, match) in worlds[0])
                File.Copy(file, Path.Combine(dest, ServerWorldId + match.Groups["suffix"].Value));
            return L.T($"monde {worlds[0].Key}, ancien format converti au démarrage", $"world {worlds[0].Key}, old format converted on start");
        }
        if (worlds.Count > 1)
            throw new InvalidDataException(L.T(
                $"L'archive contient plusieurs mondes ({string.Join(", ", worlds.Select(w => w.Key))}) : n'en gardez qu'un.",
                $"The archive holds several worlds ({string.Join(", ", worlds.Select(w => w.Key))}): keep only one."));
        throw new InvalidDataException(L.T(
            "Aucun monde Enshrouded trouvé dans l'archive (fichier .ksd, ou fichiers <id> + <id>-index).",
            "No Enshrouded world found in the archive (.ksd file, or <id> + <id>-index files)."));
    }

    /// <summary>Copies a savegame folder, without the game's own rolling backups.</summary>
    private static void CopyWorld(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(from, file);
            if (rel.StartsWith("backup" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            var target = Path.Combine(to, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }

    /// <summary>Builds the new content beside the old one, then swaps, so a failure never leaves a half-written folder.</summary>
    private static void ReplaceDir(string dir, Action<string> fill)
    {
        var tmp = dir + ".new";
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        fill(tmp);
        var old = dir + ".old";
        if (Directory.Exists(old)) Directory.Delete(old, true);
        if (Directory.Exists(dir)) Directory.Move(dir, old);
        Directory.Move(tmp, dir);
        if (Directory.Exists(old)) Directory.Delete(old, true);
    }

    private void CopyMasterToSave() => ReplaceDir(I.Backups.SaveDir, tmp => CopyWorld(I.MasterDir, tmp));

    // ---------- plumbing ----------

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
                Log.Error(L.T("Le serveur a planté plus de 3 fois en 15 minutes : redémarrage automatique suspendu.",
                              "The server crashed more than 3 times in 15 minutes: automatic restart suspended."));
                return;
            }
        }
        Log.Info(L.T("Redémarrage automatique dans 10 secondes…", "Automatic restart in 10 seconds…"));
        await Task.Delay(TimeSpan.FromSeconds(10));
        if (!I.Server.IsRunning) await StartAsync();
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
            Log.Error($"{name} : {ex.Message}");
            return L.T($"{name} : erreur — {ex.Message}", $"{name}: error — {ex.Message}");
        }
        finally
        {
            Busy = null;
            _op.Release();
        }
    }
}
