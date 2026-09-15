using System.Security.Cryptography;

namespace SecretVault.Crypto;

public static class TotpGenerator
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateCode(string base32Secret, int digits = 6, int step = 30, DateTimeOffset? at = null)
    {
        var key = Base32Decode(base32Secret);
        long counter = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / step;

        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(counterBytes);

        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(counterBytes);

        int offset = hash[^1] & 0x0F;
        int binaryCode = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        int code = binaryCode % (int)Math.Pow(10, digits);
        return code.ToString().PadLeft(digits, '0');
    }

    public static int SecondsRemaining(int step = 30)
    {
        var unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return step - (int)(unix % step);
    }

    private static byte[] Base32Decode(string input)
    {
        input = input.Trim().TrimEnd('=').ToUpperInvariant();

        var output = new List<byte>();
        int buffer = 0, bitsLeft = 0;

        foreach (var c in input)
        {
            int value = Base32Alphabet.IndexOf(c);
            if (value < 0) continue;

            buffer = (buffer << 5) | value;
            bitsLeft += 5;

            if (bitsLeft >= 8)
            {
                bitsLeft -= 8;
                output.Add((byte)((buffer >> bitsLeft) & 0xFF));
            }
        }

        return output.ToArray();
    }
}
