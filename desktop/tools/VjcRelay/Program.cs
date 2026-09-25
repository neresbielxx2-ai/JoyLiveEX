using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

// VjcRelay — tiny, self-hostable UDP relay for Virtual Joy-Con Internet mode.
//
// It only forwards datagrams between the two members of a room identified by the
// 8-char pairing code. It cannot read the payload (AES-GCM sealed end-to-end) and
// stores nothing except a 30-second room membership cache. No paid service is
// required: run `VjcRelay [port]` on any box with a public UDP port (or Docker).
//
// Wire (relay envelope): {u32 magic 'VJC1', u8 op, u8[8] room, ...}
//   op=1 JOIN        : member registers its endpoint; relay answers JOIN_ACK(4)
//                      and, if a peer is already in the room, sends PEER_INFO(2)
//                      to BOTH members so they can try a direct P2P path.
//   op=3 WRAP        : forward inner packet to the other room member.
//   op=2 PEER_INFO   : relay -> member {byte[4] ip, byte[2] port BE}

int port = 8722;
if (args.Length > 0 && int.TryParse(args[0], out var p)) port = p;

var rooms = new ConcurrentDictionary<string, RoomEntry>(StringComparer.Ordinal);
byte[] magic = "VJC1"u8.ToArray();

using var udp = new UdpClient(AddressFamily.InterNetwork);
udp.Client.Bind(new IPEndPoint(IPAddress.Any, port));
Console.WriteLine($"[vjc-relay] listening UDP :{port}");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var cleanup = new Timer(_ =>
{
    foreach (var kvp in rooms)
        if (DateTime.UtcNow - kvp.Value.LastSeen > TimeSpan.FromSeconds(30))
            RoomEntry? _dropped;
                rooms.TryRemove(kvp.Key, out _dropped);
}, null, 5000, 5000);

try
{
    while (!cts.IsCancellationRequested)
    {
        UdpReceiveResult r = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
        try { Handle(r.Buffer, r.RemoteEndPoint); }
        catch (Exception ex) { Console.WriteLine($"[vjc-relay] error: {ex.Message}"); }
    }
}
catch (OperationCanceledException) { /* ctrl-c */ }

void Handle(byte[] data, IPEndPoint from)
{
    if (data.Length < 13 || !data.AsSpan(0, 4).SequenceEqual(magic)) return; // not ours
    byte op = data[4];
    string code = Encoding.ASCII.GetString(data, 5, 8);
    if (code.Length != 8 || !code.All(char.IsLetterOrDigit)) return;

    var entry = rooms.GetOrAdd(code, static _ => new RoomEntry());
    lock (entry)
    {
        entry.LastSeen = DateTime.UtcNow;
        switch (op)
        {
            case 1: // JOIN
            {
                IPEndPoint? other = null;
                if (entry.A != null && !entry.A.Equals(from)) { entry.B = from; other = entry.A; }
                else if (entry.B != null && !entry.B.Equals(from)) { entry.A = from; other = entry.B; }
                else if (entry.A == null) { entry.A = from; }
                else { entry.B = from; }

                udp.Send(MakeJoinAck(code), from);
                if (other != null)
                {
                    udp.Send(MakePeerInfo(code, from), other);
                    udp.Send(MakePeerInfo(code, other), from);
                    Console.WriteLine($"[vjc-relay] room {code}: pair {entry.A} <-> {entry.B}");
                }
                break;
            }
            case 3: // WRAP -> forward to peer untouched
            {
                if (from.Equals(entry.A) && entry.B != null) udp.Send(data, entry.B);
                else if (from.Equals(entry.B) && entry.A != null) udp.Send(data, entry.A);
                break;
            }
        }
    }
}

static byte[] MakeJoinAck(string code)
{
    var buf = new byte[13];
    "VJC1"u8.CopyTo(buf);
    buf[4] = 4;
    Encoding.ASCII.GetBytes(code).CopyTo(buf, 5);
    return buf;
}

static byte[] MakePeerInfo(string code, IPEndPoint ep)
{
    var buf = new byte[13 + 6];
    "VJC1"u8.CopyTo(buf);
    buf[4] = 2;
    Encoding.ASCII.GetBytes(code).CopyTo(buf, 5);
    ep.Address.GetAddressBytes().CopyTo(buf, 13);
    buf[17] = (byte)(ep.Port >> 8);
    buf[18] = (byte)ep.Port;
    return buf;
}

sealed class RoomEntry
{
    public IPEndPoint? A, B;
    public DateTime LastSeen = DateTime.UtcNow;
}
