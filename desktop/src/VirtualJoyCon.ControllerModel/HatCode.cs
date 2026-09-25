namespace VirtualJoyCon.ControllerModel;

/// <summary>
/// 4-bit hat encoding on the wire (matches HID / Android AXIS_HAT conventions and
/// the Kotlin Buttons.HAT_* + daemon mapping). Bit-exact spec — do not reorder.
/// </summary>
public static class HatCode
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

    public static uint ToButtons(byte hat) => hat switch
    {
        N => (uint)ButtonFlags.DPadUp,
        S => (uint)ButtonFlags.DPadDown,
        W => (uint)ButtonFlags.DPadLeft,
        E => (uint)ButtonFlags.DPadRight,
        NE => (uint)(ButtonFlags.DPadUp | ButtonFlags.DPadRight),
        NW => (uint)(ButtonFlags.DPadUp | ButtonFlags.DPadLeft),
        SE => (uint)(ButtonFlags.DPadDown | ButtonFlags.DPadRight),
        SW => (uint)(ButtonFlags.DPadDown | ButtonFlags.DPadLeft),
        _ => 0u,
    };
}
