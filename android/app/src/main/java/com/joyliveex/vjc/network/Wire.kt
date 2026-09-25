package com.joyliveex.vjc.network

import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * Wire constants + codec. Mirror of Wire.cs / Constants.java — change all three together.
 */
object Wire {
    const val VERSION = 1.toByte()
    const val TAG = 16
    const val OVERHEAD = 12
    const val PBKDF2_ITER = 60_000
    const val STATE_SIZE = 20            // 4+2+2+2+2+1+1+1+1+4 (matches C# StatePayloadSize)
    const val MAX_PACKET = 1200
    const val RELAY_MAGIC = 0x31434A56  // "VJC1"
    const val RELAY_HDR = 13            // magic4 + op1 + room8

    const val T_SALT_C = 0x01
    const val T_SALT_S = 0x02
    const val T_ERROR = 0x03
    const val T_HELLO = 0x04
    const val T_WELCOME = 0x05
    const val T_STATE_D = 0x10
    const val T_STATE_S = 0x11
    const val T_PING = 0x12
    const val T_PONG = 0x13
    const val T_HEARTBEAT = 0x14
    const val T_CMD = 0x20
    const val T_STATUS = 0x21
    const val T_LOG = 0x22

    const val ROLE_APP = 0.toByte()

    const val CMD_VIBRATE = 1
    const val CMD_START_DAEMON = 2
    const val CMD_STOP_DAEMON = 3
    const val CMD_SET_MODE = 4
    const val CMD_CONFIG = 5
    const val CMD_RELEASE_ALL = 7

    const val RELAY_OP_JOIN = 1.toByte()
    const val RELAY_OP_PEER_INFO = 2.toByte()
    const val RELAY_OP_WRAP = 3.toByte()
    const val RELAY_OP_JOIN_ACK = 4.toByte()

    fun le(): ByteBuffer = ByteBuffer.allocate(2048).order(ByteOrder.LITTLE_ENDIAN)
}

class Packet(
    val type: Int,
    val session: Int,
    val seq: Int,
    val payload: ByteArray,
) {
    companion object {
        /** returns null on malformed input; for raw datagrams (UDP) the whole datagram is one packet */
        fun parse(buf: ByteArray, offset: Int = 0, length: Int = buf.size): Packet? {
            if (length < Wire.OVERHEAD) return null
            if (buf[offset] != Wire.VERSION) return null
            val bb = ByteBuffer.wrap(buf, offset, length).order(ByteOrder.LITTLE_ENDIAN)
            bb.position(1)
            val type = bb.get().toInt() and 0xFF
            val session = bb.int
            val seq = bb.int
            val encLen = bb.short.toInt() and 0xFFFF
            if (encLen > length - Wire.OVERHEAD) return null
            val payload = ByteArray(encLen)
            bb.get(payload)
            return Packet(type, session, seq, payload)
        }

        fun build(type: Int, session: Int, seq: Int, payload: ByteArray): ByteArray {
            val b = ByteBuffer.allocate(Wire.OVERHEAD + payload.size).order(ByteOrder.LITTLE_ENDIAN)
            b.put(Wire.VERSION)
            b.put(type.toByte())
            b.putInt(session)
            b.putInt(seq)
            b.putShort(payload.size.toShort())
            b.put(payload)
            return b.array()
        }
    }
}

object Framing {
    fun withLen(packet: ByteArray): ByteArray {
        val out = ByteBuffer.allocate(2 + packet.size) // default BIG_ENDIAN -> u16 BE length
        out.putShort(packet.size.toShort())
        out.put(packet)
        return out.array()
    }

    /** read exactly n bytes from an InputStream; null on clean EOF at start */
    fun readExact(stream: java.io.InputStream, n: Int): ByteArray? {
        val buf = ByteArray(n)
        var off = 0
        while (off < n) {
            val r = stream.read(buf, off, n - off)
            if (r < 0) {
                if (off == 0) return null
                throw java.io.IOException("eof mid-frame")
            }
            off += r
        }
        return buf

    }

    fun readFrame(stream: java.io.InputStream): ByteArray? {
        val len = readExact(stream, 2) ?: return null
        val n = ((len[0].toInt() and 0xFF) shl 8) or (len[1].toInt() and 0xFF)
        if (n <= 0 || n > Wire.MAX_PACKET) throw java.io.IOException("frame too large")
        return readExact(stream, n)
    }
}
