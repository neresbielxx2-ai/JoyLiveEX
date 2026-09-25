using Xunit;
using VirtualJoyCon.ControllerModel;
using VirtualJoyCon.Diagnostics;
using VirtualJoyCon.Network.Link;
using VirtualJoyCon.Network.Protocol;

namespace VirtualJoyCon.Core.Tests;

/// <summary>
/// End-to-end state machine test of VjcLink over an in-memory pump. This is the
/// exact handshake the Android app (Kotlin) and the daemon (Java) implement, so
/// a red test here means both platforms break. Frames go through queues to keep
/// ordering identical to a real network (no re-entrancy shortcuts).
/// </summary>
public class HandshakeTests
{
    private static Logger TempLogger()
        => new(Path.Combine(Path.GetTempPath(), "vjc-tests-" + Guid.NewGuid().ToString("N")[..8]));

    private sealed class Pump
    {
        public readonly Queue<byte[]> ToA = new();
        public readonly Queue<byte[]> ToB = new();
        public VjcLink? A, B;

        public void SendToA(byte[] f) => ToA.Enqueue(f);
        public void SendToB(byte[] f) => ToB.Enqueue(f);

        public int Run(int maxRounds = 64)
        {
            int n = 0;
            while ((ToA.Count > 0 || ToB.Count > 0) && n++ < maxRounds)
            {
                while (ToA.Count > 0) A!.Deliver(ToA.Dequeue());
                while (ToB.Count > 0) B!.Deliver(ToB.Dequeue());
            }
            return n;
        }
    }

    private static (VjcLink a, VjcLink b, Pump pump) Pair(string codeA, string codeB)
    {
        var log = TempLogger();
        var pump = new Pump();
        var a = new VjcLink(true, () => codeA, (_, buf) => pump.SendToB(buf.ToArray()), log);
        var b = new VjcLink(false, () => codeB, (_, buf) => pump.SendToA(buf.ToArray()), log);
        pump.A = a;
        pump.B = b;
        return (a, b, pump);
    }

    [Fact]
    public void CorrectCode_ReachesReady_AndStreamsState()
    {
        var (a, b, pump) = Pair("8F4K-72LM", "8F4K-72LM");
        ControllerState? got = null;
        b.StateReceived += (_, s) => got = s;   // server sends STATE_S -> client (b) receives
        bool aOpened = false, bOpened = false;
        a.Opened += _ => aOpened = true;
        b.Opened += _ => bOpened = true;

        b.StartClientHandshake();
        pump.Run();

        Assert.True(aOpened);
        Assert.True(bOpened);
        Assert.Equal(VjcLink.PhaseReady, a.Phase);
        Assert.Equal(VjcLink.PhaseReady, b.Phase);

        // server -> client state (this direction is what the daemon consumes)
        var s = new ControllerState { Buttons = (uint)ButtonFlags.B, LeftX = 0.75f };
        a.SendState(s);
        pump.Run();
        Assert.NotNull(got);
        Assert.Equal(0.75f, got!.LeftX, 3);

        // client -> server state (what the phone's touch UI sends)
        ControllerState? fromDevice = null;
        a.StateReceived += (_, st) => fromDevice = st;
        b.SendState(s);
        pump.Run();
        Assert.NotNull(fromDevice);
        Assert.Equal(0.75f, fromDevice!.LeftX, 3);
    }

    [Fact]
    public void WrongCode_ServerFailsAuth()
    {
        var (a, b, pump) = Pair("8F4K-72LM", "WRONG-CODE");
        bool authFailed = false;
        a.AuthFailed += _ => authFailed = true;
        b.StartClientHandshake();
        pump.Run();
        Assert.True(authFailed);
        Assert.NotEqual(VjcLink.PhaseReady, a.Phase);
    }

    [Fact]
    public void PingPong_ComputesLatency()
    {
        var (a, b, pump) = Pair("CODE-1234", "CODE-1234");
        b.StartClientHandshake();
        pump.Run();
        // trigger a ping through the tick path on the server link
        for (int i = 0; i < 40 && !a.Rtt.HasSamples; i++)
        {
            a.ForcePingForTests();
            pump.Run();
            Thread.Sleep(25);
        }
        Assert.True(a.Rtt.HasSamples);
    }
}
