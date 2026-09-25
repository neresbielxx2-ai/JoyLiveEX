package com.joyliveex.vjc.daemon;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.net.Socket;
import java.nio.charset.StandardCharsets;
import java.util.Random;

/**
 * VJC input daemon — launched by the PC through:
 *   adb shell CLASSPATH=/data/local/tmp/vjc-daemon.jar app_process / com.joyliveex.vjc.daemon.Main <tcpPort> <pairingCode>
 * It connects back to the PC over the adb-reverse channel, authenticates with the
 * pairing code (PBKDF2 + AES-GCM, identical framing to the desktop app), receives
 * STATE packets and injects a virtual gamepad system-wide with shell privileges.
 *
 * stdout/stderr are inherited by the PC (adb shell pipes), so every line here shows
 * up in the EXE diagnostics panel.
 */
public final class Main {

    private static final long TIMEOUT_MS = 3000;

    public static void main(String[] argv) throws Exception {
        if (argv.length < 2) {
            System.out.println("usage: Main <tcpPort> <pairingCode>");
            System.exit(2);
        }
        int port = Integer.parseInt(argv[0]);
        String code = argv[1];
        System.out.println("[vjc-daemon] starting, dialing 127.0.0.1:" + port);

        Injector injector = new Injector();
        if (!injector.init()) {
            System.out.println("[vjc-daemon] FATAL: injector init failed: " + injector.getError());
            System.out.println("[vjc-daemon] hint: this daemon must run as the adb shell user (app_process), not as an installed app");
            System.exit(3);
        }
        System.out.println("[vjc-daemon] injector ready (SOURCE_GAMEPAD, shell privileges)");

        int failures = 0;
        while (true) {
            try {
                runOnce(port, code, injector);
                failures = 0;
            } catch (Exception e) {
                System.out.println("[vjc-daemon] session ended: " + e);
                failures++;
            }
            if (failures > 12) {
                System.out.println("[vjc-daemon] giving up after repeated failures");
                System.exit(4);
            }
            Thread.sleep(2000);
        }
    }

    private static void runOnce(int port, String code, Injector injector) throws Exception {
        try (Socket socket = new Socket()) {
            socket.connect(new InetSocketAddress("127.0.0.1", port), 4000);
            socket.setTcpNoDelay(true);
            InputStream in = socket.getInputStream();
            OutputStream out = socket.getOutputStream();

            // ================= handshake (client side, mirrors VjcLink.cs) =================
            int[] seq = {0};
            byte[] clientSalt = new byte[8];
            new Random().nextBytes(clientSalt);
            sendPlain(out, Constants.TYPE_SALT_C, clientSalt, seq);

            byte[] buf = Frame.readFramed(in);
            Frame f = buf == null ? null : Frame.parse(buf);
            if (f == null || f.type != Constants.TYPE_SALT_S || f.payload.length < 8)
                throw new IOException("bad salt reply");
            byte[] serverSalt = new byte[8];
            System.arraycopy(f.payload, 0, serverSalt, 0, 8);

            byte[] key = Crypto.deriveSessionKey(code, clientSalt, serverSalt);
            int session = Crypto.computeSessionId(clientSalt, serverSalt);

            String model = readBuildModel();
            byte[] nameB = "vjc-daemon".getBytes(StandardCharsets.UTF_8);
            byte[] modelB = model.getBytes(StandardCharsets.UTF_8);
            byte[] hello = new byte[4 + nameB.length + 1 + modelB.length];
            int p = 0;
            hello[p++] = Constants.VERSION;
            hello[p++] = Constants.ROLE_DAEMON;
            hello[p++] = (byte) sdkInt();
            hello[p++] = (byte) nameB.length;
            System.arraycopy(nameB, 0, hello, p, nameB.length);
            p += nameB.length;
            hello[p++] = (byte) modelB.length;
            System.arraycopy(modelB, 0, hello, p, modelB.length);
            sendSealed(out, key, session, Constants.TYPE_HELLO, hello, seq);

            boolean welcomed = false;
            long deadline = System.currentTimeMillis() + 6000;
            while (!welcomed && System.currentTimeMillis() < deadline) {
                buf = Frame.readFramed(in);
                if (buf == null) throw new IOException("closed during handshake");
                f = Frame.parse(buf);
                if (f == null) continue;
                if (f.type == Constants.TYPE_ERROR)
                    throw new IOException("rejected by PC (pairing code?)");
                if (f.type == Constants.TYPE_WELCOME) {
                    byte[] plain = Crypto.decrypt(key, 0, session, f.seq, f.payload);
                    welcomed = plain != null && plain.length >= 2 && plain[1] == 1;
                }
            }
            if (!welcomed) throw new IOException("no WELCOME (wrong pairing code?)");
            System.out.println("[vjc-daemon] PAIRED with PC (session=" + Integer.toHexString(session) + ")");

            // ================= session loop =================
            long lastRx = System.currentTimeMillis();
            long lastBeat = 0, lastStatus = 0;
            int lastSeq = -1;
            long lost = 0, recv = 0;
            int rttMs = 0;
            long rttEwma = -1;
            boolean active = true;

            while (active) {
                long now = System.currentTimeMillis();
                if (now - lastRx > TIMEOUT_MS) {
                    System.out.println("[vjc-daemon] PC silent >3s — releasing all buttons, ending session");
                    injector.releaseAll(new Injector.State());
                    throw new IOException("link timeout");
                }

                if (in.available() > 0) {
                    buf = Frame.readFramed(in);
                    if (buf == null) break;
                    f = Frame.parse(buf);
                    if (f == null) continue;
                    lastRx = System.currentTimeMillis();
                    recv++;
                    if (lastSeq >= 0 && f.seq > lastSeq + 1) lost += Math.min(f.seq - lastSeq - 1, 1024);
                    lastSeq = f.seq;

                    byte[] plain = Crypto.decrypt(key, 0, session, f.seq, f.payload);
                    if (plain == null) continue; // bad tag: ignore (integrity wins over availability)
                    switch (f.type) {
                        case Constants.TYPE_STATE_S: {
                            if (plain.length < Constants.STATE_SIZE) break;
                            Injector.State st = new Injector.State();
                            st.buttons = Frame.u32LE(plain, 0);
                            st.lx = clampShort(Frame.u16LE(plain, 4));
                            st.ly = clampShort(Frame.u16LE(plain, 6));
                            st.rx = clampShort(Frame.u16LE(plain, 8));
                            st.ry = clampShort(Frame.u16LE(plain, 10));
                            st.lt = (plain[12] & 0xFF) / 255f;
                            st.rt = (plain[13] & 0xFF) / 255f;
                            injector.apply(st);
                            break;
                        }
                        case Constants.TYPE_CMD: {
                            if (plain.length == 0) break;
                            byte cmd = plain[0];
                            if (cmd == Constants.CMD_VIBRATE && plain.length >= 3) {
                                injector.vibrate(Frame.u16LE(plain, 1));
                            } else if (cmd == Constants.CMD_RELEASE_ALL) {
                                injector.releaseAll(new Injector.State());
                            } else if (cmd == Constants.CMD_STOP_DAEMON) {
                                active = false;
                            }
                            break;
                        }
                        case Constants.TYPE_PING: {
                            byte[] pong = new byte[8];
                            long t0 = plain.length >= 8 ? Frame.u64LE(plain, 0) : 0;
                            putU64LE(pong, 0, t0);
                            sendSealed(out, key, session, Constants.TYPE_PONG, pong, seq);
                            break;
                        }
                        case Constants.TYPE_PONG: {
                            if (plain.length >= 8) {
                                long t0 = Frame.u64LE(plain, 0);
                                int r = (int) (System.currentTimeMillis() - t0);
                                rttEwma = rttEwma < 0 ? r : (rttEwma + r) / 2;
                                rttMs = (int) rttEwma;
                            }
                            break;
                        }
                        default:
                            break;
                    }
                } else {
                    Thread.sleep(1);
                }

                now = System.currentTimeMillis();
                if (now - lastBeat > 1000) {
                    lastBeat = now;
                    sendSealed(out, key, session, Constants.TYPE_HEARTBEAT, new byte[0], seq);
                }
                if (now - lastStatus > 2000) {
                    lastStatus = now;
                    sendSealed(out, key, session, Constants.TYPE_STATUS, statusPayload(model, rttMs, lost, recv), seq);
                }
            }
        }
    }

    // helpers ------------------------------------------------------------

    private static float clampShort(int v) {
        float f = (short) v / 32767f;
        return f > 1f ? 1f : (f < -1f ? -1f : f);
    }

    private static void sendPlain(OutputStream out, int type, byte[] payload, int[] seq) throws IOException {
        int s = seq[0]++;
        Frame.writeFramed(out, Frame.build(type, 0, s, payload));
    }

    private static void sendSealed(OutputStream out, byte[] key, int session, int type, byte[] plain, int[] seq) throws IOException {
        int s = seq[0]++;
        byte[] enc = Crypto.encrypt(key, 1, session, s, plain); // direction 1 = peer -> PC
        Frame.writeFramed(out, Frame.build(type, session, s, enc));
    }

    private static void putU64LE(byte[] b, int at, long v) {
        for (int i = 0; i < 8; i++) b[at + i] = (byte) (v >>> (8 * i));
    }

    private static byte[] statusPayload(String model, int rttMs, long lost, long recv) {
        try {
            byte[] m = model.getBytes(StandardCharsets.UTF_8);
            byte[] n = "shell-daemon".getBytes(StandardCharsets.UTF_8);
            byte[] b = new byte[2 + 2 + 1 + 2 + m.length + 2 + n.length + 1 + 2];
            int p = 0;
            b[p++] = 2; // service running
            b[p++] = 2; // input mode: ADB daemon (global gamepad)
            Frame.putU16LE(b, p, rttMs); p += 2;
            long total = lost + recv;
            b[p++] = (byte) Math.min(255, total == 0 ? 0 : lost * 100 / total);
            Frame.putU16LE(b, p, m.length); p += 2;
            System.arraycopy(m, 0, b, p, m.length); p += m.length;
            Frame.putU16LE(b, p, n.length); p += 2;
            System.arraycopy(n, 0, b, p, n.length); p += n.length;
            b[p++] = (byte) sdkInt();
            Frame.putU16LE(b, p, 0x0100); // daemon "app" version 1.0
            return b;
        } catch (Exception e) {
            return new byte[0];
        }
    }

    private static int sdkInt() {
        try {
            return Integer.parseInt((String) Class.forName("android.os.Build$VERSION")
                    .getField("SDK_INT").get(null));
        } catch (Throwable t) {
            return 0;
        }
    }

    private static String readBuildModel() {
        try {
            Object m = Class.forName("android.os.Build").getField("MODEL").get(null);
            return m == null ? "Android" : String.valueOf(m);
        } catch (Throwable t) {
            return "Android";
        }
    }
}
