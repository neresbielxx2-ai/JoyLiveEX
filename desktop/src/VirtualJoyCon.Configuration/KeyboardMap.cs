namespace VirtualJoyCon.Configuration;

/// <summary>
/// Default keyboard map based on the spec (section 6).
/// Note: the spec sample assigned I/J/K/L both to the d-pad and to the face buttons;
/// that is impossible simultaneously, so face buttons keep I/J/K/L (J=A, K=B, U=X, I=Y)
/// and the d-pad defaults to the arrow keys. Every action is remappable in the UI.
/// Key literals are WPF System.Windows.Input.Key enum names.
/// </summary>
public static class KeyboardMap
{
    public static readonly (string Action, string Key)[] Defaults =
    {
        ("StickLeft",   "A"),
        ("StickRight",  "D"),
        ("StickUp",     "W"),
        ("StickDown",   "S"),

        ("Up",          "Up"),
        ("Down",        "Down"),
        ("Left",        "Left"),
        ("Right",       "Right"),

        ("A",           "J"),
        ("B",           "K"),
        ("X",           "U"),
        ("Y",           "I"),

        ("ZL",          "Q"),
        ("ZR",          "E"),
        ("L",           "D1"),
        ("R",           "D2"),

        ("Plus",        "Return"),
        ("Minus",       "Back"),
        ("Home",        "H"),
        ("Capture",     "G"),

        ("RightStickLeft",  "NumPad4"),
        ("RightStickRight", "NumPad6"),
        ("RightStickUp",    "NumPad8"),
        ("RightStickDown",  "NumPad5"),

        ("ToggleControl",   "F9"),
    };

    public static void ApplyDefaults(Dictionary<string, string> map)
    {
        map.Clear();
        foreach (var (action, key) in Defaults)
            map[action] = key;
    }

    public static readonly string[] RemappableActions =
    {
        "Up","Down","Left","Right",
        "StickUp","StickDown","StickLeft","StickRight",
        "RightStickUp","RightStickDown","RightStickLeft","RightStickRight",
        "A","B","X","Y","L","R","ZL","ZR",
        "Plus","Minus","Home","Capture",
        "LeftStickClick","RightStickClick",
        "SL_L","SR_L","SL_R","SR_R",
        "ToggleControl","Screenshot",
    };
}

/// <summary>Canonical XInput button bit constants (shared by InputEngine + default map).</summary>
public static class XInputBits
{
    public const int DPAD_UP = 0x0001;
    public const int DPAD_DOWN = 0x0002;
    public const int DPAD_LEFT = 0x0004;
    public const int DPAD_RIGHT = 0x0008;
    public const int START = 0x0010;
    public const int BACK = 0x0020;
    public const int LEFT_THUMB = 0x0040;
    public const int RIGHT_THUMB = 0x0080;
    public const int LEFT_SHOULDER = 0x0100;
    public const int RIGHT_SHOULDER = 0x0200;
    public const int GUIDE = 0x0400;
    public const int A = 0x1000;
    public const int B = 0x2000;
    public const int X = 0x4000;
    public const int Y = 0x8000;
}

public static class GamepadMapDefaults
{
    /// <summary>action -> XInput bit mask, standard layout.</summary>
    public static void Apply(Dictionary<string, int> map)
    {
        map.Clear();
        map["A"] = XInputBits.A;
        map["B"] = XInputBits.B;
        map["X"] = XInputBits.X;
        map["Y"] = XInputBits.Y;
        map["L"] = XInputBits.LEFT_SHOULDER;
        map["R"] = XInputBits.RIGHT_SHOULDER;
        map["ZL"] = -1;   // -1 => analog trigger threshold source (left trigger > 0.5)
        map["ZR"] = -2;   // -2 => right trigger threshold
        map["Plus"] = XInputBits.START;
        map["Minus"] = XInputBits.BACK;
        map["Home"] = XInputBits.GUIDE;
        map["Up"] = XInputBits.DPAD_UP;
        map["Down"] = XInputBits.DPAD_DOWN;
        map["Left"] = XInputBits.DPAD_LEFT;
        map["Right"] = XInputBits.DPAD_RIGHT;
        map["LeftStickClick"] = XInputBits.LEFT_THUMB;
        map["RightStickClick"] = XInputBits.RIGHT_THUMB;
    }
}
