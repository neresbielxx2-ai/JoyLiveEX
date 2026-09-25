using System.Net;
using System.Net.Sockets;
using VirtualJoyCon.Network.Protocol;

namespace VirtualJoyCon.Network.Channels;

/// <summary>
/// One bound UDP socket shared by the LAN listener, relay sessions and P2P probing.
/// Reusing the same socket for relay + direct traffic is what lets a P2P upgrade
/// happen without a second hole-punch exchange (both ends keep the same NAT mapping).
/// </summary>
public sealed class UdpPump : IDisposable
{
    private readonly UdpClient _client;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _rxTask;
    private volatile bool _closed;

    /// <summary>Everything not relay-wrapped (new peers, LAN direct traffic, probes).</summary>
    public event Action<byte[], int, IPEndPoint>? Unrouted;

    /// <summary>Relay control traffic: join-ack / peer endpoint announcement.</summary>
    public event Action<byte, string, IPEndPoint?>? RelayControl;

    /// <summary>Relay-wrapped payload for a room (already stripped by this pump).</summary>
    public event Action<string, byte[]>? RelayPayload;

    public int BoundPort => ((IPEndPoint)_client.Client.LocalEndPoint!).Port;
    public bool IsClosed => _closed;

    public UdpPump(int port)
    {
        _client = new UdpClient(AddressFamily.InterNetwork);
        _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _client.ExclusiveAddressUse = false;
        _client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        _client.Client.ReceiveBufferSize = 1 << 17;
        _client.Client.SendBufferSize = 1 << 15;
        _rxTask = Task.Run(ReceiveLoop);
    }

    private async Task ReceiveLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try { r = await _client.ReceiveAsync(_cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { continue; }
            catch (ObjectDisposedException) { break; }
            try { Dispatch(r.Buffer, r.RemoteEndPoint); } catch { /* one bad datagram must never kill rx */ }
        }
        _closed = true;
    }

    private void Dispatch(byte[] data, IPEndPoint from)
    {
        if (data.Length >= 5 &&
            data[0] == (byte)'V' && data[1] == (byte)'J' && data[2] == (byte)'C' && data[3] == (byte)'1' &&
            BitConverter.ToUInt32(data, 0) == Wire.RelayMagic &&
            data.Length >= Wire.RelayHeaderSize)
        {
            byte op = data[4];
            string room = System.Text.Encoding.ASCII.GetString(data, 5, Wire.RoomCodeLen);
            if (op == Wire.RelayOpWrap)
            {
                var stripped = new byte[data.Length - Wire.RelayHeaderSize];
                Buffer.BlockCopy(data, Wire.RelayHeaderSize, stripped, 0, stripped.Length);
                RelayPayload?.Invoke(room, stripped);
                return;
            }
            IPEndPoint? peerEp = null;
            if (op == Wire.RelayOpPeerInfo && data.Length >= Wire.RelayHeaderSize + 6)
            {
                var ip = new byte[4];
                Array.Copy(data, Wire.RelayHeaderSize, ip, 0, 4);
                int port = (data[Wire.RelayHeaderSize + 4] << 8) | data[Wire.RelayHeaderSize + 5];
                peerEp = new IPEndPoint(new IPAddress(ip), port);
            }
            RelayControl?.Invoke(op, room, peerEp);
            return;
        }
        Unrouted?.Invoke(data, data.Length, from);
    }

    public void SendTo(ReadOnlySpan<byte> packet, IPEndPoint remote)
    {
        if (_closed) return;
        try { _client.Send(packet, remote); }
        catch (SocketException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        _closed = true;
        _cts.Cancel();
        try { _client.Dispose(); } catch { }
        try { _rxTask.Wait(200); } catch { }
    }
}
