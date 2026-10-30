using EnshroudedManager.Shared;

namespace EnshroudedManager.Service;

/// <summary>Everything that belongs to one dedicated server: its settings, process, config file and backups.</summary>
public class Instance
{
    private readonly SettingsStore _store;

    public Instance(SettingsStore store, LogBuffer log, string id)
    {
        _store = store;
        Id = id;
        Log = new ScopedLog(log, () => IsMain ? "" : $"[{DisplayName}] ");
        Server = new GameServer(this);
        Config = new ConfigStore(this);
        Backups = new BackupStore(this);
    }

    public string Id { get; }
    public InstanceSettings Settings => _store.Current.Instances.First(i => i.Id == Id);
    public ManagerSettings Global => _store.Current;
    public bool IsMain => Id == InstanceSettings.MainId;
    public string DisplayName => Settings.Name is { Length: > 0 } n ? n : IsMain ? L.T("Serveur principal", "Main server") : Id;

    public string InstallDir => Settings.InstallDir;
    public string ExePath => Settings.ExePath;
    public string ConfigPath => Settings.ConfigPath;
    public bool Configured => !string.IsNullOrWhiteSpace(InstallDir);
    public bool Installed => Configured && File.Exists(ExePath);

    /// <summary>Per-instance files of the manager (backups, depot master). The main instance keeps the 1.0 layout.</summary>
    public string DataDir => IsMain ? _store.DataDir : Path.Combine(_store.DataDir, "instances", Id);
    public string MasterDir => Path.Combine(DataDir, "depot-master");

    public ILog Log { get; }
    public GameServer Server { get; }
    public ConfigStore Config { get; }
    public BackupStore Backups { get; }

    public void Update(Action<InstanceSettings> change) => _store.Update(_ => change(Settings));
}
