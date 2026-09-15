using System.Security.Cryptography;
using Konscious.Security.Cryptography;

namespace SecretVault.Crypto;

public static class VaultCrypto
{
    public const int SaltSize = 16;
    public const int NonceSize = 12;
    public const int TagSize = 16;
    public const int KeySize = 32;

    public static byte[] GenerateSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    public static byte[] DeriveKey(string password, byte[] salt)
    {
        using var argon2 = new Argon2id(System.Text.Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = Environment.ProcessorCount,
            MemorySize = 65536, // 64 MB
            Iterations = 4
        };
        return argon2.GetBytes(KeySize);
    }

    public static (byte[] nonce, byte[] ciphertext, byte[] tag) Encrypt(byte[] key, byte[] plaintext)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        return (nonce, ciphertext, tag);
    }

    public static byte[] Decrypt(byte[] key, byte[] nonce, byte[] ciphertext, byte[] tag)
    {
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
