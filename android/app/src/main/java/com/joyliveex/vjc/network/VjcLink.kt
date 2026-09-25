package com.joyliveex.vjc.network

import com.joyliveex.vjc.controller.Buttons
import com.joyliveex.vjc.controller.ControllerState
import java.security.SecureRandom
import java.util.concurrent.atomic.AtomicBoolean

/**
 * Client-side link (the phone always *dials*; the PC is the server).
 * Same state machine as VjcLink.cs: SALT_C → SALT_S → HELLO → WELCOME, then
 * encrypted STATE/PING/PONG/HEARTBEAT/STATUS/CMD traffic with a shared send
 * sequence used as the AES-GCM nonce component.
 */
class VjcLink(
    private val transport: Transport,
    private val pairingCode: String,
    private val listener: Listener,
) {
    interface Listener {
        fun onOpened()
        fun onClosed(reason: String)
        fun onStateFromPc(s: ControllerState)
        fun onCmd(cmd: Int, arg: ByteArray)
        fun onLog(line: String)
    }

    @Volatile var connected = false
        private set
    @Volatile var peerName = ""
        private set
    @Volatile var rttMs = 0.0
        private set
    @Volatile var lossPct = 0.0
        private set
    @Volatile var direct = false

    private val sendSeq = intArrayOf(0)
    private var recvLastSeq = -1
    private var recvCount = 0L
    private var lostCount = 0L
    private var rttEwma = -1.0
    private val closed = AtomicBoolean(false)

    fun sendState(s: ControllerState) {
        val bb = Wire.le()
        bb.putInt(s.buttons)
        bb.putShort(ControllerState.toShort(s.lx))
        bb.putShort(ControllerState.toShort(s.ly))
        bb.putShort(ControllerState.toShort(s.rx))
        bb.putShort(ControllerState.toShort(s.ry))
        bb.put(ControllerState.toByte(s.lt).toByte())
        bb.put(ControllerState.toByte(s.rt).toByte())
        bb.put(Buttons.hatOf(s.buttons).toByte())
        bb.put(0) // flags
        bb.putInt((System.currentTimeMillis() and 0xFFFFFFFFL).toInt())
        sendSealed(Wire.T_STATE_D, bb.array(), 0, Wire.STATE_SIZE)
    }

    fun status(model: String, serviceState: Int, inputMode: Int, appVer: String): ByteArray {
        val m = model.toByteArray()
        val n = android.os.Build.DEVICE.toByteArray()
        val (major, minor) = appVer.split(".").let {
            (it.getOrNull(0)?.toIntOrNull() ?: 1) to (it.getOrNull(1)?.toIntOrNull() ?: 0)
        }
        val b = ByteArray(2 + 2 + 1 + 2 + m.size + 2 + n.size + 1 + 2)
        var p = 0
        b[p++] = serviceState.toByte()
        b[p++] = inputMode.toByte()
        b[p] = rttMs.toInt().toByte(); b[p + 1] = (rttMs.toInt() shr 8).toByte(); p += 2
        b[p++] = (lossPct.toInt() and 0xFF).toByte()
        b[p] = m.size.toByte(); b[p + 1] = 0; p += 2
        m.copyInto(b, p); p += m.size
        b[p] = n.size.toByte(); b[p + 1] = 0; p += 2
        n.copyInto(b, p); p += n.size
        b[p++] = android.os.Build.VERSION.SDK_INT.toByte()
        b[p] = minor.toByte()   // LE u16 = (major<<8)|minor → C# AppVersion 0x0100 = 1.0
        b[p + 1] = major.toByte()
        return b
    }

    private var key: ByteArray? = null
    private var session = 0

    /** Runs the whole session until it ends; caller (service) handles reconnect. */
    fun run() {
        try {
            val clientSalt = ByteArray(8).also { SecureRandom().nextBytes(it) }
            sendPlain(Wire.T_SALT_C, clientSalt)

            val welcome = loopHandshake(clientSalt)
            if (!welcome) return
            connected = true
            listener.onOpened()
            loopSession()
        } catch (e: Exception) {
            listener.onLog("link: ${e.message}")
        } finally {
            connected = false
            if (closed.compareAndSet(false, true)) {
                transport.close()
                listener.onClosed("ended")
            }
        }
    }

    private fun loopHandshake(clientSalt: ByteArray): Boolean {
        val deadline = System.currentTimeMillis() + 6000
        while (System.currentTimeMillis() < deadline) {
            val frame = transport.receive((deadline - System.currentTimeMillis()).coerceAtMost(1500).toInt())
                ?: continue
            val f = Packet.parse(frame) ?: continue
            when (f.type) {
                Wire.T_SALT_S -> {
                    if (f.payload.size < 8) return false
                    val serverSalt = f.payload.copyOfRange(0, 8)
                    key = VjcCrypto.deriveSessionKey(pairingCode, clientSalt, serverSalt)
                    session = VjcCrypto.computeSessionId(clientSalt, serverSalt)
                    val name = android.os.Build.MODEL.toByteArray()
                    val hello = ByteArray(4 + name.size + 1 + name.size)
                    var p = 0
                    hello[p++] = Wire.VERSION
                    hello[p++] = Wire.ROLE_APP
                    hello[p++] = android.os.Build.VERSION.SDK_INT.toByte()
                    hello[p++] = name.size.toByte()
                    name.copyInto(hello, p); p += name.size
                    hello[p++] = name.size.toByte()
                    name.copyInto(hello, p)
                    sendSealed(Wire.T_HELLO, hello.copyOf(p + name.size), 0, p + name.size)
                }
                Wire.T_WELCOME -> {
                    val k = key ?: return false
                    val plain = VjcCrypto.decrypt(k, 0, session, f.seq, f.payload) ?: return false
                    return plain.size >= 2 && plain[1] == 1.toByte()
                }
                Wire.T_ERROR -> return false
            }
        }
        return false
    }

    private var lastRx = 0L
    private var lastBeat = 0L
    private var lastPing = 0L
    private var lastStatus = 0L

    /** Service supplies the STATUS payload; sent every 2 s (spec §2/§16 diagnostics). */
    var statusProvider: (() -> ByteArray)? = null

    private fun loopSession() {
        lastRx = System.currentTimeMillis()
        while (connected && !closed.get()) {
            val now = System.currentTimeMillis()
            val timeout = if (direct) 6000 else 9000
            if (now - lastRx > timeout) {
                listener.onLog("timeout aguardando PC (${timeout}ms)")
                listener.onCmd(-1, ByteArray(0)) // signal loss
                return
            }
            val frame = transport.receive(150)
            if (frame != null) {
                lastRx = System.currentTimeMillis()
                handle(frame)
            }
            if (now - lastBeat > 1000) {
                lastBeat = now
                sendSealed(Wire.T_HEARTBEAT, ByteArray(0), 0, 0)
            }
            if (now - lastPing > 1000) {
                lastPing = now
                val bb = Wire.le()
                bb.putLong(System.currentTimeMillis())
                sendSealed(Wire.T_PING, bb.array(), 0, 8)
            }
            if (now - lastStatus > 2000) {
                lastStatus = now
                statusProvider?.invoke()?.let { sendSealed(Wire.T_STATUS, it, 0, it.size) }
            }
        }
    }

    private fun handle(frame: ByteArray) {
        val f = Packet.parse(frame) ?: return
        if (f.type != Wire.T_ERROR) {
            if (recvLastSeq >= 0) {
                val delta = f.seq - recvLastSeq
                if (delta <= 0) return
                if (delta > 1) lostCount += minOf((delta - 1).toLong(), 1024L)
            }
            recvLastSeq = f.seq
            recvCount++
            lossPct = if (recvCount + lostCount == 0L) 0.0 else lostCount * 100.0 / (recvCount + lostCount)
        }
        val k = key ?: return
        val plain = VjcCrypto.decrypt(k, 0, session, f.seq, f.payload) ?: return
        when (f.type) {
            Wire.T_STATE_S -> {
                if (plain.size < Wire.STATE_SIZE) return
                val s = ControllerState()
                val bb = java.nio.ByteBuffer.wrap(plain).order(java.nio.ByteOrder.LITTLE_ENDIAN)
                s.buttons = bb.int
                s.lx = bb.short / 32767f
                s.ly = bb.short / 32767f
                s.rx = bb.short / 32767f
                s.ry = bb.short / 32767f
                s.lt = (bb.get().toInt() and 0xFF) / 255f
                s.rt = (bb.get().toInt() and 0xFF) / 255f
                s.tsMs = System.currentTimeMillis()
                listener.onStateFromPc(s)
            }
            Wire.T_PING -> {
                if (plain.size >= 8) {
                    val t0 = java.nio.ByteBuffer.wrap(plain).order(java.nio.ByteOrder.LITTLE_ENDIAN).long
                    val bb = Wire.le()
                    bb.putLong(t0)
                    sendSealed(Wire.T_PONG, bb.array(), 0, 8)
                }
            }
            Wire.T_PONG -> {
                if (plain.size >= 8) {
                    val t0 = java.nio.ByteBuffer.wrap(plain).order(java.nio.ByteOrder.LITTLE_ENDIAN).long
                    val r = System.currentTimeMillis() - t0
                    rttEwma = if (rttEwma < 0) r.toDouble() else (rttEwma + r) / 2
                    rttMs = rttEwma
                }
            }
            Wire.T_CMD -> {
                if (plain.isNotEmpty()) {
                    val cmd = plain[0].toInt() and 0xFF
                    listener.onCmd(cmd, plain.copyOfRange(1, plain.size))
                }
            }
            Wire.T_WELCOME -> {
                // server may re-welcome on resume; parse peer name
                if (plain.size >= 4) {
                    val nlen = plain[3].toInt() and 0xFF
                    if (plain.size >= 4 + nlen)
                        peerName = String(plain, 4, nlen)
                }
            }
        }
    }

    private fun sendPlain(type: Int, payload: ByteArray) {
        val s = sendSeq[0]++
        transport.send(Packet.build(type, 0, s, payload))
    }

    private fun sendSealed(type: Int, plain: ByteArray, off: Int, len: Int) {
        val k = key ?: return
        val s = sendSeq[0]++
        val payload = if (off == 0 && len == plain.size) plain else plain.copyOfRange(off, off + len)
        val enc = VjcCrypto.encrypt(k, 1, session, s, payload)
        transport.send(Packet.build(type, session, s, enc))
    }

    fun close() {
        connected = false
        if (closed.compareAndSet(false, true)) transport.close()
    }
}

/** Transport abstraction: datagram or length-framed stream. */
interface Transport {
    fun send(packet: ByteArray)
    /** wait up to timeoutMs; returns full packet (already de-framed) or null */
    fun receive(timeoutMs: Int): ByteArray?
    fun close()
}

class TcpTransport(host: String, port: Int) : Transport {
    private val socket = java.net.Socket().apply {
        connect(java.net.InetSocketAddress(host, port), 3500)
        tcpNoDelay = true
    }
    private val out = java.io.BufferedOutputStream(socket.getOutputStream())
    private val input = socket.getInputStream()

    override fun send(packet: ByteArray) {
        try {
            out.write(Framing.withLen(packet))
            out.flush()
        } catch (e: Exception) {
            socket.close()
        }
    }

    override fun receive(timeoutMs: Int): ByteArray? {
        socket.soTimeout = timeoutMs
        return try {
            Framing.readFrame(input)
        } catch (e: java.net.SocketTimeoutException) {
            null
        } catch (e: Exception) {
            throw java.io.IOException("tcp closed")
        }
    }

    override fun close() {
        try { socket.close() } catch (e: Exception) { }
    }
}
