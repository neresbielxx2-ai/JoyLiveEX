using System.Runtime.InteropServices;

namespace VirtualJoyCon.InputEngine;

/// <summary>
/// Direct P/Invoke to xinput1_4.dll — no external dependency, Xbox and any
/// XInput-mode generic gamepad works (DualShock/Pro controllers generally
/// expose an XInput mode via their vendor software; the project intentionally
/// does not silently install anything).
/// </summary>
public static class XInputNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputVibration
    {
        public ushort wLeftMotorSpeed;
        public ushort wRightMotorSpeed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputCapabilities
    {
        public byte Type;
        public byte SubType;
        public byte Flags;
        public XInputGamepad Gamepad;
        public XInputVibration Vibration;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    private const int XUSER_MAX_COUNT = 4;
    private const int ERROR_SUCCESS = 0;

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetStateRaw(uint userIndex, out XInputState state);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetCapabilities")]
    private static extern int XInputGetCapabilitiesRaw(uint userIndex, uint flags, out XInputCapabilities caps);

    [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
    private static extern int XInputSetStateRaw(uint userIndex, uint flags, ref XInputVibration vibration);

    public static bool Available { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            _ = XInputGetCapabilitiesRaw(0, 0, out _);
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }

    public static bool GetState(uint index, out XInputState state)
    {
        if (!Available || index >= XUSER_MAX_COUNT)
        {
            state = default;
            return false;
        }
        return XInputGetStateRaw(index, out state) == ERROR_SUCCESS;
    }

    public static bool GetCapabilities(uint index, out XInputCapabilities caps)
    {
        caps = default;
        if (!Available || index >= XUSER_MAX_COUNT) return false;
        return XInputGetCapabilitiesRaw(index, 0, out caps) == ERROR_SUCCESS;
    }

    public static void SetVibration(uint index, ushort left, ushort right)
    {
        if (!Available || index >= XUSER_MAX_COUNT) return;
        var v = new XInputVibration { wLeftMotorSpeed = left, wRightMotorSpeed = right };
        try { XInputSetStateRaw(index, 0, ref v); } catch { }
    }

    public static string DescribeType(byte type) => type switch
    {
        0 => "Gamepad",
        1 => "Wheel",
        2 => "Arcade Stick",
        3 => "Keyboard",
        5 => "Remote",
        _ => "Controller",
    };
}
