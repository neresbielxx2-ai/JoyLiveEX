namespace VirtualJoyCon.ControllerModel;

/// <summary>
/// Full instantaneous state of the virtual controller pair (left + right Joy-Con
/// working together as one gamepad). Axes are normalized: -1.0 .. +1.0.
/// On the wire, axes become short (-32767..32767) and triggers byte (0..255).
/// </summary>
public sealed class ControllerState
{
    public uint Buttons;

    public float LeftX, LeftY;   // Y: -1 = up, +1 = down (matches raw screen convention; invert applied at source)
    public float RightX, RightY;
    public float LeftTrigger;    // 0..1 (ZL pressure — Joy-Cons report digital, but wire carries analog)
    public float RightTrigger;   // 0..1 (ZR pressure)

    public long TimestampMs;

    public ControllerState Clone() => (ControllerState)MemberwiseClone();

    public void CopyFrom(ControllerState other)
    {
        Buttons = other.Buttons;
        LeftX = other.LeftX; LeftY = other.LeftY;
        RightX = other.RightX; RightY = other.RightY;
        LeftTrigger = other.LeftTrigger; RightTrigger = other.RightTrigger;
        TimestampMs = other.TimestampMs;
    }

    public bool ContentEquals(ControllerState o) =>
        Buttons == o.Buttons
        && ToShort(LeftX) == ToShort(o.LeftX)
        && ToShort(LeftY) == ToShort(o.LeftY)
        && ToShort(RightX) == ToShort(o.RightX)
        && ToShort(RightY) == ToShort(o.RightY)
        && ToByte(LeftTrigger) == ToByte(o.LeftTrigger)
        && ToByte(RightTrigger) == ToByte(o.RightTrigger);

    public static short ToShort(float v) => (short)Math.Round(Math.Clamp(v, -1f, 1f) * 32767f);
    public static float FromShort(short v) => v / 32767f;
    public static byte ToByte(float v) => (byte)Math.Round(Math.Clamp(v, 0f, 1f) * 255f);
    public static float FromByte(byte v) => v / 255f;

    public bool IsIdle =>
        Buttons == 0
        && LeftX == 0 && LeftY == 0 && RightX == 0 && RightY == 0
        && LeftTrigger == 0 && RightTrigger == 0;
}

public enum JoyConSide { Left, Right }

/// <summary>Which half of the pair a visual element belongs to.</summary>
public sealed record JoyConLayout(string Id, string DisplayName, JoyConSide Side);
