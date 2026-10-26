using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using EnshroudedManager.Service;
using EnshroudedManager.Shared;

var settings = new SettingsStore();

if (args.Contains("--reset-password"))
{
    new Auth(settings).Reset();
    Console.WriteLine(L.T("Mot de passe réinitialisé : il sera demandé à la prochaine ouverture de l'interface.",
                          "Password reset: it will be asked the next time the interface is opened."));
    return;
}

var log = new LogBuffer(2000, Path.Combine(settings.DataDir, "logs"), "manager");

// Choices made in the installer (server folder, port, web on/off, password…) arrive through data\setup.json.
var auth = new Auth(settings);
new Provisioner(settings, log, new SteamCmd(settings, log)).ApplySetupFile(auth);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory, // services start in System32 otherwise
});
builder.Host.UseWindowsService(o => o.ServiceName = "EnshroudedManager");
// Without the web interface the API only listens locally, for the desktop app.
builder.WebHost.UseUrls($"http://{(settings.Current.WebEnabled ? "0.0.0.0" : "127.0.0.1")}:{settings.Current.Port}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(log);
builder.Services.AddSingleton(auth);
builder.Services.AddSingleton<GameServer>();
builder.Services.AddSingleton<SteamCmd>();
builder.Services.AddSingleton<ConfigStore>();
builder.Services.AddSingleton<BackupStore>();
builder.Services.AddSingleton<Provisioner>();
builder.Services.AddSingleton<Supervisor>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Supervisor>());

var app = builder.Build();
if (settings.Current.WebEnabled)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

// Everything under /api needs a token, except what is used to obtain one.
string[] open = ["/api/auth/state", "/api/auth/setup", "/api/auth/login"];
app.Use(async (ctx, next) =>
{
    var path = ctx.Request.Path.Value ?? "";
    if (path.StartsWith("/api/") && !open.Contains(path))
    {
        var header = ctx.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ") || !auth.ValidateToken(header[7..]))
        {
            ctx.Response.StatusCode = 401;
            return;
        }
    }
    await next();
});

string Client(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
string MinLength() => L.T("8 caractères minimum.", "At least 8 characters.");

var api = app.MapGroup("/api");

api.MapGet("/auth/state", () => new AuthStateDto(auth.Configured, settings.Current.Language, settings.Current.WebEnabled));
api.MapPost("/auth/setup", (PasswordDto dto) =>
{
    if (auth.Configured) return Results.Conflict(new MessageDto(L.T("Un mot de passe est déjà défini.", "A password is already set.")));
    if (dto.Password.Length < 8) return Results.BadRequest(new MessageDto(MinLength()));
    auth.SetPassword(dto.Password);
    log.Event(L.T("Mot de passe de l'interface défini.", "Interface password set."));
    return Results.Ok(new TokenDto(auth.IssueToken()));
});
api.MapPost("/auth/login", async (PasswordDto dto, HttpContext ctx) =>
{
    var token = await auth.LoginAsync(dto.Password, Client(ctx));
    if (token == null)
    {
        log.Warn(L.T($"Échec de connexion depuis {Client(ctx)}.", $"Failed login from {Client(ctx)}."));
        return Results.Json(new MessageDto(L.T("Mot de passe incorrect (ou trop d'essais, réessaie dans 5 minutes).",
            "Wrong password (or too many attempts, try again in 5 minutes).")), statusCode: 401);
    }
    return Results.Ok(new TokenDto(token));
});
api.MapPost("/auth/logout", (HttpContext ctx) =>
{
    auth.Revoke(ctx.Request.Headers.Authorization.ToString()[7..]);
    return Results.Ok();
});
api.MapPost("/auth/password", (ChangePasswordDto dto) =>
{
    if (!auth.CheckPassword(dto.Current)) return Results.BadRequest(new MessageDto(L.T("Mot de passe actuel incorrect.", "Current password is wrong.")));
    if (dto.New.Length < 8) return Results.BadRequest(new MessageDto(MinLength()));
    auth.SetPassword(dto.New);
    log.Event(L.T("Mot de passe de l'interface modifié.", "Interface password changed."));
    return Results.Ok(new TokenDto(auth.IssueToken()));
});

api.MapGet("/status", (Supervisor sup, GameServer server, SteamCmd steam, ConfigStore config) =>
{
    var local = steam.GetLocalBuild();
    return new StatusDto(
        server.State, Labels.State(server.State), server.Pid, server.StartedAt, server.MemoryMb, sup.Players,
        config.ServerName(), local, sup.RemoteBuild, sup.RemoteBuild != null && local != null && local != sup.RemoteBuild,
        sup.LastCheck, sup.NextCheck, settings.Current.LastUpdate, sup.Busy, sup.Progress, settings.Current.AutoUpdate,
        config.Version, log.LastId);
});
api.MapGet("/logs", (long? after) => log.Since(after ?? 0));

api.MapPost("/server/start", async (Supervisor sup) => new MessageDto(await sup.StartAsync()));
api.MapPost("/server/stop", async (Supervisor sup) => new MessageDto(await sup.StopServerAsync()));
api.MapPost("/server/restart", async (Supervisor sup) => new MessageDto(await sup.RestartAsync()));
api.MapPost("/update/check", async (Supervisor sup) => new MessageDto(await sup.CheckAndUpdateAsync(auto: false)));
api.MapPost("/update/run", async (Supervisor sup) => new MessageDto(await sup.UpdateAsync(sup.RemoteBuild, force: true)));

api.MapGet("/config/schema", (ConfigStore config) => config.Schema());
api.MapGet("/config", (ConfigStore config) =>
{
    try { return Results.Ok(config.Read()); }
    catch (Exception) { return Results.NotFound(new MessageDto(L.T("Le fichier de configuration du serveur n'existe pas encore.", "The server configuration file does not exist yet."))); }
});
api.MapPost("/config/validate", (JsonObject values, ConfigStore config) => config.Validate(values, out _));
api.MapPut("/config", async (JsonObject values, Supervisor sup) => await sup.ApplyConfigAsync(values, restart: true));

api.MapGet("/backups", (BackupStore b) => b.List());
api.MapPost("/backups", async (Supervisor sup) => new MessageDto(await sup.BackupNowAsync()));
api.MapPost("/backups/{name}/restore", async (string name, bool? config, Supervisor sup) =>
    new MessageDto(await sup.RestoreAsync(name, config ?? false)));
api.MapGet("/backups/{name}", (string name, BackupStore b) => Results.File(b.ResolvePath(name), "application/zip", name));

api.MapGet("/settings", () =>
{
    var s = settings.Current;
    return new ManagerSettingsDto(s.CheckIntervalMinutes, s.AutoUpdate, s.AutoRestart, s.BackupsToKeep, s.Language);
});
api.MapPut("/settings", (ManagerSettingsDto dto, Supervisor sup) =>
{
    if (dto.CheckIntervalMinutes is < 1 or > 1440) return Results.BadRequest(new MessageDto(L.T("Intervalle : entre 1 et 1440 minutes.", "Interval: between 1 and 1440 minutes.")));
    if (dto.BackupsToKeep is < 3 or > 500) return Results.BadRequest(new MessageDto(L.T("Sauvegardes à garder : entre 3 et 500.", "Backups to keep: between 3 and 500.")));
    settings.Update(s =>
    {
        s.CheckIntervalMinutes = dto.CheckIntervalMinutes;
        s.AutoUpdate = dto.AutoUpdate;
        s.AutoRestart = dto.AutoRestart;
        s.BackupsToKeep = dto.BackupsToKeep;
        if (dto.Language is "fr" or "en") s.Language = dto.Language;
    });
    sup.Reschedule();
    log.Info(L.T($"Réglages de l'outil modifiés (vérification toutes les {dto.CheckIntervalMinutes} min, MAJ auto {(dto.AutoUpdate ? "activée" : "désactivée")}).",
                 $"Tool settings changed (check every {dto.CheckIntervalMinutes} min, auto update {(dto.AutoUpdate ? "on" : "off")})."));
    return Results.Ok(new MessageDto(L.T("Réglages enregistrés.", "Settings saved.")));
});

app.Run();
