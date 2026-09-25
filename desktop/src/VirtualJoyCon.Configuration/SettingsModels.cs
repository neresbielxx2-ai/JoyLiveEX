namespace VirtualJoyCon.Configuration;

/// <summary>Stick tuning shared by every input source on the PC side.</summary>
public sealed class StickTuning
{
    public float Deadzone { get; set; } = 0.10f;      // 0..0.9
    public float Sensitivity { get; set; } = 1.0f;    // 0.2..3
    public float MaxIntensity { get; set; } = 1.0f;   // 0.1..1
    public bool InvertY { get; set; }
    public int Curve { get; set; } // 0 linear, 1 gentle, 2 aggressive, 3 custom
    public float CurveExponent { get; set; } = 1.0f;

    public ControllerModel.StickCurve ToCurve() => new()
    {
        Deadzone = Math.Clamp(Deadzone, 0f, 0.9f),
        Sensitivity = Math.Clamp(Sensitivity, 0.2f, 3f),
        MaxIntensity = Math.Clamp(MaxIntensity, 0.1f, 1f),
        InvertY = InvertY,
        Curve = (ControllerModel.StickCurve.CurveKind)Math.Clamp(Curve, 0, 3),
        CurveExponent = CurveExponent,
    };
}

/// <summary>Connection + network settings (sections 8, 9, 10, 17 of the spec).</summary>
public sealed class ConnectionSettings
{
    public bool AutoReconnect { get; set; } = true;
    public int ReconnectTimeoutSec { get; set; } = 10;   // no traffic => considered lost
    public int HeartbeatMs { get; set; } = 1000;
    public int PingMs { get; set; } = 1000;
    public bool UseRelay { get; set; } = false;
    public string RelayHost { get; set; } = "";
    public int RelayPort { get; set; } = 8722;
    public bool PreferP2P { get; set; } = true;
    public bool AllowUnencryptedLan { get; set; } = true; // dev convenience on LAN; relay/internet always encrypted
    public int TcpUdpUdpPort { get; set; } = 8721;         // UDP input port (LAN)
    public int TcpPort { get; set; } = 8721;               // TCP port (adb reverse + LAN pairing fallback)
    public int AdbReverseDevicePort { get; set; } = 8721;  // device-local port the daemon/app dials
    public string AdbPath { get; set; } = "";              // empty = auto-detect
    public bool StartDaemonOnUsb { get; set; } = true;
}

/// <summary>Performance settings (section 16 PERFORMANCE).</summary>
public sealed class PerformanceSettings
{
    public int InputPollHz { get; set; } = 250;   // local input engine tick
    public int NetworkSendHz { get; set; } = 125; // max full-state packets/s (changes always sent immediately)
    public bool ShowFps { get; set; } = true;
    public bool VerboseDiagnostics { get; set; }
}

/// <summary>Interface settings (section 16 INTERFACE).</summary>
public sealed class InterfaceSettings
{
    public double UiScale { get; set; } = 1.0;        // 0.7..1.6 controller canvas scale
    public double Opacity { get; set; } = 1.0;        // controller opacity
    public double ControllerSize { get; set; } = 1.0;
    public double RailSpacing { get; set; } = 24;     // px between left/right halves
    public bool DarkMode { get; set; } = true;
    public bool ShowKeyLabels { get; set; } = true;
}

/// <summary>Options forwarded to the Android companion so its local sticks feel the same.</summary>
public sealed class AndroidOptions
{
    public bool VibrationEnabled { get; set; } = true;
    public int VibrationMs { get; set; } = 40;
}
