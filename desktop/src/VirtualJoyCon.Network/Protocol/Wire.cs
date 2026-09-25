using System.Buffers.Binary;

namespace VirtualJoyCon.Network.Protocol;

/// <summary>
/// Wire protocol definition (docs/PROTOCOL.md). All integers little-endian.
/// This file is the single source of truth on the PC side; Android mirrors it
/// (Kotlin WireConsts.kt + Java daemon Constants.java).
/// </summary>
public static class Wire
{
    public const byte Version = 1;
    public const int RoomCodeLen = 8;                 // "8F4K72LM" (dash removed on the wire)
    public const uint RelayMagic = 0x31434A56;         // "VJC1" little-endian
    public const int MaxPacketSize = 1200;
    public const int Overhead = 1 + 1 + 4 + 4 + 2;     // ver,type,session,seq,encLen
    public const int TagSize = 16;                     // AES-GCM tag
    public const int Pbkdf2Iterations = 60_000;

    // ---- packet types -----------------------------------------------------
    public const byte TypeSaltClient = 0x01;  // PC -> peer  {u8[8] salt}
    public const byte TypeSaltServer = 0x02;  // peer -> PC  {u8[8] salt}
    public const byte TypeError      = 0x03;  // {u8 reason, u8 len, msg} (plaintext)
    public const byte TypeHello      = 0x04;  // peer -> PC  (encrypted)
    public const byte TypeWelcome    = 0x05;  // PC -> peer  (encrypted)
    public const byte TypeStateD     = 0x10;  // device -> PC controller state
    public const byte TypeStateS     = 0x11;  // PC -> device controller state
    public const byte TypePing       = 0x12;  // both, encrypted {u64 t0 ms}
    public const byte TypePong       = 0x13;  // both, encrypted {u64 t0 echo}
    public const byte TypeHeartbeat  = 0x14;  // both, empty payload
    public const byte TypeCmd        = 0x20;  // PC -> device command
    public const byte TypeStatus     = 0x21;  // device -> PC status report
    public const byte TypeLog        = 0x22;  // device -> PC diagnostic line

    // ---- roles ------------------------------------------------------------
    public const byte RoleApp = 0;
    public const byte RoleDaemon = 1;

    // ---- transports -------------------------------------------------------
    public const byte TransportLan = 0;
    public const byte TransportUsb = 1;
    public const byte TransportInternet = 2;

    // ---- commands (TypeCmd payload[0]) ------------------------------------
    public const byte CmdVibrate = 1;          // {u16 ms}
    public const byte CmdStartDaemon = 2;      // request adb daemon lifecycle (informational)
    public const byte CmdStopDaemon = 3;
    public const byte CmdSetMode = 4;          // {u8 mode}
    public const byte CmdConfig = 5;           // {json bytes} tuning forwarded to device UI
    public const byte CmdScreenshot = 6;
    public const byte CmdReleaseAll = 7;       // force device-side release of every held button

    // ---- error reasons ----------------------------------------------------
    public const byte ErrBadAuth = 1;
    public const byte ErrBusy = 2;
    public const byte ErrProto = 3;
    public const byte ErrExpensive = 4;        // rate limited

    /// <summary>STATE payload layout (section 9: only what is needed, nothing else).</summary>
    public const int StateSize = 4 + 2 + 2 + 2 + 2 + 1 + 1 + 1 + 1 + 4;

    // ---- relay header -----------------------------------------------------
    // {u32 magic 'VJC1', u8 op, u8[8] room, payload...}
    public const byte RelayOpJoin = 1;
    public const byte RelayOpPeerInfo = 2;   // relay -> member: {byte[6] peerEndpoint (4 IP + 2 port)}
    public const byte RelayOpWrap = 3;       // member -> relay: forward inner packet to room peer
    public const int RelayHeaderSize = 4 + 1 + RoomCodeLen;
}

/// <summary>Binary helpers used by both serializer sides.</summary>
public static class Bin
{
    public static void WriteU16(Span<byte> dst, ref int pos, ushort v)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(dst.Slice(pos), v);
        pos += 2;
    }
    public static void WriteU32(Span<byte> dst, ref int pos, uint v)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(pos), v);
        pos += 4;
    }
    public static void WriteU64(Span<byte> dst, ref int pos, ulong v)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(dst.Slice(pos), v);
        pos += 8;
    }
    public static ushort ReadU16(ReadOnlySpan<byte> src, ref int pos)
    {
        var v = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(pos));
        pos += 2;
        return v;
    }
    public static uint ReadU32(ReadOnlySpan<byte> src, ref int pos)
    {
        var v = BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(pos));
        pos += 4;
        return v;
    }
    public static ulong ReadU64(ReadOnlySpan<byte> src, ref int pos)
    {
        var v = BinaryPrimitives.ReadUInt64LittleEndian(src.Slice(pos));
        pos += 8;
        return v;
    }

    public static void PutU16BE(Span<byte> dst, int at, ushort v)
    {
        dst[at] = (byte)(v >> 8);
        dst[at + 1] = (byte)v;
    }
    public static ushort GetU16BE(ReadOnlySpan<byte> src, int at) =>
        (ushort)((src[at] << 8) | src[at + 1]);
}
