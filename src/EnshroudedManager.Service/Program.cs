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

// Used by the uninstaller: firewall rules of every instance, and the server folders it may offer to delete.
if (args.Contains("--uninstall-cleanup"))
{
    var cleanupLog = new LogBuffer(10);
    foreach (var s in settings.Current.Instances) Provisioner.RemoveFirewall(new Instance(settings, cleanupLog, s.Id));
    var listFile = args.SkipWhile(a => a != "--uninstall-cleanup").Skip(1).FirstOrDefault();
    if (listFile != null)
        File.WriteAllLines(listFile, settings.Current.Instances.Select(i => i.InstallDir).Where(d => d.Length > 0 && Directory.Exists(d)));
    return;
}

var log = new LogBuffer(2000, Path.Combine(settings.DataDir, "logs"), "manager");

// Choices made in the installer (server folder, port, web on/off, password…) arrive through data\setup.json.
var auth = new Auth(settings);
var steam = new SteamCmd(settings, log);
var provisioner = new Provisioner(settings, log, steam);
provisioner.ApplySetupFile(auth);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory, // services start in System32 otherwise
});
builder.Host.UseWindowsService(o => o.ServiceName = "EnshroudedManager");
// Without the web interface the API only listens locally, for the desktop app.
builder.WebHost.UseUrls($"http://{(settings.Current.WebEnabled ? "0.0.0.0" : "127.0.0.1")}:{settings.Current.Port}");
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 2L * 1024 * 1024 * 1024); // world archives
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton(settings);
builder.Services.AddSingleton(log);
builder.Services.AddSingleton(auth);
builder.Services.AddSingleton(steam);
builder.Services.AddSingleton(provisioner);
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

var supervisor = app.Services.GetRequiredService<Supervisor>();
string Client(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
string MinLength() => L.T("8 caractères minimum.", "At least 8 characters.");

// Per-instance routes take ?instance=<id>; without it they act on the main server (as in version 1.0).
// (An explicit parameter, not HttpContext: a lambda taking only HttpContext and returning a Task is treated
// as a raw request delegate and its result is never written.)
InstanceRunner Runner(string? instance) =>
    supervisor.Get(instance) ?? throw new BadHttpRequestException(L.T("Instance introuvable.", "Instance not found."), 404);

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

// ---------- instances ----------

api.MapGet("/instances", () => supervisor.Runners.Select(r => new InstanceDto(
    r.I.Id, r.I.DisplayName, r.I.IsMain, r.I.Settings.Depot.Enabled, r.I.Server.State, Labels.State(r.I.Server.State),
    r.Players, r.I.Config.QueryPort(), r.I.InstallDir)));
api.MapGet("/instances/defaults", () =>
{
    var root = Path.GetPathRoot(settings.Current.Main.InstallDir) is { Length: > 0 } r ? r : @"C:\";
    var dir = Path.Combine(root, "EnshroudedServer-Depot");
    for (var n = 2; Directory.Exists(dir) || settings.Current.Instances.Any(i => string.Equals(i.InstallDir, dir, StringComparison.OrdinalIgnoreCase)); n++)
        dir = Path.Combine(root, $"EnshroudedServer-{n}");
    var port = supervisor.Runners.Select(x => x.I.Config.QueryPort()).DefaultIfEmpty(15637).Max() + 10;
    return new NewInstanceDefaultsDto(dir, port);
});
api.MapPost("/instances", (CreateInstanceDto dto) =>
{
    var error = supervisor.ValidateNewInstance(dto, out var dir);
    if (error != null) return Results.BadRequest(new MessageDto(error));
    var id = supervisor.CreateInstance(dto, dir);
    return Results.Ok(new MessageDto(id));
});
api.MapDelete("/instances/{id}", async (string id, bool? deleteFiles) =>
    new MessageDto(await supervisor.RemoveInstanceAsync(id, deleteFiles ?? false)));

// ---------- per instance ----------

api.MapGet("/status", (string? instance) =>
{
    var r = Runner(instance);
    var i = r.I;
    var local = r.LocalBuild;
    return new StatusDto(
        i.Server.State, Labels.State(i.Server.State), i.Server.Pid, i.Server.StartedAt, i.Server.MemoryMb, r.Players,
        i.Config.ServerName(), local, supervisor.RemoteBuild,
        supervisor.RemoteBuild != null && local != null && local != supervisor.RemoteBuild,
        supervisor.LastCheck, supervisor.NextCheck, settings.Current.LastUpdate, r.Busy, r.Progress, settings.Current.AutoUpdate,
        i.Config.Version, log.LastId, i.Id, i.DisplayName, i.Settings.Depot.Enabled, r.NextReset);
});
api.MapGet("/logs", (long? after) => log.Since(after ?? 0));

api.MapPost("/server/start", async (string? instance) => new MessageDto(await Runner(instance).StartAsync()));
api.MapPost("/server/stop", async (string? instance) => new MessageDto(await Runner(instance).StopServerAsync()));
api.MapPost("/server/restart", async (string? instance) => new MessageDto(await Runner(instance).RestartAsync()));
api.MapPost("/update/check", async () => new MessageDto(await supervisor.CheckAndUpdateAsync(auto: false)));
api.MapPost("/update/run", async (string? instance) => new MessageDto(await Runner(instance).UpdateAsync(supervisor.RemoteBuild, force: true)));

api.MapGet("/config/schema", (string? instance) => Runner(instance).I.Config.Schema());
api.MapGet("/config", (string? instance) =>
{
    try { return Results.Ok(Runner(instance).I.Config.Read()); }
    catch (BadHttpRequestException) { throw; }
    catch (Exception) { return Results.NotFound(new MessageDto(L.T("Le fichier de configuration du serveur n'existe pas encore.", "The server configuration file does not exist yet."))); }
});
api.MapPost("/config/validate", (JsonObject values, string? instance) => Runner(instance).I.Config.Validate(values, out _));
api.MapPut("/config", async (JsonObject values, string? instance) => await Runner(instance).ApplyConfigAsync(values));

api.MapGet("/backups", (string? instance) => Runner(instance).I.Backups.List());
api.MapPost("/backups", async (string? instance) => new MessageDto(await Runner(instance).BackupNowAsync()));
api.MapPost("/backups/{name}/restore", async (string name, bool? config, string? instance) =>
    new MessageDto(await Runner(instance).RestoreAsync(name, config ?? false)));
api.MapGet("/backups/{name}", (string name, string? instance) => Results.File(Runner(instance).I.Backups.ResolvePath(name), "application/zip", name));

// ---------- resource depot ----------

DepotDto Depot(InstanceRunner r)
{
    var d = r.I.Settings.Depot;
    return new DepotDto(d.Enabled, d.ResetEveryMinutes, d.OnlyWhenEmpty, d.LastReset, r.NextReset, r.HasMaster, r.MasterTime);
}
api.MapGet("/depot", (string? instance) => Depot(Runner(instance)));
api.MapPut("/depot", (DepotSettingsDto dto, string? instance) =>
{
    var r = Runner(instance);
    if (dto.ResetEveryMinutes is < 5 or > 1440) return Results.BadRequest(new MessageDto(L.T("Reset : entre 5 et 1440 minutes.", "Reset: between 5 and 1440 minutes.")));
    r.I.Update(s =>
    {
        s.Depot.Enabled = dto.Enabled;
        s.Depot.ResetEveryMinutes = dto.ResetEveryMinutes;
        s.Depot.OnlyWhenEmpty = dto.OnlyWhenEmpty;
    });
    r.I.Log.Info(L.T($"Réglages du dépôt : {(dto.Enabled ? $"reset toutes les {dto.ResetEveryMinutes} min" : "désactivé")}.",
                     $"Depot settings: {(dto.Enabled ? $"reset every {dto.ResetEveryMinutes} min" : "disabled")}."));
    return Results.Ok(Depot(r));
});
api.MapPost("/depot/reset", async (string? instance) => new MessageDto(await Runner(instance).ResetDepotAsync()));
api.MapPost("/depot/master/from-current", async (string? instance) => new MessageDto(await Runner(instance).SetMasterFromCurrentAsync()));
api.MapPost("/depot/master/import", async (string? instance, HttpContext ctx) =>
{
    var r = Runner(instance);
    var tmp = Path.Combine(Path.GetTempPath(), $"em-import-{Guid.NewGuid():N}.zip");
    try
    {
        // The archive is sent as the raw request body (application/zip or application/octet-stream).
        await using (var file = File.Create(tmp)) await ctx.Request.Body.CopyToAsync(file);
        return Results.Ok(new MessageDto(await r.ImportMasterAsync(tmp)));
    }
    finally { try { File.Delete(tmp); } catch { } }
}).DisableAntiforgery();

// ---------- tool settings ----------

api.MapGet("/settings", () =>
{
    var s = settings.Current;
    return new ManagerSettingsDto(s.CheckIntervalMinutes, s.AutoUpdate, s.Main.AutoRestart, s.BackupsToKeep, s.Language);
});
api.MapPut("/settings", (ManagerSettingsDto dto) =>
{
    if (dto.CheckIntervalMinutes is < 1 or > 1440) return Results.BadRequest(new MessageDto(L.T("Intervalle : entre 1 et 1440 minutes.", "Interval: between 1 and 1440 minutes.")));
    if (dto.BackupsToKeep is < 3 or > 500) return Results.BadRequest(new MessageDto(L.T("Sauvegardes à garder : entre 3 et 500.", "Backups to keep: between 3 and 500.")));
    settings.Update(s =>
    {
        s.CheckIntervalMinutes = dto.CheckIntervalMinutes;
        s.AutoUpdate = dto.AutoUpdate;
        foreach (var i in s.Instances) i.AutoRestart = dto.AutoRestart;
        s.BackupsToKeep = dto.BackupsToKeep;
        if (dto.Language is "fr" or "en") s.Language = dto.Language;
    });
    supervisor.Reschedule();
    log.Info(L.T($"Réglages de l'outil modifiés (vérification toutes les {dto.CheckIntervalMinutes} min, MAJ auto {(dto.AutoUpdate ? "activée" : "désactivée")}).",
                 $"Tool settings changed (check every {dto.CheckIntervalMinutes} min, auto update {(dto.AutoUpdate ? "on" : "off")})."));
    return Results.Ok(new MessageDto(L.T("Réglages enregistrés.", "Settings saved.")));
});

app.Run();
