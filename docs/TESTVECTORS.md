# Cross-runtime protocol test vectors

These vectors are computed from the reference algorithm (RFC 2898 + RFC 5116,
little-endian framing) with a pure-Python generator. **Every runtime** (C# desktop,
Kotlin companion app, Java daemon) must reproduce them byte-for-byte. They are
asserted in:

- `desktop/tests/VirtualJoyCon.Core.Tests/VectorTests.cs`
- `android/app/src/test/java/com/joyliveex/vjc/ProtocolVectorTest.kt`

## Inputs

| Field | Value |
|---|---|
| pairing code (normalized) | `8F4K72LM` |
| client salt | `03 03 03 03 03 03 03 03` |
| server salt | `07 07 07 07 07 07 07 07` |
| PBKDF2 | HMAC-SHA256, 60 000 iterations, dkLen 32 |

## Expected

| Output | Hex |
|---|---|
| session key | `28d04d2809187c866ee06e662958015a7295a591cf8715115034013bc37d3f69` |
| session id | `20A269FF` |

Sample STATE (payload of a `STATE_S` packet):

```
buttons = A(0x01) | ZL(0x40) | DPAD_UP(0x4000)   → 0x00004041
left  = (0.50, -0.25) → shorts 0x4000, 0xE000
right = (-1.0, 0.25)  → shorts 0x8001, 0x2000
ltrig = 255, rtrig = 64, hat = N(1), flags = 0
ts_lo = 0x12345678
```

| Output | Hex |
|---|---|
| STATE payload (20 B) | `41400000004000e001800020ff40010078563412` |
| full packet (type 0x11, session above, seq 101, **plaintext payload for test**) | `0111ff69a22065000000140041400000004000e001800020ff40010078563412` |

Packet header: `u8 ver=1, u8 type, u32 LE session, u32 LE seq, u16 LE payloadLen`.

Nonce for AES-GCM (12 B): `[dir][session LE u32][seq LE u32][00 00 00]` with
`dir = 0` PC→device, `1` device→PC.

## Regenerating

```python
import hashlib, struct
code = "8F4K72LM"; cs = bytes([3]*8); ss = bytes([7]*8); salt = cs+ss
dk = hashlib.pbkdf2_hmac('sha256', code.encode(), salt, 60000, 32)
sid = struct.unpack(">I", hashlib.sha256(hashlib.sha256(b"VJC-session|").digest()+salt).digest()[:4])[0]
```
