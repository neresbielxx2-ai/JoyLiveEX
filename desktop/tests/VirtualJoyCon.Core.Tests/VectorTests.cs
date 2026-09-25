using VirtualJoyCon.ControllerModel;
using VirtualJoyCon.Network.Crypto;
using VirtualJoyCon.Network.Protocol;
using Xunit;

namespace VirtualJoyCon.Core.Tests;

/// <summary>
/// Vectors from docs/TESTVECTORS.md (generated with pure python hashlib).
/// If these break, the Android side breaks silently — do not "fix" tests
/// without regenerating BOTH platforms.
/// </summary>
public class VectorTests
{
    [Fact]
    public void Pbkdf2_MatchesPythonReference()
    {
        var cs = Enumerable.Repeat((byte)3, 8).ToArray();
        var ss = Enumerable.Repeat((byte)7, 8).ToArray();
        var key = VjcCrypto.DeriveSessionKey("8F4K-72LM", cs, ss);
        Assert.Equal("28d04d2809187c866ee06e662958015a7295a591cf8715115034013bc37d3f69", Convert.ToHexString(key).ToLowerInvariant());
    }

    [Fact]
    public void SessionId_MatchesPythonReference()
    {
        var cs = Enumerable.Repeat((byte)3, 8).ToArray();
        var ss = Enumerable.Repeat((byte)7, 8).ToArray();
        var sid = VjcCrypto.ComputeSessionId(cs, ss);
        Assert.Equal(0x20A269FFu, sid);
    }

    [Fact]
    public void StatePayload_MatchesGoldenBytes()
    {
        var s = new ControllerState
        {
            Buttons = (uint)ButtonFlags.A | (uint)ButtonFlags.ZL | (uint)ButtonFlags.DPadUp,
            LeftX = 0.5f, LeftY = -0.25f,
            RightX = -1.0f, RightY = 0.25f,
            LeftTrigger = 1f, RightTrigger = 64f / 255f,
        };
        var pay = StatePayload.From(s);
        pay.TimestampMsLo = 0x12345678; // golden vector uses a fixed timestamp
        var buf = new byte[20];
        int pos = 0;
        pay.Write(buf, ref pos);
        Assert.Equal(20, pos);
        Assert.Equal("41400000004000e001800020ff40010078563412", Convert.ToHexString(buf).ToLowerInvariant());
    }

    [Fact]
    public void PacketCodec_Roundtrip()
    {
        Span<byte> buf = stackalloc byte[64];
        var payload = new byte[] { 1, 2, 3, 4 };
        int n = PacketCodec.Pack(Wire.TypeStateS, 0x20A269FF, 101, payload, buf);
        Assert.True(Packet.TryParse(buf.Slice(0, n), out var pkt));
        Assert.Equal(Wire.TypeStateS, pkt.Type);
        Assert.Equal(0x20A269FFu, pkt.Session);
        Assert.Equal(101u, pkt.Seq);
        Assert.Equal(payload, pkt.Payload.ToArray());
    }

    [Fact]
    public void Packet_FullGoldenPacket_Parses()
    {
        var raw = Convert.FromHexString("0111ff69a22065000000140041400000004000e001800020ff40010078563412");
        Assert.True(Packet.TryParse(raw, out var pkt));
        Assert.Equal(0x11, pkt.Type);
        Assert.Equal(0x20A269FFu, pkt.Session);
        Assert.Equal(101u, pkt.Seq);
        int pos = 0;
        var sp = StatePayload.Read(pkt.Payload, ref pos);
        Assert.Equal((uint)ButtonFlags.A | (uint)ButtonFlags.ZL | (uint)ButtonFlags.DPadUp, sp.Buttons);
        Assert.Equal(1, sp.Hat);
        Assert.Equal(0x12345678u, sp.TimestampMsLo);
        var st = sp.To();
        Assert.Equal(0.5f, st.LeftX, 3);
        Assert.Equal(-0.25f, st.LeftY, 3);
        Assert.Equal(-1f, st.RightX, 3);
    }

    [Fact]
    public void AesGcm_Roundtrip_WithDerivedKey()
    {
        var cs = Enumerable.Repeat((byte)3, 8).ToArray();
        var ss = Enumerable.Repeat((byte)7, 8).ToArray();
        var key = VjcCrypto.DeriveSessionKey("8F4K72LM", cs, ss);
        var sid = VjcCrypto.ComputeSessionId(cs, ss);
        var plain = "state:test"u8.ToArray();
        var enc = VjcCrypto.Encrypt(key, 1, sid, 7, plain);
        var dec = VjcCrypto.Decrypt(key, 1, sid, 7, enc);
        Assert.NotNull(dec);
        Assert.Equal(plain, dec);
        Assert.Null(VjcCrypto.Decrypt(key, 1, sid, 8, enc)); // wrong seq → tag failure
    }
}

public class CurveTests
{
    [Fact]
    public void Deadzone_ZeroesSmallMotion()
    {
        var c = new StickCurve { Deadzone = 0.2f };
        c.Process(0.15f, 0f, out float x, out float y);
        Assert.Equal(0f, x);
        Assert.Equal(0f, y);
    }

    [Fact]
    public void OutsideDeadzone_MonotonicAndCapped()
    {
        var c = new StickCurve { Deadzone = 0.1f, Sensitivity = 1f };
        float prev = -1f;
        for (float v = 0.2f; v <= 1.0f; v += 0.1f)
        {
            c.Process(v, 0f, out float x, out _);
            Assert.True(x > prev, "monotonic");
            prev = x;
        }
        Assert.True(prev <= 1.0001f);
    }

    [Fact]
    public void InvertY_FlipsSign()
    {
        var c = new StickCurve { Deadzone = 0f, InvertY = true };
        c.Process(0f, 0.5f, out _, out float y);
        Assert.Equal(-0.5f, y, 2);
    }

    [Fact]
    public void MaxIntensity_Clamps()
    {
        var c = new StickCurve { Deadzone = 0f, MaxIntensity = 0.5f };
        c.Process(1f, 1f, out float x, out float y);
        Assert.True(MathF.Sqrt(x * x + y * y) <= 0.501f);
    }

    [Fact]
    public void Hat_FromState()
    {
        uint b = (uint)ButtonFlags.DPadUp | (uint)ButtonFlags.DPadRight;
        Assert.Equal(HatCode.NE, HatCode.FromState(b));
        Assert.Equal(HatCode.None, HatCode.FromState(0));
    }
}
