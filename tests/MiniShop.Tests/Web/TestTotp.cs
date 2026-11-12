using System.Security.Cryptography;

namespace MiniShop.Tests.Web;

/// <summary>Kód z aplikace v telefonu: TOTP podle RFC 6238, počítá si ho test sám – bez zadních vrátek v aplikaci.</summary>
public static class TestTotp
{
    /// <summary>TOTP podle RFC 6238 (HMAC-SHA1, 30 s, 6 číslic) – totéž, co počítá aplikace v telefonu.</summary>
    public static string Compute(string base32Key)
    {
        var key = Base32Decode(base32Key);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var message = new byte[8];
        for (var index = 7; index >= 0; index--)
        {
            message[index] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        var hash = HMACSHA1.HashData(key, message);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string text)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        var buffer = 0;
        var bits = 0;
        foreach (var character in text.TrimEnd('='))
        {
            buffer = (buffer << 5) | alphabet.IndexOf(character);
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }

        return [.. bytes];
    }
}
