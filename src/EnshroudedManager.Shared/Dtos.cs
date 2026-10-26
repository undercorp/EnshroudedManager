using System.Text.Json.Nodes;

namespace EnshroudedManager.Shared;

public enum ServerState { Stopped, Starting, Running, Stopping, Updating, Crashed, Installing }

public static class Labels
{
    public static string State(ServerState s) => s switch
    {
        ServerState.Stopped => L.T("Arrêté", "Stopped"),
        ServerState.Starting => L.T("Démarrage…", "Starting…"),
        ServerState.Running => L.T("En ligne", "Online"),
        ServerState.Stopping => L.T("Arrêt…", "Stopping…"),
        ServerState.Updating => L.T("Mise à jour…", "Updating…"),
        ServerState.Crashed => L.T("Planté", "Crashed"),
        ServerState.Installing => L.T("Installation…", "Installing…"),
        _ => s.ToString()
    };
}

public record PlayersDto(int Current, int Max);

public record StatusDto(
    ServerState State,
    string StateLabel,
    int? Pid,
    DateTimeOffset? StartedAt,
    long? MemoryMb,
    PlayersDto? Players,
    string ServerName,
    string? LocalBuild,
    string? RemoteBuild,
    bool UpdateAvailable,
    DateTimeOffset? LastCheck,
    DateTimeOffset? NextCheck,
    DateTimeOffset? LastUpdate,
    string? Busy,
    double? Progress,
    bool AutoUpdate,
    long ConfigVersion,
    long LastLogId);

public record LogEntryDto(long Id, DateTimeOffset Time, string Level, string Message);

/// <summary>Description of one editable field of enshrouded_server.json.</summary>
/// <param name="Key">Dotted path in the JSON, e.g. "gameSettings.playerHealthFactor".</param>
/// <param name="Type">string | password | int | float | bool | enum | duration | tags</param>
public record FieldDto(
    string Key,
    string Label,
    string Section,
    string Type,
    double? Min = null,
    double? Max = null,
    double? Step = null,
    string[]? Options = null,
    string? Help = null,
    string? Unit = null);

public record SchemaDto(FieldDto[] Fields, FieldDto[] GroupFields);

/// <summary>Config values keyed by field Key; "userGroups" holds the raw groups array.</summary>
public record ConfigDto(JsonObject Values, long Version);

public record ApplyResultDto(bool Ok, string[] Errors, string[] Warnings);

public record BackupDto(string Name, DateTimeOffset Time, long SizeBytes, string Reason);

public record ManagerSettingsDto(int CheckIntervalMinutes, bool AutoUpdate, bool AutoRestart, int BackupsToKeep, string Language);

public record PasswordDto(string Password);
public record ChangePasswordDto(string Current, string New);
public record TokenDto(string Token);
public record AuthStateDto(bool Configured, string Language, bool Web);
public record MessageDto(string Message);
