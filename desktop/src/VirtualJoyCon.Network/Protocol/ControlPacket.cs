using VirtualJoyCon.ControllerModel;

namespace VirtualJoyCon.Network.Protocol;

/// <summary>Payload of STATE packets (both directions). 25 bytes on the wire.</summary>
public struct StatePayload
{
    public uint Buttons;
    public short LeftX, LeftY, RightX, RightY;   // -32767..32767
    public byte Lt, Rt;                          // triggers 0..255
    public byte Hat;                             // see Hat class
    public byte Flags;                           // b0 = control-active on device
    public uint TimestampMsLo;                   // low 32 bits of ms timestamp

    public void Write(Span<byte> dst, ref int pos)
    {
        Bin.WriteU32(dst, ref pos, Buttons);
        Bin.WriteU16(dst, ref pos, unchecked((ushort)LeftX));
        Bin.WriteU16(dst, ref pos, unchecked((ushort)LeftY));
        Bin.WriteU16(dst, ref pos, unchecked((ushort)RightX));
        Bin.WriteU16(dst, ref pos, unchecked((ushort)RightY));
        dst[pos++] = Lt;
        dst[pos++] = Rt;
        dst[pos++] = Hat;
        dst[pos++] = Flags;
        Bin.WriteU32(dst, ref pos, TimestampMsLo);
    }

    public static StatePayload Read(ReadOnlySpan<byte> src, ref int pos)
    {
        var p = new StatePayload
        {
            Buttons = Bin.ReadU32(src, ref pos),
            LeftX = unchecked((short)Bin.ReadU16(src, ref pos)),
            LeftY = unchecked((short)Bin.ReadU16(src, ref pos)),
            RightX = unchecked((short)Bin.ReadU16(src, ref pos)),
            RightY = unchecked((short)Bin.ReadU16(src, ref pos)),
        };
        p.Lt = src[pos++];
        p.Rt = src[pos++];
        p.Hat = src[pos++];
        p.Flags = src[pos++];
        p.TimestampMsLo = Bin.ReadU32(src, ref pos);
        return p;
    }

    public static StatePayload From(ControllerState s)
    {
        var ts = s.TimestampMs;
        return new StatePayload
        {
            Buttons = s.Buttons,
            LeftX = ControllerState.ToShort(s.LeftX),
            LeftY = ControllerState.ToShort(s.LeftY),
            RightX = ControllerState.ToShort(s.RightX),
            RightY = ControllerState.ToShort(s.RightY),
            Lt = ControllerState.ToByte(s.LeftTrigger),
            Rt = ControllerState.ToByte(s.RightTrigger),
            Hat = Hat.FromState(s.Buttons),
            Flags = 0,
            TimestampMsLo = (uint)(ts & 0xFFFFFFFF),
        };
    }

    public ControllerState To()
    {
        // Hat is authoritative for the d-pad: materialize it back into button bits so
        // consumers only ever look at Buttons.
        uint b = Buttons & ~(uint)(ButtonFlags.DPadUp | ButtonFlags.DPadDown | ButtonFlags.DPadLeft | ButtonFlags.DPadRight);
        b |= Hat switch
        {
            Hat.N => (uint)ButtonFlags.DPadUp,
            Hat.S => (uint)ButtonFlags.DPadDown,
            Hat.W => (uint)ButtonFlags.DPadLeft,
            Hat.E => (uint)ButtonFlags.DPadRight,
            Hat.NE => (uint)(ButtonFlags.DPadUp | ButtonFlags.DPadRight),
            Hat.NW => (uint)(ButtonFlags.DPadUp | ButtonFlags.DPadLeft),
            Hat.SE => (uint)(ButtonFlags.DPadDown | ButtonFlags.DPadRight),
            Hat.SW => (uint)(ButtonFlags.DPadDown | ButtonFlags.DPadLeft),
            _ => 0u,
        };
        var s = new ControllerState
        {
            Buttons = b,
            LeftX = ControllerState.FromShort(LeftX),
            LeftY = ControllerState.FromShort(LeftY),
            RightX = ControllerState.FromShort(RightX),
            RightY = ControllerState.FromShort(RightY),
            LeftTrigger = ControllerState.FromByte(Lt),
            RightTrigger = ControllerState.FromByte(Rt),
        };
        return s;
    }
}

/// <summary>Device -> PC status report (section 2: android state + service state).</summary>
public struct StatusPayload
{
    public byte ServiceState;   // 0 stopped, 1 starting, 2 running, 3 error
    public byte InputMode;      // 0 none, 1 in-app, 2 adb-daemon, 3 accessibility-touch
    public ushort RttMs;        // device-measured RTT to PC
    public byte LossX100;       // device-measured loss (0..10000 /100)
    public string Model = "";
    public string DeviceName = "";
    public int SdkInt;
    public ushort AppVersion;   // e.g. 0x0100 => 1.0

    public byte[] Write()
    {
        var modelBytes = System.Text.Encoding.UTF8.GetBytes(Model);
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(DeviceName);
        var buf = new byte[1 + 1 + 2 + 1 + 2 + modelBytes.Length + 2 + nameBytes.Length + 1 + 2];
        int pos = 0;
        buf[pos++] = ServiceState;
        buf[pos++] = InputMode;
        Bin.WriteU16(buf, ref pos, RttMs);
        buf[pos++] = LossX100;
        Bin.WriteU16(buf, ref pos, (ushort)modelBytes.Length);
        modelBytes.CopyTo(buf.AsSpan(pos));
        pos += modelBytes.Length;
        Bin.WriteU16(buf, ref pos, (ushort)nameBytes.Length);
        nameBytes.CopyTo(buf.AsSpan(pos));
        pos += nameBytes.Length;
        buf[pos++] = (byte)Math.Clamp(SdkInt, 0, 255);
        Bin.WriteU16(buf, ref pos, AppVersion);
        return buf;
    }

    public static StatusPayload Read(ReadOnlySpan<byte> src)
    {
        var p = new StatusPayload();
        int pos = 0;
        p.ServiceState = src[pos++];
        p.InputMode = src[pos++];
        p.RttMs = Bin.ReadU16(src, ref pos);
        p.LossX100 = src[pos++];
        var ml = Bin.ReadU16(src, ref pos);
        p.Model = System.Text.Encoding.UTF8.GetString(src.Slice(pos, ml));
        pos += ml;
        var nl = Bin.ReadU16(src, ref pos);
        p.DeviceName = System.Text.Encoding.UTF8.GetString(src.Slice(pos, nl));
        pos += nl;
        p.SdkInt = src[pos++];
        p.AppVersion = Bin.ReadU16(src, ref pos);
        return p;
    }
}
