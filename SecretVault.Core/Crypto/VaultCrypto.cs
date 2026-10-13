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

    // Argon2id settings. The output depends on every one of them, so they are stored in the vault file (v2)
    // and must be reproduced exactly to open it — including parallelism, which must not follow the CPU count.
    public sealed record KdfParams(int Parallelism, int MemoryKb, int Iterations)
    {
        public static readonly KdfParams Default = new(4, 65536, 4); // 64 MB, 4 passes, 4 lanes
    }

    // Original (v1) behaviour: parallelism followed this machine's processor count. Only used to open v1 files.
    public static byte[] DeriveKey(string password, byte[] salt) =>
        DeriveKey(password, salt, new KdfParams(Environment.ProcessorCount, 65536, 4));

    public static byte[] DeriveKey(string password, byte[] salt, KdfParams kdf)
    {
        using var argon2 = new Argon2id(System.Text.Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = kdf.Parallelism,
            MemorySize = kdf.MemoryKb,
            Iterations = kdf.Iterations
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
