using System.Security.Cryptography;

namespace SecretVault.Crypto;

public class PasswordOptions
{
    public bool Passphrase { get; set; }

    // Random-character mode.
    public int Length { get; set; } = 20;
    public bool Uppercase { get; set; } = true;
    public bool Lowercase { get; set; } = true;
    public bool Digits { get; set; } = true;
    public bool Symbols { get; set; } = true;

    // Passphrase mode.
    public int Words { get; set; } = 5;
    public string Separator { get; set; } = "-";
    public bool Capitalize { get; set; } = true;
    public bool IncludeNumber { get; set; } = true;
}

public static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%^&*()-_=+[]{}";

    public static string Generate(int length = 20, bool useSymbols = true) =>
        Generate(new PasswordOptions { Length = length, Symbols = useSymbols });

    public static string Generate(PasswordOptions options)
    {
        if (options.Passphrase) return GeneratePassphrase(options);

        var categories = new List<string>();
        if (options.Uppercase) categories.Add(Upper);
        if (options.Lowercase) categories.Add(Lower);
        if (options.Digits) categories.Add(Digits);
        if (options.Symbols) categories.Add(Symbols);
        if (categories.Count == 0) categories.Add(Lower);

        var length = Math.Max(options.Length, Math.Max(4, categories.Count));
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

    private static string GeneratePassphrase(PasswordOptions options)
    {
        var words = new List<string>();
        for (var i = 0; i < Math.Clamp(options.Words, 3, 12); i++)
        {
            var word = Wordlist.Words[RandomNumberGenerator.GetInt32(Wordlist.Words.Count)];
            words.Add(options.Capitalize ? char.ToUpperInvariant(word[0]) + word[1..] : word);
        }

        if (options.IncludeNumber)
        {
            var index = RandomNumberGenerator.GetInt32(words.Count);
            words[index] += RandomNumberGenerator.GetInt32(10, 100).ToString();
        }

        return string.Join(options.Separator, words);
    }

    // Approximate entropy in bits, shown next to the generator so people can compare settings.
    public static double EstimateEntropyBits(PasswordOptions options)
    {
        if (options.Passphrase)
        {
            var bits = Math.Clamp(options.Words, 3, 12) * Math.Log2(Wordlist.Words.Count);
            return options.IncludeNumber ? bits + Math.Log2(90) : bits;
        }

        var pool = (options.Uppercase ? Upper.Length : 0) + (options.Lowercase ? Lower.Length : 0) +
                   (options.Digits ? Digits.Length : 0) + (options.Symbols ? Symbols.Length : 0);
        return Math.Max(options.Length, 4) * Math.Log2(Math.Max(pool, Lower.Length));
    }
}
