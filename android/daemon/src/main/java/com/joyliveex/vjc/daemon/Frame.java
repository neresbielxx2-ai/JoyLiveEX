package com.joyliveex.vjc.daemon;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;

/** Little-endian packet codec: {u8 ver, u8 type, u32 session, u32 seq, u16 encLen, payload}. */
public final class Frame {
    public byte type;
    public int session;
    public int seq;
    public byte[] payload = new byte[0];

    public static void putU16LE(byte[] b, int at, int v) {
        b[at] = (byte) v;
        b[at + 1] = (byte) (v >>> 8);
    }

    public static int u16LE(byte[] b, int at) {
        return (b[at] & 0xFF) | ((b[at + 1] & 0xFF) << 8);
    }

    public static void putU32LE(byte[] b, int at, int v) {
        b[at] = (byte) v;
        b[at + 1] = (byte) (v >>> 8);
        b[at + 2] = (byte) (v >>> 16);
        b[at + 3] = (byte) (v >>> 24);
    }

    public static int u32LE(byte[] b, int at) {
        return (b[at] & 0xFF) | ((b[at + 1] & 0xFF) << 8)
             | ((b[at + 2] & 0xFF) << 16) | ((b[at + 3] & 0xFF) << 24);
    }

    public static long u64LE(byte[] b, int at) {
        return (u32LE(b, at) & 0xFFFFFFFFL) | ((long) u32LE(b, at + 4) << 32);
    }

    public static byte[] build(int type, int session, int seq, byte[] payload) {
        byte[] b = new byte[Constants.OVERHEAD + payload.length];
        b[0] = Constants.VERSION;
        b[1] = (byte) type;
        putU32LE(b, 2, session);
        putU32LE(b, 6, seq);
        putU16LE(b, 10, payload.length);
        System.arraycopy(payload, 0, b, Constants.OVERHEAD, payload.length);
        return b;
    }

    /** TCP framing: u16 BE length + packet. */
    public static void writeFramed(OutputStream out, byte[] packet) throws IOException {
        byte[] len = new byte[2];
        len[0] = (byte) (packet.length >>> 8);
        len[1] = (byte) packet.length;
        out.write(len);
        out.write(packet);
        out.flush();
    }

    public static byte[] readFramed(InputStream in) throws IOException {
        int a = in.read();
        if (a < 0) return null;
        int b = in.read();
        if (b < 0) return null;
        int n = (a << 8) | b;
        if (n <= 0 || n > 2048) throw new IOException("frame too large: " + n);
        byte[] buf = new byte[n];
        int off = 0;
        while (off < n) {
            int r = in.read(buf, off, n - off);
            if (r < 0) throw new IOException("eof");
            off += r;
        }
        return buf;
    }

    public static Frame parse(byte[] buf) {
        if (buf.length < Constants.OVERHEAD || buf[0] != Constants.VERSION) return null;
        int encLen = u16LE(buf, 10);
        if (encLen < 0 || Constants.OVERHEAD + encLen > buf.length) return null;
        Frame f = new Frame();
        f.type = buf[1];
        f.session = u32LE(buf, 2);
        f.seq = u32LE(buf, 6);
        ByteArrayOutputStream out = new ByteArrayOutputStream(encLen);
        out.write(buf, Constants.OVERHEAD, encLen);
        f.payload = out.toByteArray();
        return f;
    }
}
