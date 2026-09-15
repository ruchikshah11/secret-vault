using System.Security.Cryptography;

namespace SecretVault.Crypto;

public static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{}";

    public static string Generate(int length = 20, bool useSymbols = true)
    {
        if (length < 4)
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be at least 4.");

        var categories = new List<string> { Upper, Lower, Digits };
        if (useSymbols) categories.Add(Symbols);
        var charset = string.Concat(categories);

        var chars = new char[length];

        // Guarantee at least one character from every category.
        for (int i = 0; i < categories.Count; i++)
            chars[i] = categories[i][RandomNumberGenerator.GetInt32(categories[i].Length)];

        for (int i = categories.Count; i < length; i++)
            chars[i] = charset[RandomNumberGenerator.GetInt32(charset.Length)];

        // Fisher-Yates shuffle so the guaranteed characters aren't always first.
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
