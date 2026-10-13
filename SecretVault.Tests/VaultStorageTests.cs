using System.Text;
using System.Text.Json;
using SecretVault.Crypto;
using SecretVault.Models;
using SecretVault.Storage;

namespace SecretVault.Tests;

// Vault file format, upgrades, master password changes, recovery keys and backups.
// Every test works on its own temporary folder — never the real vault.
public sealed class VaultStorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "svtests-" + Guid.NewGuid().ToString("N"));
    private string VaultPath => Path.Combine(_dir, "vault.db");

    public VaultStorageTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static VaultData SampleData() => new() { Entries = { new VaultEntry { Name = "GitHub", Username = "ruchik", Secret = "s3cret" } } };

    // Writes a file exactly the way the original (v1) code did.
    private void WriteV1(VaultData data, string password)
    {
        var salt = VaultCrypto.GenerateSalt();
        var (nonce, cipher, tag) = VaultCrypto.Encrypt(VaultCrypto.DeriveKey(password, salt), JsonSerializer.SerializeToUtf8Bytes(data));
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write(Encoding.ASCII.GetBytes("SVLT")); w.Write((byte)1); w.Write(salt); w.Write(nonce); w.Write(tag); w.Write(cipher);
        }
        File.WriteAllBytes(VaultPath, ms.ToArray());
    }

    private byte FileVersion() => File.ReadAllBytes(VaultPath)[4];

    [Fact]
    public async Task V1_vault_still_opens_and_rejects_wrong_password()
    {
        WriteV1(SampleData(), "old-master");
        var store = new FileVaultStore(VaultPath);
        Assert.Equal("GitHub", (await store.LoadAsync("old-master")).Entries.Single().Name);
        await Assert.ThrowsAsync<VaultAuthenticationException>(() => store.LoadAsync("wrong"));
    }

    [Fact]
    public async Task Saving_upgrades_v1_to_v2_and_keeps_data()
    {
        WriteV1(SampleData(), "pw");
        var store = new FileVaultStore(VaultPath);
        await store.SaveAsync(await store.LoadAsync("pw"), "pw");
        Assert.Equal(2, FileVersion());
        Assert.Equal("s3cret", (await store.LoadAsync("pw")).Entries.Single().Secret);
    }

    [Fact]
    public async Task V2_stores_fixed_argon2_settings_so_it_opens_on_any_pc()
    {
        var store = new FileVaultStore(VaultPath);
        await store.SaveAsync(SampleData(), "pw");
        var bytes = File.ReadAllBytes(VaultPath);
        Assert.Equal(2, bytes[4]);
        Assert.Equal(4, bytes[5]);                              // lanes — not the machine's CPU count
        Assert.Equal(65536, BitConverter.ToInt32(bytes, 6));    // 64 MB
        Assert.Equal(4, BitConverter.ToInt32(bytes, 10));       // passes
    }

    [Fact]
    public async Task V2_rejects_wrong_password_and_tampering()
    {
        var store = new FileVaultStore(VaultPath);
        await store.SaveAsync(SampleData(), "pw");
        await Assert.ThrowsAsync<VaultAuthenticationException>(() => store.LoadAsync("wrong"));

        var bytes = File.ReadAllBytes(VaultPath);
        bytes[^1] ^= 0xFF; // flip a bit in the encrypted records
        File.WriteAllBytes(VaultPath, bytes);
        await Assert.ThrowsAsync<VaultAuthenticationException>(() => store.LoadAsync("pw"));
    }

    [Fact]
    public async Task Non_vault_file_is_reported_clearly()
    {
        File.WriteAllText(VaultPath, "not a vault");
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileVaultStore(VaultPath).LoadAsync("pw"));
    }

    [Fact]
    public async Task Changing_master_password_keeps_recovery_key_working()
    {
        var store = new FileVaultStore(VaultPath);
        var data = SampleData();
        await store.SaveAsync(data, "old");
        var key = await store.CreateRecoveryKeyAsync(data, "old");

        await store.ChangeMasterPasswordAsync(data, "old", "new");

        await Assert.ThrowsAsync<VaultAuthenticationException>(() => store.LoadAsync("old"));
        Assert.Single((await store.LoadAsync("new")).Entries);
        Assert.True(await store.HasRecoveryKeyAsync());
        Assert.Single((await store.ResetMasterPasswordAsync(key, "after-reset")).Entries);
    }

    [Fact]
    public async Task Recovery_key_resets_password_and_ignores_spacing_and_case()
    {
        var store = new FileVaultStore(VaultPath);
        var data = SampleData();
        await store.SaveAsync(data, "forgotten");
        var key = await store.CreateRecoveryKeyAsync(data, "forgotten");

        var messy = key.ToLowerInvariant().Replace("-", "  ");
        var opened = await store.ResetMasterPasswordAsync(messy, "brand-new");

        Assert.Equal("s3cret", opened.Entries.Single().Secret);
        Assert.Single((await store.LoadAsync("brand-new")).Entries);
        await Assert.ThrowsAsync<VaultAuthenticationException>(() => store.LoadAsync("forgotten"));
    }

    [Fact]
    public async Task Wrong_recovery_key_is_rejected_and_password_unchanged()
    {
        var store = new FileVaultStore(VaultPath);
        var data = SampleData();
        await store.SaveAsync(data, "pw");
        await store.CreateRecoveryKeyAsync(data, "pw");

        await Assert.ThrowsAsync<VaultAuthenticationException>(() => store.ResetMasterPasswordAsync("AAAAA-AAAAA-AAAAA-AAAAA-AAAAA-AAAAA", "x"));
        Assert.Single((await store.LoadAsync("pw")).Entries);
    }

    [Fact]
    public async Task Replacing_recovery_key_invalidates_the_old_one()
    {
        var store = new FileVaultStore(VaultPath);
        var data = SampleData();
        await store.SaveAsync(data, "pw");
        var first = await store.CreateRecoveryKeyAsync(data, "pw");
        var second = await store.CreateRecoveryKeyAsync(data, "pw");

        await Assert.ThrowsAsync<VaultAuthenticationException>(() => store.ResetMasterPasswordAsync(first, "x"));
        Assert.Single((await store.ResetMasterPasswordAsync(second, "y")).Entries);
    }

    [Fact]
    public async Task Saves_keep_recovery_key_and_removing_it_disables_reset()
    {
        var store = new FileVaultStore(VaultPath);
        var data = SampleData();
        await store.SaveAsync(data, "pw");
        var key = await store.CreateRecoveryKeyAsync(data, "pw");

        data.Entries.Add(new VaultEntry { Name = "Second" });
        await store.SaveAsync(data, "pw");
        Assert.True(await store.HasRecoveryKeyAsync());
        Assert.Equal(2, (await store.LoadAsync("pw")).Entries.Count);

        await store.RemoveRecoveryKeyAsync(data, "pw");
        Assert.False(await store.HasRecoveryKeyAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ResetMasterPasswordAsync(key, "x"));
    }

    [Fact]
    public async Task Backups_are_throttled_forced_when_asked_and_old_ones_still_open()
    {
        WriteV1(SampleData(), "pw");
        var store = new FileVaultStore(VaultPath);
        var data = await store.LoadAsync("pw");

        await store.SaveAsync(data, "pw");   // upgrade => forced backup of the v1 file
        await store.SaveAsync(data, "pw");   // within the interval => no new backup
        await store.SaveAsync(data, "pw");
        var afterSaves = store.ListBackups().Count;
        store.CreateBackupNow();

        Assert.Equal(1, afterSaves);
        Assert.Equal(2, store.ListBackups().Count);
        var oldest = store.ListBackups().Last();
        Assert.Equal(1, File.ReadAllBytes(oldest.FullName)[4]);
        Assert.Single((await FileVaultStore.LoadFromAsync(oldest.FullName, "pw")).Entries);
    }

    [Fact]
    public async Task Legacy_plaintext_dump_is_deleted_and_never_written()
    {
        var dump = Path.Combine(_dir, "vault.readable.json");
        File.WriteAllText(dump, "{\"MasterPassword\":\"leaked\"}");
        var store = new FileVaultStore(VaultPath);
        store.DeleteLegacyPlaintextDump();
        await store.SaveAsync(SampleData(), "pw");
        Assert.False(File.Exists(dump));
    }

    [Fact]
    public async Task New_and_optional_fields_round_trip_through_encryption()
    {
        var store = new FileVaultStore(VaultPath);
        var entry = new VaultEntry
        {
            Name = "Prod VM", Url = "https://portal.azure.com", Favorite = true, Type = RecordTypes.Server,
            Tags = { "prod", "azure" }, TotpSecret = "JBSWY3DPEHPK3PXP",
            CustomFields = { new CustomField { Label = "Hostname", Value = "10.0.0.4" } },
            PasswordHistory = { new PasswordHistoryItem { Password = "old", ChangedAt = DateTimeOffset.UtcNow } }
        };
        await store.SaveAsync(new VaultData { Entries = { entry }, PaymentCards = { new PaymentCard { CardNumber = "4111", Brand = "Visa" } }, Folders = { @"Projects\New" } }, "pw");

        var back = await store.LoadAsync("pw");
        var e = back.Entries.Single();
        Assert.Equal(("https://portal.azure.com", true, RecordTypes.Server), (e.Url, e.Favorite, e.Type));
        Assert.Equal(new[] { "prod", "azure" }, e.Tags);
        Assert.Equal("old", e.PasswordHistory.Single().Password);
        Assert.Equal("Visa", back.PaymentCards.Single().Brand);
        Assert.Equal(@"Projects\New", back.Folders.Single());
    }
}
