using System.Buffers.Binary;

namespace VirtualJoyCon.Network.Protocol;

/// <summary>
/// Framing of a packet (12-byte header, all little-endian):
///   {u8 ver, u8 type, u32 session, u32 seq, u16 encLen, encLen bytes}
/// Plaintext handshake types (SALT/ERROR) store raw payload in the enc field.
/// </summary>
public static class PacketCodec
{
    /// <summary>Returns total length; throws if dest is too small.</summary>
    public static int Pack(byte type, uint session, uint seq, ReadOnlySpan<byte> payload, Span<byte> dest)
    {
        if (payload.Length > ushort.MaxValue || dest.Length < Wire.Overhead + payload.Length)
            throw new ArgumentException("packet too large");
        dest[0] = Wire.Version;
        dest[1] = type;
        BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(2), session);
        BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(6), seq);
        BinaryPrimitives.WriteUInt16LittleEndian(dest.Slice(10), (ushort)payload.Length);
        payload.CopyTo(dest.Slice(Wire.Overhead));
        return Wire.Overhead + payload.Length;
    }

    public static int PackPlain(byte type, uint session, uint seq, ReadOnlySpan<byte> payload, Span<byte> dest)
        => Pack(type, session, seq, payload, dest);
}

/// <summary>Decoded view of one packet. Payload spans into the receive buffer.</summary>
public readonly struct Packet
{
    public readonly byte Version;
    public readonly byte Type;
    public readonly uint Session;
    public readonly uint Seq;
    public readonly ReadOnlySpan<byte> Payload;

    public Packet(byte version, byte type, uint session, uint seq, ReadOnlySpan<byte> payload)
    {
        Version = version; Type = type; Session = session; Seq = seq; Payload = payload;
    }

    public static bool TryParse(ReadOnlySpan<byte> src, out Packet packet)
    {
        packet = default;
        if (src.Length < Wire.Overhead) return false;
        if (src[0] != Wire.Version) return false;
        ushort encLen = BinaryPrimitives.ReadUInt16LittleEndian(src.Slice(10));
        if (encLen > Wire.MaxPacketSize - Wire.Overhead) return false;
        if (src.Length < Wire.Overhead + encLen) return false;
        packet = new Packet(
            src[0],
            src[1],
            BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(2)),
            BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(6)),
            src.Slice(Wire.Overhead, encLen));
        return true;
    }
}
