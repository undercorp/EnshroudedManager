using System.Diagnostics;
using System.Runtime.InteropServices;
using EnshroudedManager.Shared;

namespace EnshroudedManager.Service;

/// <summary>Owns the enshrouded_server.exe process: start, graceful stop, crash detection.</summary>
public class GameServer
{
    private readonly Instance _instance;
    private readonly object _lock = new();
    private Process? _process;
    private bool _stopRequested;

    public ServerState State { get; private set; } = ServerState.Stopped;
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>Raised when the process exits without being asked to.</summary>
    public event Action<int?>? Crashed;

    public GameServer(Instance instance) => _instance = instance;

    private ILog _log => _instance.Log;

    public int? Pid { get { lock (_lock) return _process is { HasExited: false } p ? p.Id : null; } }

    public long? MemoryMb
    {
        get
        {
            lock (_lock)
            {
                if (_process is not { HasExited: false } p) return null;
                try { p.Refresh(); return p.WorkingSet64 / (1024 * 1024); } catch { return null; }
            }
        }
    }

    public bool IsRunning => Pid != null;

    /// <summary>Marks the state while the supervisor runs a long operation (update).</summary>
    public void SetState(ServerState state) => State = state;

    /// <summary>Takes control of a server that was already running (started by hand or by a previous manager run).</summary>
    public bool TryAdopt()
    {
        var exe = Path.GetFullPath(_instance.ExePath);
        foreach (var p in Process.GetProcessesByName("enshrouded_server"))
        {
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { }
            if (path == null || !string.Equals(Path.GetFullPath(path), exe, StringComparison.OrdinalIgnoreCase)) continue;
            Attach(p);
            try { StartedAt = p.StartTime; } catch { StartedAt = DateTimeOffset.Now; }
            State = ServerState.Running;
            _log.Info(L.T($"Serveur déjà en cours d'exécution repris en charge (PID {p.Id}).", $"Already running server taken over (PID {p.Id})."));
            return true;
        }
        return false;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_process is { HasExited: false }) return;
            if (!File.Exists(_instance.ExePath)) throw new FileNotFoundException(L.T("Exécutable du serveur introuvable", "Server executable not found"), _instance.ExePath);

            State = ServerState.Starting;
            var psi = new ProcessStartInfo(_instance.ExePath)
            {
                WorkingDirectory = _instance.InstallDir,
                UseShellExecute = false,
                CreateNoWindow = true, // hidden console: still lets us send Ctrl+C
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { };
            p.ErrorDataReceived += (_, e) => { };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _stopRequested = false;
            Attach(p);
            StartedAt = DateTimeOffset.Now;
            State = ServerState.Running;
            _log.Info(L.T($"Serveur démarré (PID {p.Id}).", $"Server started (PID {p.Id})."));
        }
    }

    private void Attach(Process p)
    {
        _process = p;
        p.EnableRaisingEvents = true;
        p.Exited += (_, _) => OnExited(p);
    }

    private void OnExited(Process p)
    {
        bool expected;
        int? code = null;
        lock (_lock)
        {
            if (!ReferenceEquals(_process, p)) return;
            expected = _stopRequested;
            try { code = p.ExitCode; } catch { }
            _process = null;
            StartedAt = null;
            if (State != ServerState.Updating) State = expected ? ServerState.Stopped : ServerState.Crashed;
        }
        if (!expected)
        {
            _log.Error(L.T($"Le serveur s'est arrêté de façon inattendue (code {code?.ToString() ?? "?"}).", $"The server stopped unexpectedly (code {code?.ToString() ?? "?"})."));
            Crashed?.Invoke(code);
        }
    }

    /// <summary>Asks the server to stop with Ctrl+C (it saves the world), then kills it after the timeout.</summary>
    public async Task StopAsync()
    {
        Process? p;
        lock (_lock)
        {
            p = _process;
            if (p is not { HasExited: false }) { State = ServerState.Stopped; return; }
            _stopRequested = true;
            if (State != ServerState.Updating) State = ServerState.Stopping;
        }
        var timeout = TimeSpan.FromSeconds(_instance.Global.StopTimeoutSeconds);
        _log.Info(L.T("Arrêt du serveur (sauvegarde du monde)…", "Stopping the server (saving the world)…"));

        if (SendCtrlC(p.Id))
        {
            using var cts = new CancellationTokenSource(timeout);
            try { await p.WaitForExitAsync(cts.Token); }
            catch (OperationCanceledException) { }
        }
        if (!p.HasExited)
        {
            _log.Warn(L.T("Le serveur n'a pas répondu à l'arrêt propre, arrêt forcé.", "The server did not respond to the clean shutdown, killing it."));
            try { p.Kill(entireProcessTree: true); } catch { }
            await p.WaitForExitAsync();
        }
        lock (_lock)
        {
            if (ReferenceEquals(_process, p)) { _process = null; StartedAt = null; }
            if (State != ServerState.Updating) State = ServerState.Stopped;
        }
        _log.Info(L.T("Serveur arrêté.", "Server stopped."));
    }

    private static readonly object ConsoleLock = new();

    private bool SendCtrlC(int pid)
    {
        lock (ConsoleLock)
        {
            FreeConsole();
            if (!AttachConsole((uint)pid))
            {
                _log.Warn(L.T($"Impossible de se rattacher à la console du serveur (erreur {Marshal.GetLastWin32Error()}).", $"Could not attach to the server console (error {Marshal.GetLastWin32Error()})."));
                return false;
            }
            try
            {
                SetConsoleCtrlHandler(IntPtr.Zero, true); // don't stop ourselves
                return GenerateConsoleCtrlEvent(CtrlCEvent, 0);
            }
            finally
            {
                FreeConsole();
                // Give the event time to be delivered before we accept Ctrl+C again.
                Thread.Sleep(500);
                SetConsoleCtrlHandler(IntPtr.Zero, false);
            }
        }
    }

    private const uint CtrlCEvent = 0;
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AttachConsole(uint dwProcessId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FreeConsole();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetConsoleCtrlHandler(IntPtr handler, bool add);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GenerateConsoleCtrlEvent(uint ctrlEvent, uint processGroupId);
}
