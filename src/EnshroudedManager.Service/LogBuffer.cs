using EnshroudedManager.Shared;

namespace EnshroudedManager.Service;

/// <summary>
/// Ring buffer of entries, optionally mirrored to a daily file.
/// Levels: info, warn, error, event (events trigger desktop notifications).
/// </summary>
public class LogBuffer
{
    private readonly int _capacity;
    private readonly string? _fileDir;
    private readonly string? _filePrefix;
    private readonly LinkedList<LogEntryDto> _entries = new();
    private readonly object _lock = new();
    private long _nextId = 1;

    public LogBuffer(int capacity, string? fileDir = null, string? filePrefix = null)
    {
        _capacity = capacity;
        _fileDir = fileDir;
        _filePrefix = filePrefix;
        if (_fileDir != null) Directory.CreateDirectory(_fileDir);
    }

    public long LastId { get { lock (_lock) return _nextId - 1; } }

    public void Info(string m) => Add("info", m);
    public void Warn(string m) => Add("warn", m);
    public void Error(string m) => Add("error", m);
    public void Event(string m) => Add("event", m);

    public void Add(string level, string message)
    {
        var now = DateTimeOffset.Now;
        lock (_lock)
        {
            _entries.AddLast(new LogEntryDto(_nextId++, now, level, message));
            while (_entries.Count > _capacity) _entries.RemoveFirst();
            if (_fileDir != null)
            {
                try
                {
                    File.AppendAllText(Path.Combine(_fileDir, $"{_filePrefix}-{now:yyyy-MM-dd}.log"),
                        $"{now:yyyy-MM-dd HH:mm:ss} [{level.ToUpperInvariant()}] {message}{Environment.NewLine}");
                }
                catch { /* logging must never break the manager */ }
            }
        }
    }

    public LogEntryDto[] Since(long afterId, int max = 500)
    {
        lock (_lock)
            return _entries.Where(e => e.Id > afterId).TakeLast(max).ToArray();
    }
}
