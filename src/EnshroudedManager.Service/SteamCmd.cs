using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using EnshroudedManager.Shared;

namespace EnshroudedManager.Service;

/// <summary>Wraps steamcmd.exe: reads the published build and installs updates.</summary>
public class SteamCmd
{
    private readonly SettingsStore _settings;
    private readonly LogBuffer _log;

    public SteamCmd(SettingsStore settings, LogBuffer log)
    {
        _settings = settings;
        _log = log;
    }

    private ManagerSettings S => _settings.Current;

    /// <summary>Build id currently installed, read from the Steam app manifest.</summary>
    public string? GetLocalBuild()
    {
        if (string.IsNullOrWhiteSpace(S.InstallDir)) return null;
        var manifest = Path.Combine(S.InstallDir, "steamapps", $"appmanifest_{S.AppId}.acf");
        if (!File.Exists(manifest)) return null;
        var m = Regex.Match(File.ReadAllText(manifest), "\"buildid\"\\s+\"(\\d+)\"");
        return m.Success && m.Groups[1].Value != "0" ? m.Groups[1].Value : null; // 0 while the first download runs
    }

    /// <summary>Build id published on Steam for the configured branch.</summary>
    public async Task<string> GetRemoteBuildAsync(CancellationToken ct) => (await GetRemoteInfoAsync(ct)).Build;

    /// <summary>Published build id and total download size of the configured branch.</summary>
    public async Task<(string Build, long Size)> GetRemoteInfoAsync(CancellationToken ct)
    {
        // steamcmd happily answers from a stale local cache; drop it so the info is fresh.
        var cache = Path.Combine(Path.GetDirectoryName(S.SteamCmdPath)!, "appcache", "appinfo.vdf");
        try { if (File.Exists(cache)) File.Delete(cache); } catch { }

        for (var attempt = 1; ; attempt++)
        {
            var (_, output) = await RunAsync(
                $"+login anonymous +app_info_update 1 +app_info_print {S.AppId} +quit",
                TimeSpan.FromMinutes(5), echo: false, ct);
            var build = ParseBranchBuild(output, S.Branch);
            if (build != null) return (build, ParseDepotSize(output, S.Branch));
            // First run after a steamcmd self-update often prints nothing useful.
            if (attempt >= 3) throw new InvalidOperationException(L.T("Impossible de lire la version publiée sur Steam (réponse de steamcmd illisible).", "Could not read the published version from Steam (unreadable steamcmd output)."));
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    public static string? ParseBranchBuild(string output, string branch)
    {
        var branches = output.IndexOf("\"branches\"", StringComparison.Ordinal);
        if (branches < 0) return null;
        var m = Regex.Match(output[branches..],
            "\"" + Regex.Escape(branch) + "\"\\s*\\{[^}]*?\"buildid\"\\s*\"(\\d+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Sum of the depot sizes of a branch (Windows server depots only appear for this app).</summary>
    public static long ParseDepotSize(string output, string branch) =>
        Regex.Matches(output, "\"" + Regex.Escape(branch) + "\"\\s*\\{\\s*\"gid\"\\s*\"\\d+\"\\s*\"size\"\\s*\"(\\d+)\"")
            .Sum(m => long.Parse(m.Groups[1].Value));

    /// <summary>Installs/validates the server. Returns true when the manifest reaches <paramref name="targetBuild"/>.</summary>
    /// <param name="progress">Called with the steamcmd phase ("downloading", "verifying"…), percent, done and total bytes.</param>
    public async Task<bool> UpdateAsync(string? targetBuild, CancellationToken ct, Action<string, double, long, long>? progress = null)
    {
        var beta = S.Branch == "public" ? "" : $" -beta {S.Branch}";
        // 3 attempts: on a fresh machine the first steamcmd run is often spent updating steamcmd itself.
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var (code, output) = await RunAsync(
                $"+force_install_dir \"{S.InstallDir}\" +login anonymous +app_update {S.AppId}{beta} validate +quit",
                TimeSpan.FromHours(3), echo: true, ct, progress);
            var local = GetLocalBuild();
            var success = output.Contains($"Success! App '{S.AppId}'", StringComparison.Ordinal)
                          || output.Contains("fully installed", StringComparison.Ordinal);
            if (success && (targetBuild == null || local == targetBuild)) return true;
            _log.Warn(L.T($"steamcmd a terminé sans confirmer la mise à jour (code {code}, build local {local ?? "?"}), tentative {attempt}/3.",
                          $"steamcmd finished without confirming the update (code {code}, local build {local ?? "?"}), attempt {attempt}/3."));
        }
        return false;
    }

    private static readonly Regex ProgressLine = new(@"Update state \(0x[0-9a-f]+\) (?<phase>[\w ]+?), progress: (?<pct>[\d.]+) \((?<done>\d+) / (?<total>\d+)\)");

    private async Task<(int ExitCode, string Output)> RunAsync(string args, TimeSpan timeout, bool echo, CancellationToken ct,
        Action<string, double, long, long>? progress = null)
    {
        var psi = new ProcessStartInfo(S.SteamCmdPath, args)
        {
            WorkingDirectory = Path.GetDirectoryName(S.SteamCmdPath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var p = new Process { StartInfo = psi };
        var sb = new StringBuilder();
        string? lastBucket = null;
        void OnLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (sb) sb.AppendLine(line);
            var m = ProgressLine.Match(line);
            if (m.Success)
            {
                var phase = m.Groups["phase"].Value;
                var pct = double.Parse(m.Groups["pct"].Value, System.Globalization.CultureInfo.InvariantCulture);
                progress?.Invoke(phase, pct, long.Parse(m.Groups["done"].Value), long.Parse(m.Groups["total"].Value));
                // One log line per phase and per 10 %, not one every second.
                var bucket = $"{phase}:{(int)(pct / 10)}";
                if (echo && bucket != lastBucket) _log.Info("steamcmd : " + line.Trim());
                lastBucket = bucket;
            }
            else if (echo && Regex.IsMatch(line, "Success|Error|ERROR|fully installed", RegexOptions.IgnoreCase))
                _log.Info("steamcmd : " + line.Trim());
        }
        p.OutputDataReceived += (_, e) => OnLine(e.Data);
        p.ErrorDataReceived += (_, e) => OnLine(e.Data);
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException(L.T($"steamcmd n'a pas répondu en {timeout.TotalMinutes} min.", $"steamcmd did not answer within {timeout.TotalMinutes} min."));
        }
        p.WaitForExit(); // flush async output
        lock (sb) return (p.ExitCode, sb.ToString());
    }
}
