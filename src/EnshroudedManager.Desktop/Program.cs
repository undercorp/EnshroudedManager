using EnshroudedManager.Shared;

namespace EnshroudedManager.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var single = new Mutex(true, @"Local\EnshroudedManager.Desktop", out var first);
        if (!first)
        {
            MessageBox.Show(L.T("Enshrouded Manager est déjà ouvert (voir la zone de notification).", "Enshrouded Manager is already open (see the notification area)."), "Enshrouded Manager");
            return;
        }

        ApplicationConfiguration.Initialize();
        var settings = DesktopSettings.Load();
        UseServiceLanguage(settings.ServerUrl);

        var api = TryExistingToken(settings);
        if (api == null)
        {
            using var login = new LoginForm(settings.ServerUrl);
            if (login.ShowDialog() != DialogResult.OK || login.Client == null) return;
            api = login.Client;
            settings.ServerUrl = api.BaseUrl;
            settings.Token = api.Token;
            settings.Save();
            UseServiceLanguage(api.BaseUrl);
        }

        Application.Run(new MainForm(settings, api));
    }

    /// <summary>The language is a service setting (chosen in the installer), so the app and the web page always match.</summary>
    private static void UseServiceLanguage(string url)
    {
        try { L.Set(new ApiClient(url, null).Get<AuthStateDto>("auth/state").GetAwaiter().GetResult().Language); }
        catch { /* service unreachable: keep the Windows language */ }
    }

    private static ApiClient? TryExistingToken(DesktopSettings settings)
    {
        if (settings.Token is not { } token) return null;
        var api = new ApiClient(settings.ServerUrl, token);
        try
        {
            api.Get<StatusDto>("status").GetAwaiter().GetResult();
            return api;
        }
        catch { return null; }
    }
}
