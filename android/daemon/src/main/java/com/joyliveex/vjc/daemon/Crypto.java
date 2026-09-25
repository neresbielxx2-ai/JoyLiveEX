package com.joyliveex.vjc.daemon;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;

import javax.crypto.Cipher;
import javax.crypto.Mac;
import javax.crypto.spec.GCMParameterSpec;
import javax.crypto.spec.SecretKeySpec;

/**
 * Session crypto — deterministic manual PBKDF2-HMAC-SHA256 + AES/GCM, identical to
 * VjcCrypto.cs (desktop) and VjcCrypto.kt (companion app). Manual PBKDF2 avoids
 * provider differences on older ART builds.
 */
public final class Crypto {
    private Crypto() {}

    public static byte[] pbkdf2Sha256(String password, byte[] salt, int iterations, int dkLen) {
        try {
            Mac mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(password.getBytes(StandardCharsets.UTF_8), "HmacSHA256"));
            byte[] dk = new byte[dkLen];
            int offset = 0;
            int block = 1;
            byte[] input = new byte[salt.length + 4];
            System.arraycopy(salt, 0, input, 0, salt.length);
            byte[] u = new byte[32];
            byte[] t = new byte[32];
            while (offset < dkLen) {
                int be = block;
                input[input.length - 4] = (byte) (be >>> 24);
                input[input.length - 3] = (byte) (be >>> 16);
                input[input.length - 2] = (byte) (be >>> 8);
                input[input.length - 1] = (byte) be;
                mac.reset();
                u = mac.doFinal(input);
                System.arraycopy(u, 0, t, 0, 32);
                for (int i = 1; i < iterations; i++) {
                    mac.reset();
                    u = mac.doFinal(u);
                    for (int j = 0; j < 32; j++) t[j] ^= u[j];
                }
                int take = Math.min(32, dkLen - offset);
                System.arraycopy(t, 0, dk, offset, take);
                offset += take;
                block++;
            }
            return dk;
        } catch (Exception e) {
            throw new RuntimeException("pbkdf2 failed", e);
        }
    }

    public static String normalizeCode(String code) {
        StringBuilder sb = new StringBuilder();
        for (char c : code.toUpperCase().toCharArray())
            if (Character.isLetterOrDigit(c)) sb.append(c);
        return sb.toString();
    }

    public static byte[] deriveSessionKey(String pairingCode, byte[] clientSalt, byte[] serverSalt) {
        byte[] salt = new byte[16];
        System.arraycopy(clientSalt, 0, salt, 0, 8);
        System.arraycopy(serverSalt, 0, salt, 8, 8);
        return pbkdf2Sha256(normalizeCode(pairingCode), salt, Constants.PBKDF2_ITERATIONS, 32);
    }

    public static int computeSessionId(byte[] clientSalt, byte[] serverSalt) {
        try {
            MessageDigest sha = MessageDigest.getInstance("SHA-256");
            byte[] h = sha.digest("VJC-session|".getBytes(StandardCharsets.US_ASCII));
            sha.reset();
            byte[] ctx = new byte[16];
            System.arraycopy(clientSalt, 0, ctx, 0, 8);
            System.arraycopy(serverSalt, 0, ctx, 8, 8);
            sha.update(h);
            byte[] full = sha.digest(ctx);
            return ((full[0] & 0xFF) << 24) | ((full[1] & 0xFF) << 16) | ((full[2] & 0xFF) << 8) | (full[3] & 0xFF);
        } catch (Exception e) {
            throw new RuntimeException(e);
        }
    }

    static void buildNonce(byte[] nonce, int direction, int sessionId, int seq) {
        nonce[0] = (byte) direction;
        nonce[1] = (byte) sessionId;
        nonce[2] = (byte) (sessionId >>> 8);
        nonce[3] = (byte) (sessionId >>> 16);
        nonce[4] = (byte) (sessionId >>> 24);
        nonce[5] = (byte) seq;
        nonce[6] = (byte) (seq >>> 8);
        nonce[7] = (byte) (seq >>> 16);
        nonce[8] = (byte) (seq >>> 24);
        nonce[9] = 0;
        nonce[10] = 0;
        nonce[11] = 0;
    }

    public static byte[] encrypt(byte[] key, int direction, int sessionId, int seq, byte[] plaintext) {
        try {
            byte[] nonce = new byte[12];
            buildNonce(nonce, direction, sessionId, seq);
            Cipher c = Cipher.getInstance("AES/GCM/NoPadding");
            c.init(Cipher.ENCRYPT_MODE, new SecretKeySpec(key, "AES"), new GCMParameterSpec(128, nonce));
            return c.doFinal(plaintext); // ct||tag
        } catch (Exception e) {
            throw new RuntimeException(e);
        }
    }

    public static byte[] decrypt(byte[] key, int direction, int sessionId, int seq, byte[] sealed) {
        try {
            byte[] nonce = new byte[12];
            buildNonce(nonce, direction, sessionId, seq);
            Cipher c = Cipher.getInstance("AES/GCM/NoPadding");
            c.init(Cipher.DECRYPT_MODE, new SecretKeySpec(key, "AES"), new GCMParameterSpec(128, nonce));
            return c.doFinal(sealed);
        } catch (Exception e) {
            return null;
        }
    }
}
