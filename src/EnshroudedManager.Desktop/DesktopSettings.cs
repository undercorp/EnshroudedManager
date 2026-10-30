using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EnshroudedManager.Desktop;

/// <summary>Per-user settings in %AppData%; the token is encrypted with DPAPI.</summary>
public class DesktopSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EnshroudedManager", "desktop.json");

    public string ServerUrl { get; set; } = $"http://localhost:{LocalServicePort()}";

    /// <summary>Port of the service installed alongside (installer layout: {app}\Desktop next to {app}\data).</summary>
    private static int LocalServicePort()
    {
        try
        {
            var file = Path.Combine(AppContext.BaseDirectory, "..", "data", "manager.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            return doc.RootElement.GetProperty("Port").GetInt32();
        }
        catch { return 8090; }
    }
    public string? ProtectedToken { get; set; }
    public bool NotifyEvents { get; set; } = true;
    /// <summary>Instance shown last time.</summary>
    public string Instance { get; set; } = "main";

    public string? Token
    {
        get
        {
            if (ProtectedToken == null) return null;
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(ProtectedToken), null, DataProtectionScope.CurrentUser)); }
            catch { return null; }
        }
        set => ProtectedToken = value == null ? null
            : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }

    public static DesktopSettings Load()
    {
        try { return JsonSerializer.Deserialize<DesktopSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
