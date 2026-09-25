namespace VirtualJoyCon.Diagnostics;

public enum LogLevel { Trace = 0, Info = 1, Warn = 2, Error = 3 }

public readonly record struct LogEntry(DateTime TimeUtc, LogLevel Level, string Source, string Message)
{
    public override string ToString() =>
        $"[{TimeUtc.ToLocalTime():HH:mm:ss.fff}] {Level.ToString().ToUpperInvariant(),-5} {Source}: {Message}";
}
