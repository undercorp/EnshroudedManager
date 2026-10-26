using System.IO.Compression;
using System.Text.Json.Nodes;
using EnshroudedManager.Shared;

namespace EnshroudedManager.Service;

/// <summary>Zip archives of the savegame folder + enshrouded_server.json, in data\backups.</summary>
public class BackupStore
{
    private const string ConfigEntry = "enshrouded_server.json";
    private readonly SettingsStore _settings;
    private readonly LogBuffer _log;

    public BackupStore(SettingsStore settings, LogBuffer log)
    {
        _settings = settings;
        _log = log;
        Directory.CreateDirectory(Dir);
    }

    public string Dir => Path.Combine(_settings.DataDir, "backups");

    /// <summary>Save directory as configured in enshrouded_server.json (relative to the install dir).</summary>
    public string SaveDir
    {
        get
        {
            var s = _settings.Current;
            string? rel = null;
            try { rel = JsonNode.Parse(File.ReadAllText(s.ConfigPath))?["saveDirectory"]?.ToString(); } catch { }
            return Path.GetFullPath(Path.Combine(s.InstallDir, string.IsNullOrWhiteSpace(rel) ? "savegame" : rel));
        }
    }

    public BackupDto[] List() =>
        new DirectoryInfo(Dir).GetFiles("*.zip")
            .OrderByDescending(f => f.Name)
            .Select(f => new BackupDto(f.Name, f.CreationTime, f.Length, ReasonOf(f.Name)))
            .ToArray();

    private static string ReasonOf(string name)
    {
        var parts = Path.GetFileNameWithoutExtension(name).Split('_', 3);
        return parts.Length == 3 ? parts[2] : "";
    }

    /// <summary>Creates and verifies a backup. Throws when anything goes wrong, so callers can abort.</summary>
    public BackupDto Create(string reason)
    {
        var saveDir = SaveDir;
        if (!Directory.Exists(saveDir)) throw new DirectoryNotFoundException(L.T($"Dossier de sauvegarde introuvable : {saveDir}", $"Save folder not found: {saveDir}"));

        var name = $"{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{reason}.zip";
        var path = Path.Combine(Dir, name);
        var tmp = path + ".tmp";
        var expected = 0;
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(saveDir, "*", SearchOption.AllDirectories))
            {
                zip.CreateEntryFromFile(file, "savegame/" + Path.GetRelativePath(saveDir, file).Replace('\\', '/'), CompressionLevel.Optimal);
                expected++;
            }
            if (File.Exists(_settings.Current.ConfigPath))
            {
                zip.CreateEntryFromFile(_settings.Current.ConfigPath, ConfigEntry);
                expected++;
            }
        }

        // Re-open the archive to make sure it is readable before trusting it.
        using (var check = ZipFile.OpenRead(tmp))
        {
            if (check.Entries.Count != expected || !check.Entries.Any(e => e.FullName.StartsWith("savegame/")))
                throw new InvalidDataException(L.T("La sauvegarde créée est incomplète.", "The backup archive is incomplete."));
        }
        File.Move(tmp, path);

        var info = new FileInfo(path);
        _log.Info(L.T($"Sauvegarde créée : {name} ({info.Length / 1024} Ko).", $"Backup created: {name} ({info.Length / 1024} KB)."));
        Prune();
        return new BackupDto(name, info.CreationTime, info.Length, reason);
    }

    private void Prune()
    {
        var keep = Math.Max(3, _settings.Current.BackupsToKeep);
        foreach (var old in new DirectoryInfo(Dir).GetFiles("*.zip").OrderByDescending(f => f.Name).Skip(keep))
        {
            try { old.Delete(); _log.Info(L.T($"Ancienne sauvegarde supprimée : {old.Name}", $"Old backup deleted: {old.Name}")); } catch { }
        }
    }

    /// <summary>Replaces the savegame folder (and optionally the config) with the archive content. The server must be stopped.</summary>
    public void Restore(string name, bool includeConfig)
    {
        var path = ResolvePath(name);
        var saveDir = SaveDir;
        var staging = saveDir + ".restore";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);

        using (var zip = ZipFile.OpenRead(path))
        {
            foreach (var e in zip.Entries.Where(e => e.FullName.StartsWith("savegame/") && e.Name != ""))
            {
                var target = Path.GetFullPath(Path.Combine(staging, e.FullName["savegame/".Length..]));
                if (!target.StartsWith(staging + Path.DirectorySeparatorChar)) continue; // no path traversal
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target, true);
            }
            if (includeConfig && zip.GetEntry(ConfigEntry) is { } cfg)
                cfg.ExtractToFile(_settings.Current.ConfigPath, true);
        }

        var old = saveDir + ".old";
        if (Directory.Exists(old)) Directory.Delete(old, true);
        if (Directory.Exists(saveDir)) Directory.Move(saveDir, old);
        Directory.Move(staging, saveDir);
        Directory.Delete(old, true);
        _log.Event(L.T($"Sauvegarde restaurée : {name}{(includeConfig ? " (avec la configuration)" : "")}.", $"Backup restored: {name}{(includeConfig ? " (including settings)" : "")}."));
    }

    public string ResolvePath(string name)
    {
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !name.EndsWith(".zip"))
            throw new ArgumentException(L.T("Nom de sauvegarde invalide.", "Invalid backup name."));
        var path = Path.Combine(Dir, name);
        if (!File.Exists(path)) throw new FileNotFoundException(L.T("Sauvegarde introuvable.", "Backup not found."), name);
        return path;
    }
}
