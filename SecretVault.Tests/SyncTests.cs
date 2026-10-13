using System.Text.Json;
using SecretVault.Crypto;
using SecretVault.Models;
using SecretVault.Storage;

namespace SecretVault.Tests;

// PC <-> phone sync: record Ids, tombstones and the per-record merge. Temporary files only.
public sealed class SyncTests : IDisposable
{
    private const string Password = "Sync-Test-Password-1";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "svsync-" + Guid.NewGuid().ToString("N"));

    public SyncTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string PathOf(string name) => Path.Combine(_dir, name);

    private static VaultEntry Entry(string name, DateTimeOffset? modified = null) => new()
    {
        Name = name, Username = name.ToLowerInvariant(), Secret = "pw-" + name,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-10), ModifiedAt = modified ?? DateTimeOffset.UtcNow.AddDays(-5)
    };

    // Two devices start from the same saved vault (a copy of one file).
    private async Task<(FileVaultStore Pc, VaultData PcData, FileVaultStore Phone, VaultData PhoneData)> TwoDevicesAsync(params VaultEntry[] entries)
    {
        var pc = new FileVaultStore(PathOf("pc.db"));
        var data = new VaultData();
        data.Entries.AddRange(entries);
        await pc.SaveAsync(data, Password);
        File.Copy(pc.FilePath, PathOf("phone.db"));
        var phone = new FileVaultStore(PathOf("phone.db"));
        return (pc, await pc.LoadAsync(Password), phone, await phone.LoadAsync(Password));
    }

    [Fact]
    public async Task Saving_gives_every_record_a_stable_id()
    {
        var (pc, pcData, _, phoneData) = await TwoDevicesAsync(Entry("GitHub"), Entry("Google"));
        Assert.All(pcData.Entries, e => Assert.False(string.IsNullOrEmpty(e.Id)));
        Assert.Equal(pcData.Entries.Select(e => e.Id), phoneData.Entries.Select(e => e.Id));
        Assert.Equal(pcData.Entries.Select(e => e.Id), (await pc.LoadAsync(Password)).Entries.Select(e => e.Id));
    }

    [Fact]
    public void Old_vaults_without_ids_get_the_same_ids_on_every_device()
    {
        // A vault written before Ids existed: the JSON has no "Id" property at all.
        var legacy = new VaultData { Entries = { Entry("GitHub"), Entry("GitHub") } };
        var json = JsonSerializer.Serialize(legacy).Replace("\"Id\":\"\",", "");
        Assert.DoesNotContain("\"Id\"", json.Split("PaymentCards")[0]);
        var bytes = WriteV1(json);

        var onPc = FileVaultStore.LoadFromBytes(bytes, Password);
        var onPhone = FileVaultStore.LoadFromBytes(bytes, Password);
        Assert.Equal(onPc.Entries.Select(e => e.Id), onPhone.Entries.Select(e => e.Id));
        Assert.Equal(2, onPc.Entries.Select(e => e.Id).Distinct().Count()); // identical records still get distinct Ids
        Assert.Equal(2, VaultMerge.Merge(onPc, onPhone).Data.Entries.Count); // merging the two copies doesn't duplicate
    }

    private static byte[] WriteV1(string json)
    {
        var salt = VaultCrypto.GenerateSalt();
        var (nonce, cipher, tag) = VaultCrypto.Encrypt(VaultCrypto.DeriveKey(Password, salt), System.Text.Encoding.UTF8.GetBytes(json));
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms))
        {
            w.Write("SVLT"u8.ToArray()); w.Write((byte)1); w.Write(salt); w.Write(nonce); w.Write(tag); w.Write(cipher);
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task Records_added_on_either_device_show_up_on_both()
    {
        var (pc, pcData, phone, phoneData) = await TwoDevicesAsync(Entry("GitHub"));
        pcData.Entries.Add(Entry("Added on PC", DateTimeOffset.UtcNow));
        phoneData.Entries.Add(Entry("Added on phone", DateTimeOffset.UtcNow));
        await pc.SaveAsync(pcData, Password);
        await phone.SaveAsync(phoneData, Password);

        // PC syncs first (shared copy doesn't exist yet), then the phone, then the PC again.
        var shared = (await VaultSync.SyncAsync(pc, pcData, Password, null)).SharedCopy;
        var phoneSync = await VaultSync.SyncAsync(phone, phoneData, Password, shared);
        Assert.Equal(1, phoneSync.Merge.Added);
        var pcSync = await VaultSync.SyncAsync(pc, pcData, Password, phoneSync.SharedCopy);
        Assert.Equal(1, pcSync.Merge.Added);

        var expected = new[] { "Added on PC", "Added on phone", "GitHub" };
        Assert.Equal(expected, pcData.Entries.Select(e => e.Name).Order());
        Assert.Equal(expected, phoneData.Entries.Select(e => e.Name).Order());
        Assert.Equal(expected, (await phone.LoadAsync(Password)).Entries.Select(e => e.Name).Order()); // saved locally too
    }

    [Fact]
    public async Task Editing_a_record_keeps_its_id_so_the_edit_syncs_instead_of_duplicating()
    {
        var (_, pcData, _, phoneData) = await TwoDevicesAsync(Entry("GitHub"));
        var original = phoneData.Entries[0];
        var edited = VaultOperations.Stamp(new VaultEntry { Name = "GitHub", Username = "jane", Secret = "new-password" }, original);
        phoneData.Entries[0] = edited;

        Assert.Equal(original.Id, edited.Id);
        Assert.Equal("pw-GitHub", edited.PasswordHistory.Single().Password);
        var merged = VaultMerge.Merge(pcData, phoneData).Data;
        Assert.Equal("new-password", merged.Entries.Single().Secret);
    }

    [Fact]
    public async Task The_newer_edit_wins_and_older_edits_do_not_overwrite_it()
    {
        var (_, pcData, _, phoneData) = await TwoDevicesAsync(Entry("GitHub"), Entry("Google"));
        pcData.Entries[0].Secret = "changed-on-pc";
        pcData.Entries[0].ModifiedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        phoneData.Entries[0].Secret = "changed-on-phone-later";
        phoneData.Entries[0].ModifiedAt = DateTimeOffset.UtcNow;
        phoneData.Entries[1].Notes = "older phone edit";
        phoneData.Entries[1].ModifiedAt = DateTimeOffset.UtcNow.AddDays(-9);

        var merged = VaultMerge.Merge(pcData, phoneData);
        Assert.Equal("changed-on-phone-later", merged.Data.Entries.Single(e => e.Name == "GitHub").Secret);
        Assert.Equal("", merged.Data.Entries.Single(e => e.Name == "Google").Notes);
        Assert.Equal(1, merged.Updated);
    }

    [Fact]
    public async Task Deleting_forever_on_one_device_is_not_undone_by_the_other()
    {
        var (_, pcData, _, phoneData) = await TwoDevicesAsync(Entry("GitHub"), Entry("Old account"));
        VaultOperations.DeleteForever(phoneData, phoneData.Entries.Single(e => e.Name == "Old account"));

        var onPc = VaultMerge.Merge(pcData, phoneData);
        Assert.DoesNotContain(onPc.Data.Entries, e => e.Name == "Old account");
        Assert.Equal(1, onPc.Removed);
        var onPhone = VaultMerge.Merge(phoneData, pcData);
        Assert.DoesNotContain(onPhone.Data.Entries, e => e.Name == "Old account");
    }

    [Fact]
    public async Task Moving_to_deleted_items_and_restoring_sync_as_changes()
    {
        var (_, pcData, _, phoneData) = await TwoDevicesAsync(Entry("GitHub"));
        VaultOperations.MoveToDeletedItems(phoneData.Entries[0]);
        var merged = VaultMerge.Merge(pcData, phoneData).Data;
        Assert.NotNull(merged.Entries.Single().DeletedAt);

        await Task.Delay(5);
        VaultOperations.Restore(merged.Entries.Single());
        Assert.Null(VaultMerge.Merge(phoneData, merged).Data.Entries.Single().DeletedAt);
    }

    [Fact]
    public void Folders_merge_and_deleted_or_renamed_folders_stay_gone()
    {
        var pc = new VaultData { Folders = { "Work", "Old" } };
        var phone = new VaultData { Folders = { "Work", "Old", "Travel" } };
        VaultOperations.DeleteFolder(pc, "Old");
        var entry = Entry("VPN");
        entry.Folder = "Work";
        pc.Entries.Add(entry);
        VaultOperations.RenameFolder(pc, "Work", "Office");

        var merged = VaultMerge.Merge(pc, phone).Data;
        Assert.Equal(new[] { "Office", "Travel" }, VaultOperations.FolderTree(merged));
        Assert.Equal("Office", merged.Entries.Single().Folder);
    }

    [Fact]
    public async Task Cards_and_identities_merge_by_id()
    {
        var (_, pcData, _, phoneData) = await TwoDevicesAsync();
        VaultOperations.SaveCard(phoneData, new PaymentCard { CardholderName = "Jane", CardNumber = "4111111111111111" }, null);
        var card = new PaymentCard { CardholderName = "Old card", CardNumber = "5500000000005678" };
        VaultOperations.SaveCard(pcData, card, null);
        var merged = VaultMerge.Merge(pcData, phoneData).Data;
        Assert.Equal(2, merged.PaymentCards.Count);

        VaultOperations.DeleteCard(merged, merged.PaymentCards.Single(c => c.CardholderName == "Old card"));
        Assert.Single(VaultMerge.Merge(pcData, merged).Data.PaymentCards);
    }

    [Fact]
    public async Task Shared_copy_with_another_master_password_asks_for_it()
    {
        var (pc, pcData, phone, phoneData) = await TwoDevicesAsync(Entry("GitHub"));
        await phone.ChangeMasterPasswordAsync(phoneData, Password, "Phone-Password-2");
        var shared = await File.ReadAllBytesAsync(phone.FilePath);

        await Assert.ThrowsAsync<VaultAuthenticationException>(() => VaultSync.SyncAsync(pc, pcData, Password, shared));
        var outcome = await VaultSync.SyncAsync(pc, pcData, Password, shared, "Phone-Password-2");
        Assert.False(outcome.Merge.ChangedLocal);
        Assert.Equal("Already up to date", outcome.Merge.Summary);
        // The new shared copy is the PC's file: it opens with the PC's password.
        Assert.Single(FileVaultStore.LoadFromBytes(outcome.SharedCopy, Password).Entries);
    }

    [Fact]
    public void Import_plan_skips_duplicates_and_reports_them()
    {
        var data = new VaultData { Entries = { new VaultEntry { Name = "GitHub", Username = "jane", Secret = "x" } } };
        var import = CsvTransfer.Import("name,url,username,password\nGitHub,,jane,x\nGoogle,,jane,y\nGoogle,,jane,y\n");
        var plan = VaultOperations.PlanImport(data, import);
        Assert.Equal(new[] { "Google" }, plan.Fresh.Select(e => e.Name));
        Assert.Equal(2, plan.Duplicates);
        Assert.Contains("1 new record", plan.Summary);
    }

    [Fact]
    public void Security_report_matches_the_desktop_formula()
    {
        var data = new VaultData
        {
            Entries =
            {
                new VaultEntry { Name = "a", Secret = "password1" },
                new VaultEntry { Name = "b", Secret = "Xq9#vL2!pZ7@wK4$" },
                new VaultEntry { Name = "c", Secret = "Xq9#vL2!pZ7@wK4$", TotpSecret = "JBSWY3DPEHPK3PXP" },
                new VaultEntry { Name = "deleted", Secret = "123", DeletedAt = DateTimeOffset.UtcNow }
            }
        };
        var report = SecurityReport.Create(data, "Correct-Horse-Battery-Staple-9!");
        Assert.Equal(3, report.Entries.Count);
        Assert.Single(report.Weak);
        Assert.Equal(2, report.Reused.Count);
        // 70 * (1 - (0.6*1/3 + 0.4*2/3)) + 15 (strong master) + 15 (2FA) = 67.33 -> 67
        Assert.Equal(67, report.Score);
        Assert.Equal("Fair", report.ScoreLabel);
    }
}
