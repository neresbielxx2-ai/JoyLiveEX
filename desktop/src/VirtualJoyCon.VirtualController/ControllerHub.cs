using System.Diagnostics;
using VirtualJoyCon.Configuration;
using VirtualJoyCon.ControllerModel;
using VirtualJoyCon.Diagnostics;
using VirtualJoyCon.InputEngine;
using VirtualJoyCon.Network;
using VirtualJoyCon.Network.Link;
using VirtualJoyCon.Network.Protocol;

namespace VirtualJoyCon.VirtualController;

public enum ControlMode { Stopped, Running }

/// <summary>
/// The heart of the desktop app. Composes three input sources (interactive UI
/// widgets, keyboard, physical XInput gamepad) plus the phone's own touch
/// controls into one canonical controller state and streams it to the Android
/// peers through the LinkManager.
///
/// Send policy (low latency + no stuck state):
///  - any button change            -> packet IMMEDIATELY
///  - analog axis motion           -> up to NetworkSendHz packets/s
///  - while active, full state is re-sent at 2 Hz so a dropped "release" can
///    never leave a virtual button held on the phone.
/// </summary>
public sealed class ControllerHub : IDisposable
{
    private readonly AppSettings _cfg;
    private readonly Logger _log;
    private readonly LinkManager _links;
    private readonly GamepadSource _gamepad;
    private readonly StickCurve _leftCurve, _rightCurve;

    private readonly object _gate = new();
    private readonly ControllerState _ui = new();     // UI widgets + keyboard (axes already curved)
    private readonly ControllerState _pad = new();    // physical gamepad
    private readonly ControllerState _dev = new();    // phone touch UI relayed back
    private readonly ControllerState _merged = new();
    private readonly ControllerState _lastSent = new();
    private readonly HashSet<string> _stickKeys = new();
    private long _devFreshMs;

    private readonly Thread _loop;
    private readonly CancellationTokenSource _cts = new();
    private long _lastSentPeriodicMs;
    private long _lastSendTs;

    public ControlMode Mode { get; private set; } = ControlMode.Stopped;
    public FpsCounter Fps { get; } = new();
    public LinkTransport ActiveTransport { get; private set; } = LinkTransport.Lan;
    public event Action? StatusChanged;

    public ControllerHub(AppSettings cfg, Logger log)
    {
        _cfg = cfg;
        _log = log;
        _links = new LinkManager(log, () => PairingCode);
        _links.StateReceived += OnDeviceState;
        _links.StatusReceived += OnDeviceStatus;
        _links.PeerLog += (l, lvl, msg) => log.Log((LogLevel)Math.Clamp(lvl, 0, 3), "ANDROID", msg);
        _links.LinkOpened += _ => RaiseStatus();
        _links.LinkClosed += OnLinkClosed;
        _leftCurve = cfg.LeftStick.ToCurve();
        _rightCurve = cfg.RightStick.ToCurve();
        _gamepad = new GamepadSource(cfg.GamepadMap, cfg.LeftStick, cfg.RightStick);
        _loop = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "vjc-hub" };
        _loop.Start();
    }

    public LinkManager Links => _links;
    public GamepadSource Gamepad => _gamepad;
    public string PairingCode { get; private set; } = Crypto.PairingCode.Generate();
    public bool Connected => _links.ReadyLinkCount > 0;
    public bool DaemonReady { get; private set; }

    public double LatencyMs { get; private set; }
    public double LossPercent { get; private set; }
    public bool HasLatency { get; private set; }
    public string DeviceLabel { get; private set; } = "";
    public byte AndroidServiceState { get; private set; }     // 0 stopped 1 starting 2 running 3 error
    public byte AndroidInputMode { get; private set; }        // 0 none 1 in-app 2 adb-daemon 3 a11y-touch
    public int AndroidSdk { get; private set; }
    public string AndroidAppVersion { get; private set; } = "";
    public bool DirectP2P { get; private set; }
    public bool RelayRouted => _links.RelayActive && Connected && !DirectP2P;
    public string AppVersionText { get; } = typeof(ControllerHub).Assembly.GetName().Version?.ToString(3) ?? "1.0";

    public void RegeneratePairingCode()
    {
        PairingCode = Crypto.PairingCode.Generate();
        _log.Info("HUB", "nova sessão de pareamento gerada");
        RaiseStatus();
    }

    public void SetMode(ControlMode m)
    {
        if (Mode == m) return;
        Mode = m;
        _log.Info("HUB", m == ControlMode.Running
            ? "controle INICIADO — streaming de eventos ativo"
            : "controle PARADO (estado zerado enviado ao dispositivo)");
        if (m == ControlMode.Stopped)
        {
            ReleaseAll();
            lock (_gate) { _links.SendState(_ui); _lastSent.CopyFrom(_ui); }
        }
        RaiseStatus();
    }

    // ==================== input sources ====================

    /// <summary>WPF widgets call this with a raw pointer-normalized stick vector (-1..1).</summary>
    public void UiSetStick(bool left, float rawX, float rawY)
    {
        lock (_gate)
        {
            if (left) _leftCurve.Process(rawX, rawY, out _ui.LeftX, out _ui.LeftY);
            else _rightCurve.Process(rawX, rawY, out _ui.RightX, out _ui.RightY);
        }
    }

    public void UiSetButton(ButtonFlags b, bool down)
    {
        lock (_gate)
        {
            if (down) _ui.Buttons |= (uint)b; else _ui.Buttons &= ~(uint)b;
            if (b == ButtonFlags.ZL) _ui.LeftTrigger = down ? 1f : 0f;
            if (b == ButtonFlags.ZR) _ui.RightTrigger = down ? 1f : 0f;
        }
    }

    /// <summary>Keyboard remap entry point (action names from KeyboardMap).</summary>
    public void SetAction(string action, bool down)
    {
        lock (_gate)
        {
            var bit = GamepadSource.MapActionToButton(action);
            if (bit != 0)
            {
                if (down) _ui.Buttons |= bit; else _ui.Buttons &= ~bit;
                if (action == "ZL") _ui.LeftTrigger = down ? 1f : 0f;
                if (action == "ZR") _ui.RightTrigger = down ? 1f : 0f;
                return;
            }
            switch (action)
            {
                case "StickLeft": SetStickKey("L", 'x', -1, down); break;
                case "StickRight": SetStickKey("L", 'x', +1, down); break;
                case "StickUp": SetStickKey("L", 'y', -1, down); break;
                case "StickDown": SetStickKey("L", 'y', +1, down); break;
                case "RightStickLeft": SetStickKey("R", 'x', -1, down); break;
                case "RightStickRight": SetStickKey("R", 'x', +1, down); break;
                case "RightStickUp": SetStickKey("R", 'y', -1, down); break;
                case "RightStickDown": SetStickKey("R", 'y', +1, down); break;
            }
        }
    }

    private void SetStickKey(string stick, char axis, int dir, bool down)
    {
        var id = $"{stick}{axis}{dir:+;-;0}";
        if (dir != 0) id = $"{stick}{axis}{dir}";
        if (down) _stickKeys.Add(id); else _stickKeys.Remove(id);
        if (stick == "L")
        {
            float x = (_stickKeys.Contains("Lx-1") ? -1 : 0) + (_stickKeys.Contains("Lx1") ? 1 : 0);
            float y = (_stickKeys.Contains("Ly-1") ? -1 : 0) + (_stickKeys.Contains("Ly1") ? 1 : 0);
            _leftCurve.Process(x, y, out _ui.LeftX, out _ui.LeftY);
        }
        else
        {
            float x = (_stickKeys.Contains("Rx-1") ? -1 : 0) + (_stickKeys.Contains("Rx1") ? 1 : 0);
            float y = (_stickKeys.Contains("Ry-1") ? -1 : 0) + (_stickKeys.Contains("Ry1") ? 1 : 0);
            _rightCurve.Process(x, y, out _ui.RightX, out _ui.RightY);
        }
    }

    /// <summary>Anti-stuck release: called on window focus loss, link drop, stop.</summary>
    public void ReleaseAll()
    {
        lock (_gate)
        {
            _ui.Buttons = 0;
            _ui.LeftX = _ui.LeftY = _ui.RightX = _ui.RightY = 0;
            _ui.LeftTrigger = _ui.RightTrigger = 0;
            _stickKeys.Clear();
            _pad.Buttons = 0;
            _dev.Buttons = 0;
        }
    }

    // ==================== device-side inbound ====================

    private void OnDeviceState(VjcLink l, ControllerState s)
    {
        lock (_gate)
        {
            _dev.CopyFrom(s);
            _devFreshMs = Environment.TickCount64;
        }
    }

    private void OnDeviceStatus(VjcLink l, StatusPayload st)
    {
        AndroidServiceState = st.ServiceState;
        AndroidInputMode = st.InputMode;
        DeviceLabel = st.Model.Length > 0 ? st.Model : st.DeviceName;
        AndroidSdk = st.SdkInt;
        AndroidAppVersion = $"{st.AppVersion >> 8}.{st.AppVersion & 0xFF}";
        DaemonReady = st.InputMode >= 2;
        RaiseStatus();
    }

    private void OnLinkClosed(VjcLink l, string reason)
    {
        _ = reason;
        ReleaseAll(); // never leave buttons stuck when a link drops
        RaiseStatus();
    }

    // ==================== main loop ====================

    private void Loop()
    {
        var targetHz = Math.Clamp(_cfg.Performance.InputPollHz, 30, 1000);
        long interval = Math.Max(1, Stopwatch.Frequency / targetHz);
        var sendInterval = Math.Max(1, Stopwatch.Frequency / Math.Clamp(_cfg.Performance.NetworkSendHz, 10, 500));
        var sw = Stopwatch.StartNew();
        long next = 0, nextStatus = 0;

        while (!_cts.IsCancellationRequested)
        {
            var now = Stopwatch.GetTimestamp();
            if (now < next)
            {
                long remainUs = (next - now) * 1_000_000 / Stopwatch.Frequency;
                if (remainUs > 1500) Thread.Sleep((int)(remainUs / 1000) - 1);
                else Thread.SpinWait((int)(remainUs * 30) + 50);
                continue;
            }
            next = Math.Max(next + interval, now); // skip catch-up bursts
            Fps.Tick();

            try
            {
                _links.Tick();

                lock (_gate)
                {
                    _pad.Buttons = 0;
                    _pad.LeftX = _pad.LeftY = _pad.RightX = _pad.RightY = 0;
                    _pad.LeftTrigger = _pad.RightTrigger = 0;
                    _gamepad.Poll(_pad);

                    // stale device UI state decays after 300 ms (anti-stuck on phone side too)
                    if (Environment.TickCount64 - _devFreshMs > 300)
                    {
                        _dev.Buttons = 0;
                        _dev.LeftX = _dev.LeftY = _dev.RightX = _dev.RightY = 0;
                        _dev.LeftTrigger = _dev.RightTrigger = 0;
                    }

                    Compose();
                    var nowMs = Environment.TickCount64;
                    var changed = !_merged.ContentEquals(_lastSent);
                    var periodic = nowMs - _lastSentPeriodicMs >= 500;
                    if (Mode == ControlMode.Running && _links.ReadyLinkCount > 0 &&
                        (changed || (periodic && !_merged.IsIdle)))
                    {
                        if (changed || now - _lastSendTs >= sendInterval)
                        {
                            _links.SendState(_merged);
                            _lastSent.CopyFrom(_merged);
                            _lastSentPeriodicMs = nowMs;
                            _lastSendTs = now;
                        }
                    }
                }

                if (now >= nextStatus)
                {
                    nextStatus = now + Stopwatch.Frequency / 5;
                    UpdateStatus();
                }
                _ = sw;
            }
            catch (Exception ex)
            {
                _log.Error("HUB", $"loop: {ex.Message}");
            }
        }
    }

    private void Compose()
    {
        _merged.Buttons = _ui.Buttons | _pad.Buttons | _dev.Buttons;
        _merged.LeftX = AbsMax(_ui.LeftX, _pad.LeftX, _dev.LeftX);
        _merged.LeftY = AbsMax(_ui.LeftY, _pad.LeftY, _dev.LeftY);
        _merged.RightX = AbsMax(_ui.RightX, _pad.RightX, _dev.RightX);
        _merged.RightY = AbsMax(_ui.RightY, _pad.RightY, _dev.RightY);
        _merged.LeftTrigger = Math.Max(Math.Max(_ui.LeftTrigger, _pad.LeftTrigger), _dev.LeftTrigger);
        _merged.RightTrigger = Math.Max(Math.Max(_ui.RightTrigger, _pad.RightTrigger), _dev.RightTrigger);
        _merged.TimestampMs = Environment.TickCount64;
    }

    private static float AbsMax(params float[] v)
    {
        float best = 0;
        foreach (var x in v) if (Math.Abs(x) > Math.Abs(best)) best = x;
        return best;
    }

    private void UpdateStatus()
    {
        double lat = 0, loss = 0;
        bool any = false, direct = false;
        var transport = LinkTransport.Lan;
        HasLatency = false;
        foreach (var l in _links.SnapshotLinks())
        {
            if (l.Phase != VjcLink.PhaseReady) continue;
            any = true;
            if (l.HasLatency) { lat = Math.Max(lat, l.LatencyMs); HasLatency = true; }
            loss = Math.Max(loss, l.LossPercent);
            direct |= l.IsDirect;
            transport = l.Transport;
            DaemonReady |= l.PeerRole == Wire.RoleDaemon;
        }
        ActiveTransport = any ? transport : ActiveTransport;
        LatencyMs = lat;
        LossPercent = loss;
        DirectP2P = _links.RelayActive && any && direct;
        DaemonReady = _links.SnapshotLinks().Any(x => x.Phase == VjcLink.PhaseReady && x.PeerRole == Wire.RoleDaemon);
        StatusChanged?.Invoke();
    }

    /// <summary>UI echo: current composed state (for button/stick visual feedback).</summary>
    public void SnapshotTo(ControllerState s)
    {
        lock (_gate) s.CopyFrom(_merged);
    }

    private void RaiseStatus() => StatusChanged?.Invoke();

    /// <summary>Re-reads tuning from settings after the user edits them.</summary>
    public void ApplySettings()
    {
        _leftCurve.Deadzone = _cfg.LeftStick.Deadzone;
        _leftCurve.Sensitivity = _cfg.LeftStick.Sensitivity;
        _leftCurve.MaxIntensity = _cfg.LeftStick.MaxIntensity;
        _leftCurve.InvertY = _cfg.LeftStick.InvertY;
        _leftCurve.Curve = (StickCurve.CurveKind)Math.Clamp(_cfg.LeftStick.Curve, 0, 3);
        _leftCurve.CurveExponent = _cfg.LeftStick.CurveExponent;
        _rightCurve.Deadzone = _cfg.RightStick.Deadzone;
        _rightCurve.Sensitivity = _cfg.RightStick.Sensitivity;
        _rightCurve.MaxIntensity = _cfg.RightStick.MaxIntensity;
        _rightCurve.InvertY = _cfg.RightStick.InvertY;
        _rightCurve.Curve = (StickCurve.CurveKind)Math.Clamp(_cfg.RightStick.Curve, 0, 3);
        _rightCurve.CurveExponent = _cfg.RightStick.CurveExponent;
        // forward to Android so its on-screen sticks use the same feel
        var json = $"{{\"dz\":{_cfg.LeftStick.Deadzone.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}," +
                   $"\"sens\":{_cfg.LeftStick.Sensitivity.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}," +
                   $"\"invertY\":{(_cfg.LeftStick.InvertY ? 1 : 0)},\"vib\":{(_cfg.Android.VibrationEnabled ? 1 : 0)}}}";
        _links.SendCmdToAll(Wire.CmdConfig, System.Text.Encoding.UTF8.GetBytes(json));
    }

    public void VibrateDevice(int ms)
    {
        var v = (ushort)Math.Clamp(ms, 10, 2000);
        _links.SendCmdToAll(Wire.CmdVibrate, BitConverter.GetBytes(v));
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop.Join(500); } catch { }
        _links.Dispose();
    }
}
