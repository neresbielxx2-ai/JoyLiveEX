using System.Diagnostics;

namespace VirtualJoyCon.Diagnostics;

/// <summary>EWMA latency stats (ms) for the status bar. Thread-safe.</summary>
public sealed class LatencyStats
{
    private readonly object _gate = new();
    private double _averageMs;
    private double _minMs = double.MaxValue;
    private double _maxMs;
    private int _samples;

    public double AverageMs { get { lock (_gate) return _samples == 0 ? 0 : _averageMs; } }
    public bool HasSamples { get { lock (_gate) return _samples > 0; } }

    public void AddSample(double ms)
    {
        lock (_gate)
        {
            if (_samples == 0) { _averageMs = ms; _minMs = ms; _maxMs = ms; }
            else
            {
                _averageMs += (ms - _averageMs) * 0.25; // EWMA alpha 0.25
                if (ms < _minMs) _minMs = ms;
                if (ms > _maxMs) _maxMs = ms;
            }
            _samples++;
        }
    }

    public void Reset()
    {
        lock (_gate) { _averageMs = 0; _minMs = double.MaxValue; _maxMs = 0; _samples = 0; }
    }

    public string Format()
    {
        lock (_gate)
        {
            if (_samples == 0) return "-- ms";
            return $"{_averageMs:F1} ms  (min {_minMs:F1} / max {_maxMs:F1})";
        }
    }
}

/// <summary>Packet-loss estimator from sequence gaps. Mirrored on Android.</summary>
public sealed class LossTracker
{
    private long _expectedNext = -1;
    private long _received, _lost;

    public double LossPercent
    {
        get
        {
            var total = Interlocked.Read(ref _received) + Interlocked.Read(ref _lost);
            return total == 0 ? 0 : (double)Interlocked.Read(ref _lost) / total * 100.0;
        }
    }
    public long Received => Interlocked.Read(ref _received);
    public long Lost => Interlocked.Read(ref _lost);

    public void Note(ulong seq)
    {
        var s = (long)seq;
        var next = Interlocked.Read(ref _expectedNext);
        if (next >= 0 && s > next)
            Interlocked.Add(ref _lost, Math.Min(s - next, 1024)); // ignore big jumps at reconnect
        Interlocked.Increment(ref _received);
        Interlocked.Exchange(ref _expectedNext, s + 1);
    }

    public void Resync(ulong seq) => Interlocked.Exchange(ref _expectedNext, (long)seq + 1);

    public void Reset()
    {
        Interlocked.Exchange(ref _expectedNext, -1);
        Interlocked.Exchange(ref _received, 0);
        Interlocked.Exchange(ref _lost, 0);
    }
}

/// <summary>Simple FPS counter for the PERFORMANCE diagnostics view.</summary>
public sealed class FpsCounter
{
    private long _frames;
    private long _lastUpdate = Stopwatch.GetTimestamp();
    public double Fps { get; private set; }

    public void Tick()
    {
        Interlocked.Increment(ref _frames);
        var now = Stopwatch.GetTimestamp();
        double elapsed = (now - Interlocked.Read(ref _lastUpdate)) / (double)Stopwatch.Frequency;
        if (elapsed >= 1.0)
        {
            Fps = Interlocked.Exchange(ref _frames, 0) / elapsed;
            Interlocked.Exchange(ref _lastUpdate, now);
        }
    }
}
