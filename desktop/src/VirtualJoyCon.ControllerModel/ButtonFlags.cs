namespace VirtualJoyCon.ControllerModel;

/// <summary>
/// Canonical button bit layout for the Virtual Joy-Con set.
/// IMPORTANT: this layout is mirrored on Android (WireConsts.kt / Constants.java).
/// Both sides must stay byte-compatible or input will be garbage.
/// </summary>
[Flags]
public enum ButtonFlags : uint
{
    None         = 0u,
    A            = 1u << 0,
    B            = 1u << 1,
    X            = 1u << 2,
    Y            = 1u << 3,
    L            = 1u << 4,
    R            = 1u << 5,
    ZL           = 1u << 6,
    ZR           = 1u << 7,
    Minus        = 1u << 8,
    Plus         = 1u << 9,
    Home         = 1u << 10,
    Capture      = 1u << 11,
    LeftStick    = 1u << 12,   // stick click (L3)
    RightStick   = 1u << 13,   // stick click (R3)
    DPadUp       = 1u << 14,
    DPadDown     = 1u << 15,
    DPadLeft     = 1u << 16,
    DPadRight    = 1u << 17,
    SL_Left      = 1u << 18,   // left rail side button
    SR_Left      = 1u << 19,
    SL_Right     = 1u << 20,   // right rail side button
    SR_Right     = 1u << 21,
    Guide        = 1u << 22,   // reserved (system guide passthrough)
}

/// <summary>Logical actions a key / pad button / UI element can be mapped to.</summary>
public enum PadAction
{
    None = 0,
    Up, Down, Left, Right,               // d-pad
    StickUp, StickDown, StickLeft, StickRight, // left stick directions (digital keys)
    StickRightUp, StickRightDown, StickRightLeft, StickRightRight, // right stick directions
    A, B, X, Y,
    L, R, ZL, ZR,
    Minus, Plus, Home, Capture,
    LeftStickClick, RightStickClick,
    SL_L, SR_L, SL_R, SR_R,
    ToggleControl,   // start/stop control stream
    Screenshot,      // request capture gesture on device
}

/// <summary>D-pad hat encoding used on the wire (mirrored on Android).</summary>
public static class Hat
{
    public const byte None = 0;
    public const byte N = 1;
    public const byte NE = 2;
    public const byte E = 3;
    public const byte SE = 4;
    public const byte S = 5;
    public const byte SW = 6;
    public const byte W = 7;
    public const byte NW = 8;

    public static byte FromState(uint buttons)
    {
        bool up = (buttons & (uint)ButtonFlags.DPadUp) != 0;
        bool down = (buttons & (uint)ButtonFlags.DPadDown) != 0;
        bool left = (buttons & (uint)ButtonFlags.DPadLeft) != 0;
        bool right = (buttons & (uint)ButtonFlags.DPadRight) != 0;
        if (up && right) return NE;
        if (up && left) return NW;
        if (down && right) return SE;
        if (down && left) return SW;
        if (up) return N;
        if (down) return S;
        if (left) return W;
        if (right) return E;
        return None;
    }
}
