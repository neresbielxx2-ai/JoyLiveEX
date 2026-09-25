namespace VirtualJoyCon.UsbAdb;

/// <summary>
/// Periodically polls `adb devices` and raises change events so the UI can show:
/// disconnected / unauthorized / offline / connected (spec §8 flow steps 1-7,
/// including "waiting for the user to tap Allow USB debugging").
/// </summary>
public sealed class AdbWatcher : IDisposable
{
    private readonly AdbClient _adb;
    private readonly Timer _timer;
    private readonly object _gate = new();
    private List<AdbDevice> _last = new();

    public event Action<AdbState, AdbDevice?>? StateChanged;
    public AdbState LastState { get; private set; } = AdbState.NoAdb;
    public AdbDevice? LastDevice { get; private set; }
    public bool AdbAvailable => _adb.AdbAvailable;

    public AdbWatcher(AdbClient adb, int pollMs = 1500)
    {
        _adb = adb;
        _timer = new Timer(_ => SafePoll(), null, Timeout.Infinite, Timeout.Infinite);
        _ = pollMs;
    }

    public void Start() => _timer.Change(0, Timeout.Infinite); // self-rescheduling loop
    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    private async Task SafePoll()
    {
        try
        {
            if (!_adb.AdbAvailable)
            {
                Publish(AdbState.NoAdb, null);
                return;
            }
            var devices = await _adb.ListDevicesAsync().ConfigureAwait(false);
            AdbState state;
            AdbDevice? primary = null;

            var device = devices.FirstOrDefault(d => d.State == "device");
            var unauth = devices.FirstOrDefault(d => d.State == "unauthorized");
            var offline = devices.FirstOrDefault(d => d.State == "offline");

            if (device != null) { state = AdbState.Connected; primary = device; }
            else if (unauth != null) { state = AdbState.Unauthorized; primary = unauth; }
            else if (offline != null) { state = AdbState.Offline; primary = offline; }
            else if (devices.Count > 0) { state = AdbState.Offline; primary = devices[0]; }
            else state = AdbState.WaitingDevice;

            Publish(state, primary);
        }
        catch (Exception)
        {
            Publish(AdbState.ServerError, null);
        }
        finally
        {
            lock (_gate) _timer.Change(1500, Timeout.Infinite);
        }
    }

    private void Publish(AdbState state, AdbDevice? device)
    {
        bool changed;
        lock (_gate)
        {
            changed = state != LastState || device?.Serial != LastDevice?.Serial;
            LastState = state;
            LastDevice = device;
        }
        if (changed) StateChanged?.Invoke(state, device);
    }

    public void Dispose() => _timer.Dispose();
}
