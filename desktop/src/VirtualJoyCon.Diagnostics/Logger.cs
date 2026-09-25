using System.Collections.Concurrent;

namespace VirtualJoyCon.Diagnostics;

/// <summary>
/// Ring-buffer logger used by the Diagnostics panels (EXE + forwarded Android logs)
/// and written to %APPDATA%\JoyLiveEX\VirtualJoyCon\logs\session.log for support.
/// Thread-safe, allocation-light, never throws from hot paths.
/// </summary>
public sealed class Logger : IDisposable
{
    private const int Capacity = 4000;
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private readonly object _fileLock = new();
    private StreamWriter? _file;
    private int _count;

    public event Action<LogEntry>? EntryLogged;

    public bool Verbose { get; set; }
    public string LogDirectory { get; }

    public Logger(string logDirectory)
    {
        LogDirectory = logDirectory;
        try
        {
            Directory.CreateDirectory(logDirectory);
            _file = new StreamWriter(Path.Combine(logDirectory, "session.log"), append: true) { AutoFlush = false };
        }
        catch
        {
            _file = null; // logging must never break the app
        }
    }

    public void Trace(string src, string msg) => Log(LogLevel.Trace, src, msg);
    public void Info(string src, string msg) => Log(LogLevel.Info, src, msg);
    public void Warn(string src, string msg) => Log(LogLevel.Warn, src, msg);
    public void Error(string src, string msg) => Log(LogLevel.Error, src, msg);

    public void Log(LogLevel level, string source, string message)
    {
        if (level == LogLevel.Trace && !Verbose) return;
        var e = new LogEntry(DateTime.UtcNow, level, source, message);
        _entries.Enqueue(e);
        if (Interlocked.Increment(ref _count) > Capacity)
        {
            _entries.TryDequeue(out _);
            Interlocked.Decrement(ref _count);
        }
        var f = _file;
        if (f != null)
        {
            lock (_fileLock)
            {
                try { f.WriteLine(e.ToString()); } catch { }
            }
        }
        try { EntryLogged?.Invoke(e); } catch { }
    }

    public LogEntry[] Snapshot() => _entries.ToArray();

    public void Flush()
    {
        var f = _file;
        if (f == null) return;
        lock (_fileLock) { try { f.Flush(); } catch { } }
    }

    public void Dispose()
    {
        Flush();
        var f = _file;
        _file = null;
        try { f?.Dispose(); } catch { }
    }
}
