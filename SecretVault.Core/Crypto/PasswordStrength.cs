namespace SecretVault.Crypto;

public enum StrengthLevel { VeryWeak, Weak, Fair, Strong, VeryStrong }

public static class PasswordStrength
{
    public static StrengthLevel Evaluate(string password)
    {
        if (string.IsNullOrEmpty(password)) return StrengthLevel.VeryWeak;

        var poolSize = 0;
        if (password.Any(char.IsLower)) poolSize += 26;
        if (password.Any(char.IsUpper)) poolSize += 26;
        if (password.Any(char.IsDigit)) poolSize += 10;
        if (password.Any(c => !char.IsLetterOrDigit(c))) poolSize += 32;

        // Bits of entropy, treating the password as a uniform random string over its character pool.
        var entropyBits = poolSize > 0
            ? password.Length * Math.Log2(poolSize)
            : 0;

        return entropyBits switch
        {
            < 28 => StrengthLevel.VeryWeak,
            < 36 => StrengthLevel.Weak,
            < 60 => StrengthLevel.Fair,
            < 80 => StrengthLevel.Strong,
            _ => StrengthLevel.VeryStrong
        };
    }
}
