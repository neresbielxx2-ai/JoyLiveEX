# VJC wire protocol (v1)

Optimized for input events — no images, no metadata bloat. One STATE packet = 32 bytes.
All integers little-endian. Mirrored in three places; they MUST stay in sync:

| Runtime | File |
|---|---|
| C# (PC) | `desktop/src/VirtualJoyCon.Network/Protocol/` |
| Kotlin (APK) | `android/app/.../network/{Wire,Crypto}.kt` |
| Java (daemon) | `android/daemon/.../{Constants,Crypto,Frame}.java` |

Golden vectors in `TESTVECTORS.md` are asserted by unit tests on both platforms.

## Packet

```
u8  ver   = 1
u8  type
u32 session   (0 before handshake)
u32 seq       (shared counter per direction; also the AES-GCM nonce component)
u16 encLen
encLen bytes  (AES-GCM ct||16B tag; raw payload for plaintext types)
```

Framing: **UDP** = one packet per datagram. **TCP** (adb reverse / LAN fallback) =
`u16 BE length` prefix.

## Types

| type | name | dir | payload |
|---|---|---|---|
|0x01| SALT_C | peer→PC (plain) | `u8[8]` client salt |
|0x02| SALT_S | PC→peer (plain) | `u8[8]` server salt |
|0x03| ERROR | both (plain) | `u8 reason, u8 len, msg` |
|0x04| HELLO | peer→PC | `u8 ver, u8 role, u8 sdk, u8 nameLen, name, u8 modelLen, model` |
|0x05| WELCOME | PC→peer | `u8 ver, u8 ok, u8 transport, u16 heartbeatMs` |
|0x10| STATE_D | device→PC | state payload |
|0x11| STATE_S | PC→device | state payload |
|0x12| PING | both | `u64 unixMs t0` (echoed by PONG → RTT) |
|0x13| PONG | both | `u64 t0` |
|0x14| HEARTBEAT | both | — |
|0x20| CMD | PC→device | `u8 cmd, args…` |
|0x21| STATUS | device→PC | `u8 svc, u8 mode, u16 rtt, u8 loss, u16 mLen, model, u16 nLen, name, u8 sdk, u16 ver` |
|0x22| LOG | device→PC | `u8 level, u16 len, msg` |

### STATE payload (20 B)

```
u32 buttons          (bit layout = ButtonFlags; d-pad bits also encoded in hat)
i16 leftX,leftY,rightX,rightY   (-32767..32767  ≙ -1.0..+1.0)
u8  leftTrigger, rightTrigger   (0..255)
u8  hat              (0 none, 1 N … 8 NW)
u8  flags            (b0 = device control active)
u32 timestampMsLo    (diagnostics only)
```

### CMDs

`1 VIBRATE {u16 ms}` · `2 START_DAEMON` · `3 STOP_DAEMON` · `4 SET_MODE {u8}` ·
`5 CONFIG {json}` · `6 SCREENSHOT` · `7 RELEASE_ALL` (anti-stuck on demand).

## Handshake & crypto

```
key       = PBKDF2-HMAC-SHA256(normalize(code), saltC||saltS, 60000, 32)
sessionId = BE32(SHA256( SHA256("VJC-session|") || saltC || saltS ))
nonce     = [dir:u8][sessionId:u32LE][seq:u32LE][00 00 00]      (12 B)
```

- The pairing code (8 chars, alphabet without I/O/0/1) only seeds the KDF — the
  proof of knowledge is a valid AES-GCM tag on HELLO/WELCOME.
- Receive: drop `seq <= lastSeq`; count `seq > lastSeq+1` as loss.
- Auth failures counted per 10 s window; >8 ⇒ silent drop.
- Handshake re-derives fresh salts on every (re)connection.

## Transport modes

| mode | dial | notes |
|---|---|---|
| USB | app/daemon → `127.0.0.1:8721` TCP over `adb reverse` | zero-config, ordered, still ~0.1-1 ms |
| LAN | app → `pcIP:8721` UDP | discovery: ASCII `VJCDprobe!` broadcast → `VJCDrepl!1{u16 tcpPort}{u8 codeLen}{code}` |
| Internet | both → relay UDP room | envelope `u32 'VJC1'`, `u8 op`, `u8[8] room` |

Relay ops: `JOIN(1)` register, `PEER_INFO(2)` {4B ip, 2B port BE}, `WRAP(3)` forward,
`JOIN_ACK(4)`. Peers then probe each other's public endpoint; the first raw VJC packet
accepted by the other side switches the link to **direct P2P** (relay bypassed).
