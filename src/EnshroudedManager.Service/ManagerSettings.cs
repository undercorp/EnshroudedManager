using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnshroudedManager.Service;

public class StoredToken
{
    public string Hash { get; set; } = "";
    public DateTimeOffset Expires { get; set; }
}

/// <summary>Settings of the manager itself, persisted in data\manager.json.</summary>
public class ManagerSettings
{
    public int Port { get; set; } = 8090;

    /// <summary>true: web page served and API reachable from the network. false: API on 127.0.0.1 only, for the desktop app.</summary>
    public bool WebEnabled { get; set; } = true;

    public string Language { get; set; } = Shared.L.Default();

    /// <summary>Folder that holds enshrouded_server.exe. Empty until the installer sets it.</summary>
    public string InstallDir { get; set; } = "";

    /// <summary>steamcmd.exe; downloaded there automatically when missing.</summary>
    public string SteamCmdPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "steamcmd", "steamcmd.exe");

    public string AppId { get; set; } = "2278520";
    public string Branch { get; set; } = "public";
    public int CheckIntervalMinutes { get; set; } = 10;
    public bool AutoUpdate { get; set; } = true;
    public bool AutoStart { get; set; } = true;
    public bool AutoRestart { get; set; } = true;
    public int BackupsToKeep { get; set; } = 20;
    public int StopTimeoutSeconds { get; set; } = 45;
    public string? PasswordHash { get; set; }
    public List<StoredToken> Tokens { get; set; } = new();
    public DateTimeOffset? LastUpdate { get; set; }

    [JsonIgnore] public string ExePath => Path.Combine(InstallDir, "enshrouded_server.exe");
    [JsonIgnore] public string ConfigPath => Path.Combine(InstallDir, "enshrouded_server.json");
}

public class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _lock = new();

    public string DataDir { get; }
    public string FilePath { get; }
    public ManagerSettings Current { get; private set; }

    public SettingsStore()
    {
        DataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(DataDir);
        FilePath = Path.Combine(DataDir, "manager.json");
        Current = File.Exists(FilePath)
            ? JsonSerializer.Deserialize<ManagerSettings>(File.ReadAllText(FilePath)) ?? new()
            : new ManagerSettings();
        Shared.L.Set(Current.Language);
        Save();
    }

    public void Update(Action<ManagerSettings> change)
    {
        lock (_lock)
        {
            change(Current);
            Shared.L.Set(Current.Language);
            Save();
        }
    }

    private void Save()
    {
        lock (_lock)
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
    }
}
