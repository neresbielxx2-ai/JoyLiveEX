package com.joyliveex.vjc.network

import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetSocketAddress
import java.nio.charset.StandardCharsets

/**
 * UDP transport with optional relay encapsulation and automatic P2P upgrade:
 *  - with `room`, packets go out wrapped as {VJC1|op=WRAP|room8|packet} to the relay
 *  - the relay answers JOIN_ACK/PEER_INFO; once a *raw* VJC packet arrives from
 *    the peer's public endpoint we bypass the relay (lower latency, no bandwidth
 *    through the middleman). LAN mode = no room, plain datagrams.
 */
class UdpTransport(
    private val target: InetSocketAddress,
    private val room: String?,
) : Transport {

    private val socket = DatagramSocket().apply { soTimeout = 200 }
    private val recv = ByteArray(Wire.MAX_PACKET + 64)
    private var directEp: InetSocketAddress? = null
    private var lastJoin = 0L
    private val peerInfo = java.util.concurrent.atomic.AtomicReference<InetSocketAddress?>(null)

    @Volatile var isDirect: Boolean = false
        private set

    override fun send(packet: ByteArray) {
        maybeJoin()
        val ep = directEp ?: target
        val out = if (room != null && !isDirect) {
            val r = room.toByteArray(StandardCharsets.US_ASCII)
            val b = ByteArray(Wire.RELAY_HDR + packet.size)
            b[0] = 'V'.code.toByte(); b[1] = 'J'.code.toByte(); b[2] = 'C'.code.toByte(); b[3] = '1'.code.toByte()
            b[4] = Wire.RELAY_OP_WRAP
            r.copyInto(b, 5, 0, 8)
            packet.copyInto(b, Wire.RELAY_HDR)
            b
        } else {
            packet
        }
        socket.send(DatagramPacket(out, out.size, ep))
    }

    override fun receive(timeoutMs: Int): ByteArray? {
        maybeJoin()
        socket.soTimeout = timeoutMs.coerceAtLeast(20)
        val p = DatagramPacket(recv, recv.size)
        try {
            socket.receive(p)
        } catch (e: java.net.SocketTimeoutException) {
            return null
        }
        var off = 0
        var len = p.length
        if (len >= 4 && recv[0] == 'V'.code.toByte() && recv[1] == 'J'.code.toByte() &&
            recv[2] == 'C'.code.toByte() && recv[3] == '1'.code.toByte()
        ) {
            if (len < Wire.RELAY_HDR) return null
            val op = recv[4].toInt()
            val r = String(recv, 5, 8, StandardCharsets.US_ASCII)
            if (room == null || r != room!!) return null
            when (op) {
                (Wire.RELAY_OP_WRAP.toInt()) -> { off = Wire.RELAY_HDR; len -= Wire.RELAY_HDR }
                (Wire.RELAY_OP_PEER_INFO.toInt()) -> {
                    if (len >= Wire.RELAY_HDR + 6) {
                        val ip = ByteArray(4)
                        recv.copyInto(ip, 0, Wire.RELAY_HDR, Wire.RELAY_HDR + 4)
                        val port = ((recv[Wire.RELAY_HDR + 4].toInt() and 0xFF) shl 8) or
                            (recv[Wire.RELAY_HDR + 5].toInt() and 0xFF)
                        peerInfo.set(InetSocketAddress(java.net.InetAddress.getByAddress(ip), port))
                    }
                    return null
                }
                else -> return null // join-ack etc.
            }
        } else if (room != null) {
            // raw VJC datagram from an unexpected endpoint: candidate for direct P2P
            val from = InetSocketAddress(p.address, p.port)
            val pi = peerInfo.get()
            if (directEp == null && (pi == null || from == pi || !isRelay(from))) {
                if (Packet.parse(recv, off, len) != null) {
                    directEp = from
                    isDirect = true
                } else return null
            } else if (from != directEp) {
                return null
            }
        }
        return recv.copyOfRange(off, off + len)
    }

    private fun isRelay(ep: InetSocketAddress) =
        ep.address == target.address && ep.port == target.port

    private fun maybeJoin() {
        val r = room ?: return
        val now = System.currentTimeMillis()
        if (now - lastJoin < 5000) return
        lastJoin = now
        val b = ByteArray(Wire.RELAY_HDR)
        b[0] = 'V'.code.toByte(); b[1] = 'J'.code.toByte(); b[2] = 'C'.code.toByte(); b[3] = '1'.code.toByte()
        b[4] = Wire.RELAY_OP_JOIN
        r.toByteArray(StandardCharsets.US_ASCII).copyInto(b, 5, 0, 8)
        try { socket.send(DatagramPacket(b, b.size, target)) } catch (e: Exception) { }
    }

    override fun close() {
        try { socket.close() } catch (e: Exception) { }
    }
}
