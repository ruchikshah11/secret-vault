using System.Text;
using System.Text.Json;
using SecretVault.Crypto;
using SecretVault.Models;

namespace SecretVault.Storage;

public class FileVaultStore : IVaultStore
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SVLT");
    private const byte Version = 1;

    private readonly string _path;

    public FileVaultStore(string? path = null)
    {
        _path = path ?? GetDefaultPath();
    }

    public static string GetDefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SecretVault");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "vault.db");
    }

    public bool Exists() => File.Exists(_path);

    public async Task<VaultData> LoadAsync(string masterPassword)
    {
        var bytes = await File.ReadAllBytesAsync(_path);

        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);

        var magic = reader.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException("Not a valid vault file.");

        reader.ReadByte(); // version, reserved for future format changes

        var salt = reader.ReadBytes(VaultCrypto.SaltSize);
        var nonce = reader.ReadBytes(VaultCrypto.NonceSize);
        var tag = reader.ReadBytes(VaultCrypto.TagSize);
        var ciphertext = reader.ReadBytes((int)(ms.Length - ms.Position));

        var key = VaultCrypto.DeriveKey(masterPassword, salt);

        byte[] plaintext;
        try
        {
            plaintext = VaultCrypto.Decrypt(key, nonce, ciphertext, tag);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            throw new VaultAuthenticationException();
        }

        return JsonSerializer.Deserialize<VaultData>(plaintext) ?? new VaultData();
    }

    public async Task SaveAsync(VaultData data, string masterPassword)
    {
        var salt = VaultCrypto.GenerateSalt();
        var key = VaultCrypto.DeriveKey(masterPassword, salt);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(data);
        var (nonce, ciphertext, tag) = VaultCrypto.Encrypt(key, plaintext);

        using var ms = new MemoryStream();
        using (var writer = new BinaryWriter(ms))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(salt);
            writer.Write(nonce);
            writer.Write(tag);
            writer.Write(ciphertext);
        }

        var tmpPath = _path + ".tmp";
        await File.WriteAllBytesAsync(tmpPath, ms.ToArray());
        File.Move(tmpPath, _path, overwrite: true);

        await WriteDebugReadableCopyAsync(data, masterPassword, salt, nonce, tag); // TEMP: comment out to stop writing plaintext alongside vault.db
    }

    // TEMP DEBUG ONLY: writes an unencrypted, human-readable copy of everything stored in vault.db,
    // including the master password used to encrypt it. This defeats the whole point of encryption
    // while active — remove the call above when done inspecting.
    private async Task WriteDebugReadableCopyAsync(VaultData data, string masterPassword, byte[] salt, byte[] nonce, byte[] tag)
    {
        var readablePath = Path.Combine(Path.GetDirectoryName(_path)!, "vault.readable.json");
        var debugDump = new
        {
            MasterPassword = masterPassword,
            Salt = Convert.ToBase64String(salt),
            Nonce = Convert.ToBase64String(nonce),
            Tag = Convert.ToBase64String(tag),
            Vault = data
        };
        var json = JsonSerializer.Serialize(debugDump, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(readablePath, json);
    }
}
