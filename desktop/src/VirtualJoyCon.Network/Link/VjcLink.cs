using System.Diagnostics;
using VirtualJoyCon.ControllerModel;
using VirtualJoyCon.Diagnostics;
using VirtualJoyCon.Network.Crypto;
using VirtualJoyCon.Network.Protocol;

namespace VirtualJoyCon.Network.Link;

public enum LinkTransport { Lan = 0, Usb = 1, Internet = 2 }

/// <summary>
/// One authenticated VJC session between the PC (server) and an Android peer
/// (app or input daemon). Owns the handshake, AES-GCM sealing, replay window,
/// sequence-gap loss tracking, heartbeat/ping and timeout detection.
/// Transport-agnostic: frames arrive via <see cref="Deliver"/> and leave via the
/// <c>sendFrame</c> delegate supplied by the LinkManager.
/// </summary>
public sealed class VjcLink
{
    public const int PhaseGreeting = 0;
    public const int PhaseSalting = 1;
    public const int PhaseHelloPending = 2;
    public const int PhaseReady = 3;
    public const int PhaseFailed = 4;

    private readonly bool _isServer;
    private readonly Func<string> _codeProvider;
    private readonly Action<VjcLink, byte[]> _sendFrame;
    private readonly Logger _log;

    private byte[] _clientSalt = Array.Empty<byte>();
    private byte[] _serverSalt = Array.Empty<byte>();
    private byte[]? _key;
    private uint _sessionId;

    private uint _sendSeq;
    private uint _lastRecvSeq = uint.MaxValue;
    private long _lastRecvSw = Stopwatch.GetTimestamp();

    private readonly object _gate = new();

    public int Phase { get; private set; } = PhaseGreeting;
    public bool IsServer => _isServer;
    public LinkTransport Transport { get; internal set; } = LinkTransport.Lan;
    public bool IsDirect { get; internal set; } = true;   // false while relay-routed
    public bool Alive => Phase == PhaseReady && !_disposed;

    // Peer identity (filled on hello)
    public byte PeerRole { get; private set; } = Wire.RoleApp;
    public string PeerName { get; private set; } = "";
    public string PeerModel { get; private set; } = "";
    public int PeerSdkInt { get; private set; }

    // stats
    public readonly LatencyStats Rtt = new();
    public readonly LossTracker Loss = new();
    public DateTime LastActivityUtc { get; private set; } = DateTime.UtcNow;

    public int HeartbeatMs = 1000;
    public int PingMs = 1000;
    public int TimeoutMs = 8000;
    public int StateSendHz = 125;

    private long _lastBeatSw, _lastPingSw, _lastStateSw, _lastPongT0;

    // ---- events (dispatched on the receive thread; handlers must be quick) ----
    public event Action<VjcLink>? Opened;
    public event Action<VjcLink, string>? Closed;
    public event Action<VjcLink, ControllerState>? StateReceived;
    public event Action<VjcLink, StatusPayload>? StatusReceived;
    public event Action<VjcLink, byte, string>? LogReceived;
    public event Action<VjcLink>? AuthFailed;
    private bool _disposed;
    private readonly Stopwatch _sw = Stopwatch.StartNew();

    public VjcLink(bool isServer, Func<string> codeProvider, Action<VjcLink, byte[]> sendFrame, Logger log)
    {
        _isServer = isServer;
        _codeProvider = codeProvider;
        _sendFrame = sendFrame;
        _log = log;
        _lastBeatSw = _lastPingSw = _lastStateSw = Stopwatch.GetTimestamp();
    }

    public string Describe => $"link[{(IsServer ? "srv" : "cli")} {(PeerRole == Wire.RoleDaemon ? "daemon" : "app")} {PeerName}|{Transport}{(IsDirect ? "" : "|relay")}]";

    // ============================ send ============================

    private void SendPacket(byte type, ReadOnlySpan<byte> payload, bool encrypted)
    {
        if (_disposed) return;
        var buf = new byte[Wire.Overhead + payload.Length];
        uint seq = 0;
        if (encrypted && _key != null)
        {
            var enc = VjcCrypto.Encrypt(_key, (byte)(_isServer ? 0 : 1), _sessionId, _sendSeq, payload);
            buf = new byte[Wire.Overhead + enc.Length];
            PacketCodec.Pack(type, _sessionId, _sendSeq, enc, buf);
            seq = _sendSeq;
        }
        else
        {
            PacketCodec.Pack(type, _sessionId, _sendSeq, payload, buf);
            seq = _sendSeq;
        }
        _sendSeq = unchecked(_sendSeq + 1);
        try { _sendFrame(this, buf); } catch { }
        _ = seq;
    }

    public void SendSalt()
    {
        var salt = VjcCrypto.RandomSalt();
        if (_isServer) _serverSalt = salt; else _clientSalt = salt;
        var buf = new byte[8];
        salt.CopyTo(buf, 0);
        SendPacket(_isServer ? Wire.TypeSaltServer : Wire.TypeSaltClient, buf, encrypted: false);
    }

    /// <summary>Client side: call right after attaching the transport to start SALT_C → HELLO.</summary>
    public void StartClientHandshake()
    {
        Phase = PhaseSalting;
        SendSalt(); // sends TypeSaltClient
    }

    /// <summary>Client side only (used by tests / future PC-to-PC); Android mirrors this path.</summary>
    public void SendHello()
    {
        var name = Environment.MachineName;
        var nb = System.Text.Encoding.UTF8.GetBytes(name);
        var payload = new byte[1 + 1 + 1 + 1 + nb.Length];
        int p = 0;
        payload[p++] = Wire.Version;
        payload[p++] = Wire.RoleApp;
        payload[p++] = 0; // api level (pc n/a)
        payload[p++] = (byte)nb.Length;
        nb.CopyTo(payload, p);
        SendPacket(Wire.TypeHello, payload, encrypted: true);
    }

    /// <summary>Server sends STATE_S (device consumes); client sends STATE_D (PC consumes).</summary>
    public void SendState(ControllerState s)
    {
        var pay = StatePayload.From(s);
        Span<byte> b = stackalloc byte[StatePayloadSize];
        int pos = 0;
        pay.Write(b, ref pos);
        SendPacket(_isServer ? Wire.TypeStateS : Wire.TypeStateD, b.Slice(0, pos), encrypted: true);
        _lastStateSw = _sw.ElapsedMilliseconds;
    }

    /// <summary>Test/daemon glue: force a ping now instead of waiting for the timer.</summary>
    public void ForcePingForTests() => SendPing();

    public const int StatePayloadSize = 20;

    public void SendCmd(byte cmd, ReadOnlySpan<byte> arg)
    {
        var payload = new byte[1 + arg.Length];
        payload[0] = cmd;
        arg.CopyTo(payload.AsSpan(1));
        SendPacket(Wire.TypeCmd, payload, encrypted: true);
    }

    public void SendError(byte reason, string msg)
    {
        var mb = System.Text.Encoding.UTF8.GetBytes(msg);
        var payload = new byte[2 + mb.Length];
        payload[0] = reason;
        payload[1] = (byte)Math.Min(mb.Length, 250);
        mb.AsSpan(0, payload[1]).CopyTo(payload.AsSpan(2));
        SendPacket(Wire.TypeError, payload, encrypted: false);
    }

    private void SendHeartbeat() => SendPacket(Wire.TypeHeartbeat, ReadOnlySpan<byte>.Empty, encrypted: true);
    private void SendPing()
    {
        Span<byte> b = stackalloc byte[8];
        // unix ms: PING/PONG cross .NET and Java runtimes, so it must be wall-clock
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(b, (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        SendPacket(Wire.TypePing, b, encrypted: true);
    }
    private void SendPong(ulong t0)
    {
        Span<byte> b = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt64LittleEndian(b, t0);
        SendPacket(Wire.TypePong, b, encrypted: true);
    }

    // ============================ receive ============================

    /// <summary>Entry point for one received frame (already de-framed by the channel).</summary>
    public void Deliver(ReadOnlySpan<byte> frame)
    {
        if (_disposed) return;
        if (!PacketCodec.TryParse(frame, out var pkt)) return;

        lock (_gate)
        {
            _lastRecvSw = Stopwatch.GetTimestamp();
            LastActivityUtc = DateTime.UtcNow;

            if (pkt.Type == Wire.TypeSaltClient && _isServer && Phase == PhaseGreeting)
            {
                if (pkt.Payload.Length < 8) return;
                _clientSalt = pkt.Payload.Slice(0, 8).ToArray();
                SendSalt(); // reply SALT_S + sets server salt
                DeriveKeys();
                Phase = PhaseHelloPending;
                return;
            }
            if (pkt.Type == Wire.TypeSaltServer && !_isServer && Phase == PhaseSalting)
            {
                _serverSalt = pkt.Payload.Slice(0, 8).ToArray();
                DeriveKeys();
                SendHello();
                Phase = PhaseHelloPending;
                return;
            }
            if (pkt.Type == Wire.TypeSaltClient && !_isServer && Phase == PhaseGreeting)
            {
                // server initiated: rare, but symmetric
                _clientSalt = pkt.Payload.Slice(0, 8).ToArray();
                Phase = PhaseSalting;
                SendSalt();
                DeriveKeys();
                SendHello();
                Phase = PhaseHelloPending;
                return;
            }

            if (pkt.Type == Wire.TypeError)
            {
                Phase = PhaseFailed;
                if (pkt.Payload.Length >= 1)
                    _log.Warn("NET", $"{Describe}: peer error reason={pkt.Payload[0]}");
                Close("peer-error");
                return;
            }

            if (Phase != PhaseReady && Phase != PhaseHelloPending) return;
            if (Phase == PhaseReady && pkt.Session != _sessionId) return;

            // ---- sequence/replay bookkeeping on the sealed layer ----
            if (_lastRecvSeq != uint.MaxValue)
            {
                long delta = (long)pkt.Seq - (long)_lastRecvSeq;
                if (delta <= 0) return;       // duplicate / late replay inside or outside the window
                Loss.Note(pkt.Seq);
                _lastRecvSeq = pkt.Seq;
            }
            else
            {
                Loss.Resync(pkt.Seq);
                _lastRecvSeq = pkt.Seq;
            }

            byte dir = (byte)(_isServer ? 1 : 0); // packets arriving carry the peer's direction
            byte[]? plain = null;
            if (_key != null)
                plain = VjcCrypto.Decrypt(_key, dir, _sessionId, pkt.Seq, pkt.Payload);
            if (plain == null)
            {
                // wrong pairing code / forged frame: count + drop (server side)
                if (Phase == PhaseHelloPending && _isServer)
                {
                    try { AuthFailed?.Invoke(this); } catch { }
                    Close("auth-failed");
                }
                return;
            }

            HandlePlain(pkt.Type, plain, pkt.Seq);
        }
    }

    private void HandlePlain(byte type, byte[] p, uint seq)
    {
        switch (type)
        {
            case Wire.TypeHello when _isServer:
            {
                int pos = 0;
                byte ver = p[pos++];
                PeerRole = p[pos++];
                PeerSdkInt = p[pos++];
                int nlen = p[pos++];
                PeerName = System.Text.Encoding.UTF8.GetString(p, pos, Math.Min(nlen, p.Length - pos));
                pos += nlen;
                if (pos < p.Length)
                {
                    int mlen = p[pos++];
                    PeerModel = System.Text.Encoding.UTF8.GetString(p, pos, Math.Min(mlen, p.Length - pos));
                }
                if (ver != Wire.Version)
                {
                    SendError(Wire.ErrProto, "proto mismatch");
                    Phase = PhaseFailed;
                    Close("proto");
                    return;
                }
                Span<byte> w = stackalloc byte[5];
                w[0] = Wire.Version; w[1] = 1; w[2] = (byte)Transport;
                w[3] = (byte)(HeartbeatMs >> 8); w[4] = (byte)HeartbeatMs;
                SendPacket(Wire.TypeWelcome, w, encrypted: true);
                Phase = PhaseReady;
                _log.Info("NET", $"{Describe} opened (peer={PeerName} model={PeerModel} role={PeerRole})");
                try { Opened?.Invoke(this); } catch { }
                break;
            }
            case Wire.TypeWelcome when !_isServer:
            {
                if (p.Length >= 2 && p[1] == 1)
                {
                    Phase = PhaseReady;
                    try { Opened?.Invoke(this); } catch { }
                }
                else
                {
                    AuthFailed = true;
                    Phase = PhaseFailed;
                    Close("denied");
                }
                break;
            }
            case Wire.TypeStateD when _isServer:
            case Wire.TypeStateS when !_isServer:
            {
                int pos = 0;
                var sp = StatePayload.Read(p.AsSpan(0, Math.Min(p.Length, StatePayloadSize)), ref pos);
                try { StateReceived?.Invoke(this, sp.To()); } catch { }
                break;
            }
            case Wire.TypeStatus:
            {
                try { StatusReceived?.Invoke(this, StatusPayload.Read(p)); } catch { }
                break;
            }
            case Wire.TypeLog:
            {
                if (p.Length >= 3)
                {
                    byte lvl = p[0];
                    int len = (p[1] << 8) | p[2];
                    string msg = System.Text.Encoding.UTF8.GetString(p, 3, Math.Min(len, p.Length - 3));
                    try { LogReceived?.Invoke(this, lvl, msg); } catch { }
                }
                break;
            }
            case Wire.TypePing:
            {
                if (p.Length >= 8)
                {
                    var t0 = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(p);
                    SendPong(t0);
                }
                break;
            }
            case Wire.TypePong:
            {
                if (p.Length >= 8)
                {
                    var t0 = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(p);
                    Rtt.AddSample(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - t0);
                    _lastPongT0 = t0;
                }
                break;
            }
            case Wire.TypeHeartbeat:
                break; // keep-alive only
        }
    }

    private void DeriveKeys()
    {
        var code = _codeProvider();
        var cs = _clientSalt.Length == 8 ? _clientSalt : new byte[8];
        var ss = _serverSalt.Length == 8 ? _serverSalt : new byte[8];
        _key = VjcCrypto.DeriveSessionKey(code, cs, ss);
        _sessionId = VjcCrypto.ComputeSessionId(cs, ss);
    }

    // ============================ timers ============================

    /// <summary>Called ~10Hz by the hub: heartbeat, ping, timeout detection.</summary>
    public void Tick()
    {
        if (_disposed) return;
        var now = _sw.ElapsedMilliseconds;

        if (Phase == PhaseReady)
        {
            if (now - _lastBeatSw >= HeartbeatMs) { _lastBeatSw = now; SendHeartbeat(); }
            if (now - _lastPingSw >= PingMs) { _lastPingSw = now; SendPing(); }
        }

        long idleMs = (Stopwatch.GetTimestamp() - _lastRecvSw) * 1000 / Stopwatch.Frequency;
        if (Phase != PhaseFailed && idleMs > TimeoutMs)
        {
            _log.Warn("NET", $"{Describe}: timeout after {idleMs} ms of silence");
            Close("timeout");
        }
        _ = now;
        _ = _lastStateSw;
        _ = _lastPongT0;
    }

    public void Close(string reason)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Phase = PhaseFailed;
        }
        _log.Info("NET", $"{Describe}: closed ({reason})");
        try { Closed?.Invoke(this, reason); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Phase = PhaseFailed;
    }

    public uint SessionId => _sessionId;
    public double LossPercent => Loss.LossPercent;
    public double LatencyMs => Rtt.AverageMs;
    public bool HasLatency => Rtt.HasSamples;
}
