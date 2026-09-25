using System.Security.Cryptography;
using System.Text;

namespace VirtualJoyCon.Network.Crypto;

/// <summary>
/// Session crypto (section 17). The pairing code is never used directly; a strong
/// session key is derived with PBKDF2-HMAC-SHA256 over both ephemeral salts, and
/// every packet after the handshake is AES-GCM sealed (auth + integrity + replay
/// window). PBKDF2 is implemented manually here so the result is bit-identical to
/// the Kotlin/Java implementations on the Android side and the daemon.
/// </summary>
public static class VjcCrypto
{
    public static byte[] Pbkdf2Sha256(string password, byte[] salt, int iterations, int dkLen)
    {
        var pw = Encoding.UTF8.GetBytes(password);
        using var hmac = new HMACSHA256(pw);
        var dk = new byte[dkLen];
        int offset = 0;
        uint block = 1;
        Span<byte> u = stackalloc byte[32];
        Span<byte> next = stackalloc byte[32];
        Span<byte> t = stackalloc byte[32];
        Span<byte> input = stackalloc byte[salt.Length + 4];
        salt.AsSpan().CopyTo(input);

        while (offset < dkLen)
        {
            input[^4] = (byte)(block >> 24);
            input[^3] = (byte)(block >> 16);
            input[^2] = (byte)(block >> 8);
            input[^1] = (byte)block;

            hmac.TryComputeHash(input, u, out _);
            u.CopyTo(t);
            for (int i = 1; i < iterations; i++)
            {
                hmac.TryComputeHash(u, next, out _);
                next.CopyTo(u);
                for (int j = 0; j < 32; j++) t[j] ^= u[j];
            }
            int take = Math.Min(32, dkLen - offset);
            t.Slice(0, take).CopyTo(dk.AsSpan(offset));
            offset += take;
            block++;
        }
        return dk;
    }

    public static byte[] DeriveSessionKey(string pairingCode, byte[] clientSalt, byte[] serverSalt)
    {
        var salt = new byte[16];
        Buffer.BlockCopy(clientSalt, 0, salt, 0, 8);
        Buffer.BlockCopy(serverSalt, 0, salt, 8, 8);
        return Pbkdf2Sha256(NormalizeCode(pairingCode), salt, Protocol.Wire.Pbkdf2Iterations, 32);
    }

    public static uint ComputeSessionId(byte[] clientSalt, byte[] serverSalt)
    {
        using var sha = SHA256.Create();
        var ctx = new byte[16];
        Buffer.BlockCopy(clientSalt, 0, ctx, 0, 8);
        Buffer.BlockCopy(serverSalt, 0, ctx, 8, 8);
        var h = sha.ComputeHash(Encoding.ASCII.GetBytes("VJC-session|"));
        var combined = new byte[h.Length + ctx.Length];
        Buffer.BlockCopy(h, 0, combined, 0, h.Length);
        Buffer.BlockCopy(ctx, 0, combined, h.Length, ctx.Length);
        var full = SHA256.HashData(combined);
        return (uint)((full[0] << 24) | (full[1] << 16) | (full[2] << 8) | full[3]);
    }

    /// <summary>Strips non-alphanumerics and uppercases: "8f4k-72lm" -> "8F4K72LM".</summary>
    public static string NormalizeCode(string code)
    {
        var sb = new StringBuilder(8);
        foreach (var c in code.ToUpperInvariant())
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        return sb.ToString();
    }

    public static void BuildNonce(byte[] nonce, byte direction, uint sessionId, uint seq)
    {
        nonce[0] = direction;
        nonce[1] = (byte)sessionId; nonce[2] = (byte)(sessionId >> 8);
        nonce[3] = (byte)(sessionId >> 16); nonce[4] = (byte)(sessionId >> 24);
        nonce[5] = (byte)seq; nonce[6] = (byte)(seq >> 8);
        nonce[7] = (byte)(seq >> 16); nonce[8] = (byte)(seq >> 24);
        nonce[9] = nonce[10] = nonce[11] = 0;
    }

    /// <summary>Returns ct||tag (16B).</summary>
    public static byte[] Encrypt(byte[] key, byte direction, uint sessionId, uint seq, ReadOnlySpan<byte> plaintext)
    {
        var nonce = new byte[12];
        BuildNonce(nonce, direction, sessionId, seq);
        var outBuf = new byte[plaintext.Length + 16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plaintext, outBuf.AsSpan(0, plaintext.Length), outBuf.AsSpan(plaintext.Length, 16));
        return outBuf;
    }

    public static byte[]? Decrypt(byte[] key, byte direction, uint sessionId, uint seq, ReadOnlySpan<byte> sealedBuf)
    {
        if (sealedBuf.Length < 16) return null;
        int ptLen = sealedBuf.Length - 16;
        var nonce = new byte[12];
        BuildNonce(nonce, direction, sessionId, seq);
        var pt = new byte[ptLen];
        try
        {
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, sealedBuf.Slice(0, ptLen), sealedBuf.Slice(ptLen, 16), pt);
            return pt;
        }
        catch (CryptographicException)
        {
            return null; // bad tag => wrong key, corrupted, or replay-mismatched seq
        }
    }

    public static byte[] RandomSalt()
    {
        var b = new byte[8];
        RandomNumberGenerator.Fill(b);
        return b;
    }
}
