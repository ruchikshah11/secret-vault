using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecretVault.Crypto;
using SecretVault.Models;

namespace SecretVault.Storage;

// Vault file formats
//
// v1 (legacy, read-only):  "SVLT" 01 | salt | nonce | tag | ciphertext
//     The records are encrypted with Argon2id(master password, salt), parallelism = this machine's CPU count.
//
// v2 (current):            "SVLT" 02 | kdf | master wrap | recovery flag [+ recovery wrap] | data nonce | data tag | ciphertext
//     A random 256-bit vault key encrypts the records. The vault key itself is stored twice, each copy encrypted
//     ("wrapped") with a key derived by Argon2id: once from the master password and, optionally, once from a recovery
//     key. Either secret can unlock the vault; changing the master password only re-wraps the vault key, so an existing
//     recovery key keeps working. The Argon2id settings are stored, so the file opens on any machine.
public class FileVaultStore : IVaultStore
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SVLT");
    private const byte VersionV1 = 1;
    private const byte VersionV2 = 2;

    // Automatic backups: before overwriting the vault, keep a copy at most this often, and keep this many.
    public static readonly TimeSpan BackupInterval = TimeSpan.FromMinutes(30);
    public const int BackupsToKeep = 20;

    private readonly string _path;

    public FileVaultStore(string? path = null)
    {
        _path = path ?? GetDefaultPath();
    }

    public string FilePath => _path;

    public static string GetDefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SecretVault");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "vault.db");
    }

    public string BackupDirectory => Path.Combine(Path.GetDirectoryName(_path)!, "backups");

    public bool Exists() => File.Exists(_path);

    // ---------- file model ----------

    private sealed record Wrap(byte[] Salt, byte[] Nonce, byte[] Tag, byte[] Cipher);

    private sealed class VaultFile
    {
        public byte Version;
        public VaultCrypto.KdfParams Kdf = VaultCrypto.KdfParams.Default;
        public Wrap? Master;          // v2
        public Wrap? Recovery;        // v2, optional
        public byte[] Salt = [];      // v1 only
        public byte[] DataNonce = [];
        public byte[] DataTag = [];
        public byte[] Ciphertext = [];
    }

    private static VaultFile Parse(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        using var reader = new BinaryReader(ms);

        if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
            throw new InvalidDataException("Not a valid vault file.");

        var file = new VaultFile { Version = reader.ReadByte() };
        Wrap ReadWrap() => new(reader.ReadBytes(VaultCrypto.SaltSize), reader.ReadBytes(VaultCrypto.NonceSize),
            reader.ReadBytes(VaultCrypto.TagSize), reader.ReadBytes(VaultCrypto.KeySize));

        switch (file.Version)
        {
            case VersionV1:
                file.Salt = reader.ReadBytes(VaultCrypto.SaltSize);
                break;
            case VersionV2:
                file.Kdf = new VaultCrypto.KdfParams(reader.ReadByte(), reader.ReadInt32(), reader.ReadInt32());
                file.Master = ReadWrap();
                if (reader.ReadByte() == 1) file.Recovery = ReadWrap();
                break;
            default:
                throw new InvalidDataException($"Unsupported vault version {file.Version}. Update SecretVault to open it.");
        }

        file.DataNonce = reader.ReadBytes(VaultCrypto.NonceSize);
        file.DataTag = reader.ReadBytes(VaultCrypto.TagSize);
        file.Ciphertext = reader.ReadBytes((int)(ms.Length - ms.Position));
        return file;
    }

    private async Task<VaultFile?> ReadCurrentAsync() =>
        File.Exists(_path) ? Parse(await File.ReadAllBytesAsync(_path)) : null;

    // Unwraps the vault key with a secret (master password or recovery key); wrong secret => VaultAuthenticationException.
    private static byte[] Unwrap(Wrap wrap, string secret, VaultCrypto.KdfParams kdf)
    {
        var kek = VaultCrypto.DeriveKey(secret, wrap.Salt, kdf);
        try
        {
            return VaultCrypto.Decrypt(kek, wrap.Nonce, wrap.Cipher, wrap.Tag);
        }
        catch (CryptographicException)
        {
            throw new VaultAuthenticationException();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    private static Wrap WrapKey(byte[] vaultKey, string secret, VaultCrypto.KdfParams kdf)
    {
        var salt = VaultCrypto.GenerateSalt();
        var kek = VaultCrypto.DeriveKey(secret, salt, kdf);
        try
        {
            var (nonce, cipher, tag) = VaultCrypto.Encrypt(kek, vaultKey);
            return new Wrap(salt, nonce, tag, cipher);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    private static VaultData DecryptData(VaultFile file, byte[] key)
    {
        byte[] plaintext;
        try
        {
            plaintext = VaultCrypto.Decrypt(key, file.DataNonce, file.Ciphertext, file.DataTag);
        }
        catch (CryptographicException)
        {
            throw new VaultAuthenticationException();
        }
        var data = JsonSerializer.Deserialize<VaultData>(plaintext) ?? new VaultData();
        AssignLegacyIds(data);
        return data;
    }

    // Records saved before Ids existed get one derived from their position and original contents, so two devices
    // holding copies of the same old file give the same record the same Id (and a merge doesn't duplicate it).
    private static void AssignLegacyIds(VaultData data)
    {
        for (var i = 0; i < data.Entries.Count; i++)
        {
            var e = data.Entries[i];
            if (!string.IsNullOrEmpty(e.Id)) continue;
            var source = $"{i}\u0001{e.CreatedAt.UtcTicks}\u0001{e.Name}\u0001{e.Username}";
            e.Id = "legacy-" + Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(source)))[..24].ToLowerInvariant();
        }
    }

    private static void AssignNewIds(VaultData data)
    {
        foreach (var e in data.Entries.Where(e => string.IsNullOrEmpty(e.Id)))
            e.Id = Guid.NewGuid().ToString();
    }

    // Opens a vault file's bytes (e.g. the shared sync copy) with the master password it was saved with.
    public static VaultData LoadFromBytes(byte[] bytes, string masterPassword)
    {
        var file = Parse(bytes);
        if (file.Version == VersionV1)
            return DecryptData(file, VaultCrypto.DeriveKey(masterPassword, file.Salt));

        var vaultKey = Unwrap(file.Master!, masterPassword, file.Kdf);
        try { return DecryptData(file, vaultKey); }
        finally { CryptographicOperations.ZeroMemory(vaultKey); }
    }

    public static bool LooksLikeVault(ReadOnlySpan<byte> header) =>
        header.Length >= Magic.Length + 1 && header[..Magic.Length].SequenceEqual(Magic) && header[Magic.Length] is VersionV1 or VersionV2;

    // ---------- load ----------

    public async Task<VaultData> LoadAsync(string masterPassword) => await LoadFromAsync(_path, masterPassword);

    // Opens any vault file (e.g. a backup) with the master password it was saved with.
    public static async Task<VaultData> LoadFromAsync(string path, string masterPassword) =>
        LoadFromBytes(await File.ReadAllBytesAsync(path), masterPassword);

    // ---------- save ----------

    public async Task SaveAsync(VaultData data, string masterPassword)
    {
        var current = await ReadCurrentAsync();
        if (current is { Version: VersionV2 })
        {
            // Same vault key, master and recovery wraps unchanged; only the records are re-encrypted.
            var vaultKey = Unwrap(current.Master!, masterPassword, current.Kdf);
            await WriteAsync(data, vaultKey, current.Kdf, current.Master!, current.Recovery, forceBackup: false);
            return;
        }

        // New vault, or upgrading a v1 file: fresh vault key, wrapped with the master password.
        await WriteNewKeyAsync(data, masterPassword, recovery: null, forceBackup: current is not null);
    }

    public async Task ChangeMasterPasswordAsync(VaultData data, string currentPassword, string newPassword)
    {
        var current = await ReadCurrentAsync();
        if (current is not { Version: VersionV2 })
        {
            await WriteNewKeyAsync(data, newPassword, recovery: null, forceBackup: true);
            return;
        }

        var vaultKey = Unwrap(current.Master!, currentPassword, current.Kdf);
        var master = WrapKey(vaultKey, newPassword, current.Kdf);
        await WriteAsync(data, vaultKey, current.Kdf, master, current.Recovery, forceBackup: true);
    }

    private async Task WriteNewKeyAsync(VaultData data, string masterPassword, Wrap? recovery, bool forceBackup)
    {
        var kdf = VaultCrypto.KdfParams.Default;
        var vaultKey = RandomNumberGenerator.GetBytes(VaultCrypto.KeySize);
        var master = WrapKey(vaultKey, masterPassword, kdf);
        await WriteAsync(data, vaultKey, kdf, master, recovery, forceBackup);
    }

    private async Task WriteAsync(VaultData data, byte[] vaultKey, VaultCrypto.KdfParams kdf, Wrap master, Wrap? recovery, bool forceBackup)
    {
        try
        {
            AssignNewIds(data);
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(data);
            var (nonce, ciphertext, tag) = VaultCrypto.Encrypt(vaultKey, plaintext);
            CryptographicOperations.ZeroMemory(plaintext);

            using var ms = new MemoryStream();
            using (var writer = new BinaryWriter(ms))
            {
                void WriteWrap(Wrap w) { writer.Write(w.Salt); writer.Write(w.Nonce); writer.Write(w.Tag); writer.Write(w.Cipher); }

                writer.Write(Magic);
                writer.Write(VersionV2);
                writer.Write((byte)kdf.Parallelism);
                writer.Write(kdf.MemoryKb);
                writer.Write(kdf.Iterations);
                WriteWrap(master);
                writer.Write((byte)(recovery is null ? 0 : 1));
                if (recovery is not null) WriteWrap(recovery);
                writer.Write(nonce);
                writer.Write(tag);
                writer.Write(ciphertext);
            }

            BackupExistingVault(forceBackup);

            var tmpPath = _path + ".tmp";
            await File.WriteAllBytesAsync(tmpPath, ms.ToArray());
            File.Move(tmpPath, _path, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(vaultKey);
        }
    }

    // ---------- recovery key ----------

    public async Task<bool> HasRecoveryKeyAsync() => (await ReadCurrentAsync())?.Recovery is not null;

    // Creates (or replaces) the recovery key and returns it for the user to save. The old key stops working.
    public async Task<string> CreateRecoveryKeyAsync(VaultData data, string masterPassword)
    {
        var current = await ReadCurrentAsync();
        if (current is not { Version: VersionV2 })
        {
            await SaveAsync(data, masterPassword); // upgrade to v2 first
            current = await ReadCurrentAsync();
        }

        var recoveryKey = RecoveryKey.Generate();
        var vaultKey = Unwrap(current!.Master!, masterPassword, current.Kdf);
        var recovery = WrapKey(vaultKey, RecoveryKey.Normalize(recoveryKey), current.Kdf);
        await WriteAsync(data, vaultKey, current.Kdf, current.Master!, recovery, forceBackup: true);
        return recoveryKey;
    }

    public async Task RemoveRecoveryKeyAsync(VaultData data, string masterPassword)
    {
        var current = await ReadCurrentAsync();
        if (current is not { Version: VersionV2, Recovery: not null }) return;
        var vaultKey = Unwrap(current.Master!, masterPassword, current.Kdf);
        await WriteAsync(data, vaultKey, current.Kdf, current.Master!, recovery: null, forceBackup: true);
    }

    // "Forgot password": opens the vault with the recovery key and sets a new master password.
    // Throws VaultAuthenticationException for a wrong key, InvalidOperationException if no recovery key was set up.
    public async Task<VaultData> ResetMasterPasswordAsync(string recoveryKey, string newPassword)
    {
        var current = await ReadCurrentAsync() ?? throw new FileNotFoundException("No vault found.", _path);
        if (current.Recovery is null)
            throw new InvalidOperationException("This vault has no recovery key.");

        var vaultKey = Unwrap(current.Recovery, RecoveryKey.Normalize(recoveryKey), current.Kdf);
        var data = DecryptData(current, vaultKey);
        var master = WrapKey(vaultKey, newPassword, current.Kdf);
        await WriteAsync(data, vaultKey, current.Kdf, master, current.Recovery, forceBackup: true);
        return data;
    }

    // Removes the plaintext dump older debug builds wrote next to the vault.
    public void DeleteLegacyPlaintextDump()
    {
        var legacy = Path.Combine(Path.GetDirectoryName(_path)!, "vault.readable.json");
        if (File.Exists(legacy)) File.Delete(legacy);
    }

    // ---------- backups ----------

    public IReadOnlyList<FileInfo> ListBackups()
    {
        if (!Directory.Exists(BackupDirectory)) return Array.Empty<FileInfo>();
        return new DirectoryInfo(BackupDirectory).GetFiles("vault-*.db")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();
    }

    // Copies the current (still encrypted) vault file elsewhere, e.g. for a manual backup.
    public void ExportEncryptedCopy(string destinationPath) => File.Copy(_path, destinationPath, overwrite: true);

    // Forces a backup of the current vault file right now (before risky operations like restore or re-keying).
    public void CreateBackupNow() => BackupExistingVault(force: true);

    private void BackupExistingVault(bool force = false)
    {
        if (!File.Exists(_path)) return;

        try
        {
            var backups = ListBackups();
            if (!force && backups.Count > 0 && DateTime.UtcNow - backups[0].LastWriteTimeUtc < BackupInterval) return;

            Directory.CreateDirectory(BackupDirectory);
            var name = $"vault-{DateTime.Now:yyyyMMdd-HHmmss-fff}.db";
            File.Copy(_path, Path.Combine(BackupDirectory, name), overwrite: true);

            foreach (var old in ListBackups().Skip(BackupsToKeep))
                old.Delete();
        }
        catch (IOException)
        {
            // A failed backup must never block saving the vault itself.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

// Recovery keys: 30 random characters from a 32-letter alphabet without look-alikes (no I, O, 0, 1) = 150 bits,
// shown in groups of five. Input is normalised so spacing, dashes and case don't matter.
public static class RecoveryKey
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Length = 30;

    public static string Generate()
    {
        var chars = new char[Length];
        for (var i = 0; i < Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return string.Join("-", Enumerable.Range(0, Length / 5).Select(g => new string(chars, g * 5, 5)));
    }

    public static string Normalize(string input) =>
        new string(input.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    public static bool LooksValid(string input) =>
        Normalize(input) is { Length: Length } k && k.All(Alphabet.Contains);
}
