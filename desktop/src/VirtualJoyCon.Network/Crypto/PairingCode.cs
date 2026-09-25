using System.Security.Cryptography;
using System.Text;

namespace VirtualJoyCon.Network.Crypto;

/// <summary>
/// Session pairing codes (section 9): 8 chars from a Crockford-like alphabet
/// (no I, O, U, 0, 1) so codes are unambiguous when read aloud or typed.
/// Displayed as XXXX-XXXX. TTL enforced by the link manager.
/// </summary>
public static class PairingCode
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTVWXYZ23456789"; // 29 chars
    public const int Length = 8;

    public static string Generate()
    {
        Span<byte> rnd = stackalloc byte[Length];
        RandomNumberGenerator.Fill(rnd);
        var sb = new StringBuilder(Length + 1);
        for (int i = 0; i < Length; i++)
        {
            sb.Append(Alphabet[rnd[i] % Alphabet.Length]);
            if (i == 3) sb.Append('-');
        }
        return sb.ToString();
    }

    public static string ToWireCode(string display) => VjcCrypto.NormalizeCode(display);
    public static string FromWireCode(string wire) =>
        wire.Length == 8 ? $"{wire[..4]}-{wire[4..]}" : wire;
}
