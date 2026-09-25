using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using VirtualJoyCon.Diagnostics;
using VirtualJoyCon.Network.Channels;
using VirtualJoyCon.Network.Crypto;
using VirtualJoyCon.Network.Link;
using VirtualJoyCon.Network.Protocol;

namespace VirtualJoyCon.Network;

public enum NetMode { Idle, Lan, UsbTcp, Internet }

/// <summary>
/// Owns every transport (LAN UDP + TCP listener, adb TCP listener, relay UDP) and
/// the set of live VjcLinks. The desktop hub pushes controller state here; the
/// manager forwards it to ready daemon links and replies to status/ping traffic.
/// </summary>
public sealed class LinkManager : IDisposable
{
    public const string LanOpenCode = "JOYLIVE0"; // "no pairing needed" LAN default
    private const string DiscoverProbe = "VJCDprobe!";

    private readonly Logger _log;
    private readonly Func<string> _codeProvider;

    private UdpPump? _udp;
    private TcpListener? _tcp;
    private Task? _acceptTask;
    private CancellationTokenSource? _acceptCts;

    private readonly ConcurrentDictionary<VjcLink, LinkBinding> _links = new();
    private readonly object _relayGate = new();
    private IPEndPoint? _relayEp;
    private string? _relayRoom;
    private long _lastRelayJoinTicks;
    private long _authFails;
    private long _lastAuthFailTicks;
    private int _authFailWindow;

    public NetMode Mode { get; private set; } = NetMode.Idle;
    public bool RelayActive { get; private set; }

    public event Action<VjcLink>? LinkOpened;
    public event Action<VjcLink, string>? LinkClosed;
    public event Action<VjcLink, ControllerModel.ControllerState>? StateReceived;
    public event Action<VjcLink, StatusPayload>? StatusReceived;
    public event Action<VjcLink, byte, string>? PeerLog;
    /// <summary>True when at least one READY daemon/app link exists.</summary>
    public bool AnyReady { get { foreach (var l in _links.Keys) if (l.Phase == VjcLink.PhaseReady) return true; return false; } }

    private sealed class LinkBinding
    {
        public IPEndPoint? UdpRemote;
        public TcpClient? Tcp;
        public NetworkStream? Stream;
        public Action<VjcLink, byte[]> Send = null!;
    }

    public LinkManager(Logger log, Func<string> codeProvider)
    {
        _log = log;
        _codeProvider = codeProvider;
    }

    // ======================= lifecycle =======================

    public void StartLan(int udpPort, int tcpPort)
    {
        Stop();
        _udp = new UdpPump(udpPort);
        _udp.Unrouted += OnUnrouted;
        _udp.RelayPayload += OnRelayPayload;
        _udp.RelayControl += OnRelayControl;
        try
        {
            _tcp = new TcpListener(IPAddress.Any, tcpPort);
            _tcp.Start();
            _acceptCts = new CancellationTokenSource();
            _acceptTask = Task.Run(() => AcceptLoop(_tcp, _acceptCts.Token, tag: "LAN"));
        }
        catch (SocketException ex)
        {
            _log.Warn("NET", $"TCP listener on {tcpPort} unavailable: {ex.Message}");
        }
        Mode = NetMode.Lan;
        _log.Info("NET", $"LAN listening udp+tcp :{udpPort}/{tcpPort}");
    }

    public void StartTcpOnly(int port)
    {
        Stop();
        _tcp = new TcpListener(IPAddress.Any, port);
        _tcp.Start();
        _acceptCts = new CancellationTokenSource();
        _acceptTask = Task.Run(() => AcceptLoop(_tcp, _acceptCts.Token, tag: "ADB"));
        Mode = NetMode.UsbTcp;
        _log.Info("NET", $"USB/ADB TCP listener on :{port} (adb reverse)");
    }

    public void StartRelay(string host, int port, string roomCode)
    {
        lock (_relayGate)
        {
            _relayEp = new IPEndPoint(Dns.GetHostAddresses(host).First(a => a.AddressFamily == AddressFamily.InterNetwork), port);
            _relayRoom = roomCode;
            RelayActive = true;
        }
        _udp ??= CreateRelayOnlyUdp();
        JoinRelay();
        Mode = NetMode.Internet;
        _log.Info("NET", $"Relay join -> {host}:{port} room {roomCode}");
    }

    private UdpPump CreateRelayOnlyUdp()
    {
        var p = new UdpPump(0);
        p.Unrouted += OnUnrouted;
        p.RelayPayload += OnRelayPayload;
        p.RelayControl += OnRelayControl;
        return p;
    }

    public void StopRelay()
    {
        lock (_relayGate) { _relayEp = null; _relayRoom = null; RelayActive = false; }
    }

    public void Stop()
    {
        foreach (var b in _links.Values) b.Tcp?.Dispose();
        _links.Clear();
        StopRelay();
        _acceptCts?.Cancel();
        try { _tcp?.Stop(); } catch { }
        _tcp = null;
        if (_udp != null) { _udp.Unrouted -= OnUnrouted; _udp.RelayPayload -= OnRelayPayload; _udp.RelayControl -= OnRelayControl; _udp.Dispose(); }
        _udp = null;
        Mode = NetMode.Idle;
    }

    private async Task AcceptLoop(TcpListener listener, CancellationToken tok, string tag)
    {
        while (!tok.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(tok).ConfigureAwait(false); }
            catch { break; }
            try
            {
                client.NoDelay = true;
                var stream = client.GetStream();
                var link = NewLink(isServer: true);
                var binding = new LinkBinding
                {
                    Tcp = client,
                    Stream = stream,
                };
                binding.Send = (l, buf) =>
                {
                    try
                    {
                        Span<byte> framed = stackalloc byte[buf.Length + 2];
                        Bin.PutU16BE(framed, 0, (ushort)buf.Length);
                        buf.CopyTo(framed.Slice(2));
                        stream.Write(framed);
                        stream.Flush();
                    }
                    catch { }
                };
                link.Transport = tag == "ADB" ? LinkTransport.Usb : LinkTransport.Lan;
                _links[link] = binding;
                _ = ReadTcpLoop(link, stream, tag);
                _log.Info("NET", $"{tag} TCP client accepted from {SafeEp(client)}");
            }
            catch (Exception ex)
            {
                _log.Warn("NET", $"accept({tag}) failed: {ex.Message}");
                try { client.Dispose(); } catch { }
            }
        }
    }

    private static string SafeEp(TcpClient c)
    {
        try { return c.Client.RemoteEndPoint?.ToString() ?? "?"; } catch { return "?"; }
    }

    private async Task ReadTcpLoop(VjcLink link, NetworkStream stream, string tag)
    {
        var len = new byte[2];
        var buf = new byte[Wire.MaxPacketSize];
        try
        {
            while (link.Alive || link.Phase <= VjcLink.PhaseHelloPending)
            {
                await stream.ReadExactlyAsync(len).ConfigureAwait(false);
                int n = Bin.GetU16BE(len, 0);
                if (n <= 0 || n > Wire.MaxPacketSize) break;
                await stream.ReadExactlyAsync(buf.AsMemory(0, n)).ConfigureAwait(false);
                link.Deliver(new ReadOnlySpan<byte>(buf, 0, n));
            }
        }
        catch (Exception) { /* socket closed */ }
        finally
        {
            if (_links.TryRemove(link, out var b)) b.Tcp?.Dispose();
            link.Close(tag + "-tcp-closed");
        }
    }

    // ======================= udp routing =======================

    private void OnUnrouted(byte[] data, int len, IPEndPoint from)
    {
        // Discovery probe: answer with our TCP/UDP ports + pairing code so the APK can auto-fill.
        if (len >= DiscoverProbe.Length && Encoding.ASCII.GetString(data, 0, DiscoverProbe.Length) == DiscoverProbe)
        {
            ReplyProbe(from);
            return;
        }

        // Existing link bound to this endpoint?
        foreach (var (link, b) in _links)
        {
            if (b.UdpRemote != null && b.UdpRemote.Equals(from))
            {
                link.Deliver(new ReadOnlySpan<byte>(data, 0, len));
                return;
            }
        }

        // P2P upgrade or new direct link: parse header.
        if (!PacketCodec.TryParse(data.AsSpan(0, len), out var pkt)) return;

        if (pkt.Type == Wire.TypeSaltClient)
        {
            if (RateLimited()) return;
            var link = NewLink(isServer: true);
            link.Transport = LinkTransport.Lan;
            _links[link] = new LinkBinding
            {
                UdpRemote = from,
                Send = (l, buf) => _udp?.SendTo(buf, from),
            };
            link.Deliver(new ReadOnlySpan<byte>(data, 0, len));
            return;
        }

        // Sealed packet from an endpoint with no binding: candidate for P2P direct upgrade
        // (the peer started bypassing the relay).
        foreach (var (link, b) in _links)
        {
            if (link.Phase == VjcLink.PhaseReady && !link.IsDirect && b.UdpRemote != null &&
                link.SessionId == pkt.Session)
            {
                link.IsDirect = true;
                b.UdpRemote = from;
                _log.Info("NET", $"P2P direct path established with {from} (relay bypassed)");
                link.Deliver(new ReadOnlySpan<byte>(data, 0, len));
                return;
            }
        }
    }

    private void ReplyProbe(IPEndPoint to)
    {
        // { "VJCDrepl!1", u16 tcpPort, u8 codeLen, code } — tiny, plaintext, no state change.
        var code = Encoding.ASCII.GetBytes(PairingCode.ToWireCode(_codeProvider()));
        var resp = new byte[10 + 2 + 1 + code.Length];
        Encoding.ASCII.GetBytes("VJCDrepl!1").CopyTo(resp, 0);
        int port = _tcp?.LocalEndpoint is IPEndPoint tp ? tp.Port : _udp?.BoundPort ?? 0;
        resp[10] = (byte)(port >> 8);
        resp[11] = (byte)port;
        resp[12] = (byte)Math.Min(code.Length, 250);
        code.CopyTo(resp.AsSpan(13, resp[12]));
        _udp?.SendTo(resp, to);
    }

    private void OnRelayPayload(string room, byte[] payload)
    {
        if (_relayRoom == null || room != _relayRoom) return;
        foreach (var (link, b) in _links)
        {
            if (!link.IsDirect && b.UdpRemote != null && b.UdpRemote.Equals(_relayEp))
            {
                link.Deliver(payload);
                return;
            }
        }
        // First packet from a peer while we are only in relay mode: create link on relay path.
        if (PacketCodec.TryParse(payload, out var pkt) && pkt.Type == Wire.TypeSaltClient)
        {
            if (RateLimited()) return;
            var link = NewLink(isServer: true);
            link.Transport = LinkTransport.Internet;
            link.IsDirect = false;
            var ep = _relayEp!;
            _links[link] = new LinkBinding
            {
                UdpRemote = ep,
                Send = (l, buf) => SendViaRelay(buf),
            };
            link.Deliver(payload);
        }
    }

    private void OnRelayControl(byte op, string room, IPEndPoint? peerEp)
    {
        if (_relayRoom == null || room != _relayRoom) return;
        if (op == Wire.RelayOpPeerInfo && peerEp != null && _links.Count > 0)
        {
            foreach (var (link, b) in _links)
            {
                if (!link.IsDirect)
                {
                    _log.Info("NET", $"relay reported peer endpoint {peerEp} — probing direct path");
                }
            }
        }
    }

    private void SendViaRelay(byte[] packet)
    {
        IPEndPoint? ep; string? room;
        lock (_relayGate) { ep = _relayEp; room = _relayRoom; }
        if (ep == null || room == null) return;
        var buf = new byte[Wire.RelayHeaderSize + packet.Length];
        BitConverter.TryWriteBytes(buf.AsSpan(0, 4), Wire.RelayMagic);
        buf[4] = Wire.RelayOpWrap;
        Encoding.ASCII.GetBytes(room).AsSpan(0, Wire.RoomCodeLen).CopyTo(buf.AsSpan(5, Wire.RoomCodeLen));
        packet.CopyTo(buf.AsSpan(Wire.RelayHeaderSize));
        _udp?.SendTo(buf, ep);
    }

    private void JoinRelay()
    {
        lock (_relayGate) _lastRelayJoinTicks = Environment.TickCount64;
    }

    /// <summary>Tick from the hub (~10 Hz): link timers + relay room keep-alive join.</summary>
    public void Tick()
    {
        foreach (var (link, _) in _links)
            link.Tick();
        if (RelayActive && _udp != null && Environment.TickCount64 - Volatile.Read(ref _lastRelayJoinTicks) > 5000)
        {
            IPEndPoint? ep; string? room;
            lock (_relayGate) { ep = _relayEp; room = _relayRoom; }
            if (ep != null && room != null)
            {
                var join = new byte[Wire.RelayHeaderSize];
                BitConverter.TryWriteBytes(join.AsSpan(0, 4), Wire.RelayMagic);
                join[4] = Wire.RelayOpJoin;
                Encoding.ASCII.GetBytes(room).AsSpan(0, Wire.RoomCodeLen).CopyTo(join.AsSpan(5, Wire.RoomCodeLen));
                _udp.SendTo(join, ep);
                Volatile.Write(ref _lastRelayJoinTicks, Environment.TickCount64);
            }
        }
    }

    private VjcLink NewLink(bool isServer)
    {
        var link = new VjcLink(isServer, _codeProvider, SendFrame, _log);
        link.StateReceived += (l, s) => StateReceived?.Invoke(l, s);
        link.StatusReceived += (l, st) => StatusReceived?.Invoke(l, st);
        link.LogReceived += (l, lvl, msg) => PeerLog?.Invoke(l, lvl, msg);
        link.Opened += OnLinkOpened;
        link.Closed += OnLinkClosedInternal;
        link.AuthFailed += l => { NoteAuthFailure(); _log.Warn("NET", $"auth failure from {l.Describe} — wrong pairing code?"); };
        return link;
    }

    private void OnLinkOpened(VjcLink link)
    {
        // one App + one Daemon max; a newer connection takes over the old slot
        foreach (var (other, _) in _links)
        {
            if (other != link && other.Phase == VjcLink.PhaseReady && other.PeerRole == link.PeerRole)
                other.Close("replaced-by-new-peer");
        }
        LinkOpened?.Invoke(link);
    }

    private void OnLinkClosedInternal(VjcLink link, string reason)
    {
        _links.TryRemove(link, out var b);
        b?.Tcp?.Dispose();
        LinkClosed?.Invoke(link, reason);
    }

    private void SendFrame(VjcLink link, byte[] buf)
    {
        if (_links.TryGetValue(link, out var b))
        {
            b.Send?.Invoke(link, buf);
        }
    }

    // ======================= policy =======================

    private bool RateLimited()
    {
        var now = Environment.TickCount64;
        if (now - Volatile.Read(ref _lastAuthFailTicks) > 10_000)
        {
            Interlocked.Exchange(ref _authFailWindow, 0);
            Volatile.Write(ref _lastAuthFailTicks, now);
        }
        return Interlocked.Increment(ref _authFailWindow) > 8; // >8 attempts / 10 s -> drop
    }

    public void NoteAuthFailure()
    {
        Interlocked.Increment(ref _authFails);
        Volatile.Write(ref _lastAuthFailTicks, Environment.TickCount64);
        Interlocked.Increment(ref _authFailWindow);
    }

    /// <summary>Broadcast controller state to all READY daemon links (device injectors).</summary>
    public void SendState(ControllerModel.ControllerState s)
    {
        foreach (var (link, _) in _links)
            if (link.Phase == VjcLink.PhaseReady && link.PeerRole == Wire.RoleDaemon)
                link.SendState(s);
    }

    /// <summary>Send state to ALL ready links (app UI mirrors it while PC controls are used).</summary>
    public void SendStateToAllReady(ControllerModel.ControllerState s)
    {
        foreach (var (link, _) in _links)
            if (link.Phase == VjcLink.PhaseReady)
                link.SendState(s);
    }

    public void SendCmdToAll(byte cmd, ReadOnlySpan<byte> arg)
    {
        foreach (var (link, _) in _links)
            if (link.Phase == VjcLink.PhaseReady)
                link.SendCmd(cmd, arg);
    }

    public int ReadyLinkCount
    {
        get
        {
            int n = 0;
            foreach (var (l, _) in _links)
                if (l.Phase == VjcLink.PhaseReady) n++;
            return n;
        }
    }

    public VjcLink[] SnapshotLinks() => _links.Keys.ToArray();

    public void Dispose() => Stop();
}
