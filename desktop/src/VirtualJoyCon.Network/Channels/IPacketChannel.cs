using VirtualJoyCon.Diagnostics;
using VirtualJoyCon.Network.Protocol;

namespace VirtualJoyCon.Network.Channels;

/// <summary>
/// One byte-oriented transport (TCP socket or datagram "flow"). The link only needs
/// Send(frame) + a callback for received frames; framing lives in the implementations.
/// </summary>
public interface IPacketChannel : IDisposable
{
    void Send(ReadOnlySpan<byte> packet);
    event Action<ReadOnlySpan<byte>, ReadOnlySpan<byte>>? FrameReceived;
    event Action<Exception?>? Closed;
    string Description { get; }
    /// <summary>For datagram channels: allow the link to rebind the peer endpoint (P2P upgrade).</summary>
    bool SupportsEndpointSwitch => false;
    void SetRemoteEndpoint(System.Net.IPEndPoint ep) { }
    System.Net.IPEndPoint? LocalOrRemoteEndpoint => null;
}

/// <summary>TCP framing: {u16 BE length} + packet. Used by adb reverse and LAN TCP pairing.</summary>
public sealed class TcpChannel : IPacketChannel
{
    private readonly System.Net.Sockets.NetworkStream _stream;
    private readonly byte[] _len = new byte[2];
    private readonly byte[] _readBuf = new byte[Wire.MaxPacketSize + 4];
    private int _pending;
    private readonly System.Net.Sockets.Socket _socket;
    private volatile bool _disposed;

    public event Action<ReadOnlySpan<byte>, ReadOnlySpan<byte>>? FrameReceived;
    public event Action<Exception?>? Closed;
    public string Description { get; }

    public TcpChannel(System.Net.Sockets.Socket socket)
    {
        _socket = socket;
        _socket.NoDelay = true;
        _stream = new System.Net.Sockets.NetworkStream(socket);
        Description = $"tcp:{socket.RemoteEndPoint}";
        _ = ReadLoop();
    }

    public void Send(ReadOnlySpan<byte> packet)
    {
        if (_disposed) return;
        try
        {
            Span<byte> full = stackalloc byte[2 + packet.Length];
            Bin.PutU16BE(full, 0, (ushort)packet.Length);
            packet.CopyTo(full.Slice(2));
            _stream.Write(full);
            _stream.Flush();
        }
        catch (Exception ex)
        {
            Closed?.Invoke(ex);
            Dispose();
        }
    }

    private async Task ReadLoop()
    {
        try
        {
            while (!_disposed)
            {
                int need = (_pending == 0) ? 2 : (_pending + 2 - _pendingRead);
                if (need == 0) { Deliver(); continue; }
                int n = await _stream.ReadAsync(_readBuf.AsMemory(_pendingRead == 0 && _pending == 0 ? 0 : _pendingRead, need)).ConfigureAwait(false);
                if (n <= 0) break;
                if (_pending == 0)
                {
                    _pendingRead = 2;
                    if (_pendingRead < 2) continue; // wait for full length
                    _pending = Bin.GetU16BE(_readBuf.AsSpan(0, 2)) + 2;
                }
                else
                {
                    _pendingRead += n;
                }
                if (_pendingRead >= _pending) Deliver();
            }
        }
        catch (Exception ex) when (!_disposed)
        {
            Closed?.Invoke(ex);
        }
        finally
        {
            if (!_disposed) Closed?.Invoke(null);
            Dispose();
        }

        void Deliver()
        {
            FrameReceived?.Invoke(_readBuf.AsSpan(2, _pending - 2), _readBuf.AsSpan(0, _pending));
            _pending = 0; _pendingRead = 0;
        }
    }

    // _pendingRead = bytes read so far (including the 2 length bytes once complete)
    private int _pendingRead;

    public void Dispose()
    {
        _disposed = true;
        try { _socket.Dispose(); } catch { }
        try { _stream.Dispose(); } catch { }
    }
}

/// <summary>
/// Datagram channel over a shared UdpClient, addressed to one remote endpoint.
/// Handles optional relay encapsulation {u32 magic, u8 op, u8[8] room} + payload.
/// </summary>
public sealed class UdpChannel : IPacketChannel
{
    private readonly UdpPump _pump;
    private System.Net.IPEndPoint _remote;
    private readonly byte[] _wrapBuf = new byte[Wire.MaxPacketSize];
    private readonly string? _relayRoom;   // non-null while routed via relay
    private volatile bool _attached;
    private readonly object _gate = new();

    public event Action<ReadOnlySpan<byte>, ReadOnlySpan<byte>>? FrameReceived;
    public event Action<Exception?>? Closed;
    public string Description => _relayRoom == null ? $"udp:{_remote}" : $"udp:relay[{_relayRoom}]->{_remote}";
    public override bool SupportsEndpointSwitch => true;
    public System.Net.IPEndPoint? LocalOrRemoteEndpoint => _remote;

    public UdpChannel(UdpPump pump, System.Net.IPEndPoint remote, string? relayRoom)
    {
        _pump = pump;
        _remote = remote;
        _relayRoom = relayRoom;
    }

    /// <summary>Server side: start receiving from any endpoint matching this link (unknown peers).</summary>
    public void AttachPump()
    {
        if (_attached) return;
        _pump.BindChannel(_remote, this);
        _attached = true;
    }

    /// <summary>
    /// P2P upgrade: once raw (non-relay) traffic is seen from the peer's public endpoint,
    /// bypass the relay. Returns true when this datagram should be processed.
    /// </summary>
    public bool OnRawFromOther(System.Net.IPEndPoint from, ReadOnlySpan<byte> data)
    {
        lock (_gate)
        {
            if (_relayRoom == null) return false;
            _relayRoom = null;
            _remote = from;
        }
        FrameReceived?.Invoke(data, data);
        return true;
    }

    public void SetRemoteEndpoint(System.Net.IPEndPoint ep)
    {
        lock (_gate) _remote = ep;
    }

    public void Send(ReadOnlySpan<byte> packet)
    {
        string? room;
        System.Net.IPEndPoint remote;
        lock (_gate) { room = _relayRoom; remote = _remote; }
        try
        {
            if (room == null)
            {
                _pump.SendTo(packet, remote);
            }
            else
            {
                int n = Wire.RelayHeaderSize + packet.Length;
                if (n > _wrapBuf.Length) return;
                Span<byte> b = _wrapBuf.AsSpan(0, n);
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b, Wire.RelayMagic);
                b[4] = Wire.RelayOpWrap;
                System.Text.Encoding.ASCII.GetBytes(room).AsSpan(0, 8).CopyTo(b.Slice(5));
                packet.CopyTo(b.Slice(Wire.RelayHeaderSize));
                _pump.SendToRaw(b, remote);
            }
        }
        catch (Exception ex)
        {
            Closed?.Invoke(ex);
        }
    }

    /// <summary>Called by the pump when a *relay-wrapped* datagram for our room arrives.</summary>
    internal void DeliverRelay(ReadOnlySpan<byte> strippedPayload, System.Net.IPEndPoint viaRelay)
    {
        FrameReceived?.Invoke(strippedPayload, strippedPayload);
    }

    public void Dispose()
    {
        if (_attached) _pump.UnbindChannel(_remote, this);
    }
}

/// <summary>Interface implemented by the UDP socket owner to route frames to bound channels.</summary>
public interface IUdpPump
{
    void BindChannel(System.Net.IPEndPoint remote, UdpChannel channel);
    void UnbindChannel(System.Net.IPEndPoint remote, UdpChannel channel);
    void SendTo(ReadOnlySpan<byte> packet, System.Net.IPEndPoint remote);
    void SendToRaw(ReadOnlySpan<byte> frame, System.Net.IPEndPoint remote);
}
