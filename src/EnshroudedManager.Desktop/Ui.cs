using EnshroudedManager.Shared;

namespace EnshroudedManager.Desktop;

public static class Ui
{
    public static readonly Color Accent = Color.FromArgb(0xE0, 0x8A, 0x2E);
    public static readonly Color Ok = Color.FromArgb(0x1E, 0x9E, 0x62);
    public static readonly Color Warn = Color.FromArgb(0xC0, 0x8A, 0x10);
    public static readonly Color Err = Color.FromArgb(0xD0, 0x45, 0x40);
    public static readonly Color Info = Color.FromArgb(0x2F, 0x6F, 0xD0);
    public static readonly Color Muted = Color.FromArgb(0x6B, 0x74, 0x84);

    public static Color StateColor(ServerState s) => s switch
    {
        ServerState.Running => Ok,
        ServerState.Stopped or ServerState.Crashed => Err,
        _ => Warn,
    };

    public static Color LevelColor(string level) => level switch
    {
        "warn" => Warn,
        "error" => Err,
        "event" => Info,
        _ => SystemColors.WindowText,
    };

    private static Icon? _icon, _trayIcon;

    /// <summary>Enshrouded icon with the update badge (assets\app.ico, embedded).</summary>
    public static Icon AppIcon => _icon ??= LoadIcon(new Size(256, 256));

    /// <summary>Small frame for the notification area, so Windows doesn't downscale the big one.</summary>
    public static Icon TrayIcon => _trayIcon ??= LoadIcon(SystemInformation.SmallIconSize);

    private static Icon LoadIcon(Size size)
    {
        using var stream = typeof(Ui).Assembly.GetManifestResourceStream("app.ico")!;
        return new Icon(stream, size);
    }

    public static string FormatDate(DateTimeOffset? d) => d?.ToLocalTime().ToString(L.En ? "yyyy-MM-dd HH:mm" : "dd/MM/yyyy HH:mm") ?? "—";

    public static string FormatDuration(TimeSpan t) =>
        t.TotalDays >= 1 ? L.T($"{(int)t.TotalDays} j {t.Hours} h", $"{(int)t.TotalDays} d {t.Hours} h") : t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{t.Minutes} min";

    public static void Error(IWin32Window? owner, string message) =>
        MessageBox.Show(owner, message, "Enshrouded Manager", MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static bool Confirm(IWin32Window? owner, string message, string? title = null) =>
        MessageBox.Show(owner, message, title ?? L.T("Confirmer", "Confirm"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK;

    public static Button Button(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Padding = new Padding(8, 3, 8, 3), Margin = new Padding(0, 0, 8, 8) };
        b.Click += onClick;
        return b;
    }
}
