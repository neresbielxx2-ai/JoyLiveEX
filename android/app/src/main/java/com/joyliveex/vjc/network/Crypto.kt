package com.joyliveex.vjc.network

import java.security.MessageDigest
import javax.crypto.Cipher
import javax.crypto.Mac
import javax.crypto.spec.GCMParameterSpec
import javax.crypto.spec.SecretKeySpec

/**
 * PBKDF2-HMAC-SHA256 (manual, RFC 8018) + AES-GCM — byte-identical to VjcCrypto.cs
 * and the daemon's Crypto.java.
 */
object VjcCrypto {

    fun pbkdf2(password: String, salt: ByteArray, iterations: Int, dkLen: Int): ByteArray {
        val mac = Mac.getInstance("HmacSHA256")
        mac.init(SecretKeySpec(password.toByteArray(Charsets.UTF_8), "HmacSHA256"))
        val dk = ByteArray(dkLen)
        var offset = 0
        var block = 1
        val input = ByteArray(salt.size + 4)
        salt.copyInto(input)
        while (offset < dkLen) {
            input[input.size - 4] = (block ushr 24).toByte()
            input[input.size - 3] = (block ushr 16).toByte()
            input[input.size - 2] = (block ushr 8).toByte()
            input[input.size - 1] = block.toByte()
            var u = mac.doFinal(input)
            val t = u.copyOf()
            for (i in 1 until iterations) {
                u = mac.doFinal(u)
                for (j in 0 until 32) t[j] = (t[j].toInt() xor u[j].toInt()).toByte()
            }
            val take = minOf(32, dkLen - offset)
            t.copyInto(dk, offset, 0, take)
            offset += take
            block++
        }
        return dk
    }

    fun normalizeCode(code: String): String =
        code.uppercase().filter { it.isLetterOrDigit() }

    fun deriveSessionKey(code: String, clientSalt: ByteArray, serverSalt: ByteArray): ByteArray {
        val salt = ByteArray(16)
        clientSalt.copyInto(salt, 0, 0, 8)
        serverSalt.copyInto(salt, 8, 0, 8)
        return pbkdf2(normalizeCode(code), salt, Wire.PBKDF2_ITER, 32)
    }

    fun computeSessionId(clientSalt: ByteArray, serverSalt: ByteArray): Int {
        val sha = MessageDigest.getInstance("SHA-256")
        val h = sha.digest("VJC-session|".toByteArray(Charsets.US_ASCII))
        val ctx = ByteArray(16)
        clientSalt.copyInto(ctx, 0, 0, 8)
        serverSalt.copyInto(ctx, 8, 0, 8)
        val full = sha.digest(h + ctx)
        return ((full[0].toInt() and 0xFF) shl 24) or
            ((full[1].toInt() and 0xFF) shl 16) or
            ((full[2].toInt() and 0xFF) shl 8) or
            (full[3].toInt() and 0xFF)
    }

    private fun nonce(dir: Int, session: Int, seq: Int): ByteArray {
        val n = ByteArray(12)
        n[0] = dir.toByte()
        n[1] = session.toByte()
        n[2] = (session ushr 8).toByte()
        n[3] = (session ushr 16).toByte()
        n[4] = (session ushr 24).toByte()
        n[5] = seq.toByte()
        n[6] = (seq ushr 8).toByte()
        n[7] = (seq ushr 16).toByte()
        n[8] = (seq ushr 24).toByte()
        return n
    }

    fun encrypt(key: ByteArray, dir: Int, session: Int, seq: Int, plain: ByteArray): ByteArray {
        val c = Cipher.getInstance("AES/GCM/NoPadding")
        c.init(Cipher.ENCRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(128, nonce(dir, session, seq)))
        return c.doFinal(plain)
    }

    fun decrypt(key: ByteArray, dir: Int, session: Int, seq: Int, sealed: ByteArray): ByteArray? {
        return try {
            val c = Cipher.getInstance("AES/GCM/NoPadding")
            c.init(Cipher.DECRYPT_MODE, SecretKeySpec(key, "AES"), GCMParameterSpec(128, nonce(dir, session, seq)))
            c.doFinal(sealed)
        } catch (e: Exception) {
            null
        }
    }
}

object PairingCode {
    fun toWire(display: String): String = VjcCrypto.normalizeCode(display)
}
