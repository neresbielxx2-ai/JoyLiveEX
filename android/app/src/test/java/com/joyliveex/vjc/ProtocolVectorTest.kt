package com.joyliveex.vjc

import com.joyliveex.vjc.controller.Buttons
import com.joyliveex.vjc.controller.StickCurve
import com.joyliveex.vjc.network.Framing
import com.joyliveex.vjc.network.Packet
import com.joyliveex.vjc.network.VjcCrypto
import com.joyliveex.vjc.network.Wire
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test
import java.nio.ByteBuffer
import java.nio.ByteOrder

/**
 * Same vectors as docs/TESTVECTORS.md / the desktop test project: PBKDF2, session
 * id, STATE payload layout, packet framing. If this fails on Android while the
 * C# side passes, the runtimes diverged — fix the mirror, never the vectors.
 */
class ProtocolVectorTest {

    private fun hex(b: ByteArray) = b.joinToString("") { "%02x".format(it) }

    @Test
    fun pbkdf2MatchesReference() {
        val cs = ByteArray(8) { 3 }
        val ss = ByteArray(8) { 7 }
        val key = VjcCrypto.deriveSessionKey("8F4K-72LM", cs, ss)
        assertEquals(
            "28d04d2809187c866ee06e662958015a7295a591cf8715115034013bc37d3f69",
            hex(key),
        )
    }

    @Test
    fun sessionIdMatchesReference() {
        val cs = ByteArray(8) { 3 }
        val ss = ByteArray(8) { 7 }
        val sid = VjcCrypto.computeSessionId(cs, ss)
        assertEquals(0x20A269FF.toInt(), sid)
    }

    @Test
    fun statePayloadGoldenBytes() {
        val bb = ByteBuffer.allocate(Wire.STATE_SIZE).order(ByteOrder.LITTLE_ENDIAN)
        bb.putInt(0x00004041)                 // A | ZL | DPAD_UP
        bb.putShort((0.5f * 32767).toShort())
        bb.putShort((-0.25f * 32767).toInt().toShort())
        bb.putShort((-1.0f * 32767).toShort())
        bb.putShort((0.25f * 32767).toShort())
        bb.put(255.toByte()); bb.put(64.toByte()); bb.put(1); bb.put(0)
        bb.putInt(0x12345678)
        assertEquals("41400000004000e001800020ff40010078563412", hex(bb.array()))

        val pkt = Packet.build(0x11, 0x20A269FF.toInt(), 101, bb.array())
        assertEquals(
            "0111" + "ff69a220" + "65000000" + "1400" + "41400000004000e001800020ff40010078563412",
            hex(pkt),
        )
        val parsed = Packet.parse(pkt)!!
        assertEquals(0x11, parsed.type)
        assertEquals(101, parsed.seq)
    }

    @Test
    fun tcpFramingLengthIsBigEndian() {
        val pkt = byteArrayOf(1, 2, 3)
        val framed = Framing.withLen(pkt)
        assertArrayEquals(byteArrayOf(0, 3, 1, 2, 3), framed)
    }

    @Test
    fun aesGcmRoundtripAndTamper() {
        val cs = ByteArray(8) { 3 }
        val ss = ByteArray(8) { 7 }
        val key = VjcCrypto.deriveSessionKey("8F4K-72LM", cs, ss)
        val sid = VjcCrypto.computeSessionId(cs, ss)
        val enc = VjcCrypto.encrypt(key, 1, sid, 9, "oi".toByteArray())
        assertArrayEquals("oi".toByteArray(), VjcCrypto.decrypt(key, 1, sid, 9, enc))
        assertNull(VjcCrypto.decrypt(key, 1, sid, 10, enc))
    }

    @Test
    fun hatEncoding() {
        assertEquals(Buttons.HAT_NE, Buttons.hatOf(Buttons.DPAD_UP or Buttons.DPAD_RIGHT))
        assertEquals(Buttons.HAT_NONE, Buttons.hatOf(0))
    }

    @Test
    fun stickCurveDeadzone() {
        val c = StickCurve(deadzone = 0.2f)
        val out = FloatArray(2)
        c.process(0.1f, 0.1f, out)
        assertEquals(0f, out[0], 1e-6f)
        c.process(1f, 0f, out)
        assertTrue(out[0] > 0.7f)
        assertTrue(out[0] <= 1.0f)
    }
}
