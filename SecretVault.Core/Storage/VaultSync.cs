using SecretVault.Models;

namespace SecretVault.Storage;

// Merges two copies of a vault (e.g. the PC's and the phone's) record by record:
// - something added on either side is kept,
// - something changed on both sides keeps the newer version (by ModifiedAt),
// - something deleted forever on either side stays deleted (RemovedIds / RemovedFolders tombstones).
public static class VaultMerge
{
    public sealed record Result(VaultData Data, int Added, int Updated, int Removed)
    {
        public bool ChangedLocal => Added + Updated + Removed > 0;
        public string Summary => !ChangedLocal ? "Already up to date"
            : string.Join(", ", new[] { (Added, "added"), (Updated, "updated"), (Removed, "removed") }
                .Where(p => p.Item1 > 0).Select(p => $"{p.Item1} {p.Item2}"));
    }

    public static Result Merge(VaultData local, VaultData remote)
    {
        var removedIds = new HashSet<string>(local.RemovedIds.Concat(remote.RemovedIds));
        int added = 0, updated = 0, removed = 0;

        List<T> MergeList<T>(List<T> mine, List<T> theirs, Func<T, string> id, Func<T, DateTimeOffset> changed)
        {
            var result = new List<T>();
            var theirsById = theirs.Where(t => id(t).Length > 0).GroupBy(id).ToDictionary(g => g.Key, g => g.First());
            foreach (var item in mine)
            {
                if (removedIds.Contains(id(item))) { removed++; continue; }
                if (theirsById.Remove(id(item), out var other) && changed(other) > changed(item))
                {
                    result.Add(other);
                    updated++;
                }
                else
                {
                    result.Add(item);
                }
            }
            foreach (var other in theirsById.Values.Where(t => !removedIds.Contains(id(t))))
            {
                result.Add(other);
                added++;
            }
            return result;
        }

        static DateTimeOffset EntryChanged(VaultEntry e) => e.DeletedAt is { } d && d > e.ModifiedAt ? d : e.ModifiedAt;

        var removedFolders = new HashSet<string>(local.RemovedFolders.Concat(remote.RemovedFolders).Select(VaultOperations.NormalizeFolder),
            StringComparer.OrdinalIgnoreCase);
        var folders = local.Folders.Concat(remote.Folders)
            .Where(f => !removedFolders.Contains(VaultOperations.NormalizeFolder(f)))
            .DistinctBy(VaultOperations.NormalizeFolder, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var merged = new VaultData
        {
            Entries = MergeList(local.Entries, remote.Entries, e => e.Id, EntryChanged),
            PaymentCards = MergeList(local.PaymentCards, remote.PaymentCards, c => c.Id, c => c.ModifiedAt),
            PersonalInfoRecords = MergeList(local.PersonalInfoRecords, remote.PersonalInfoRecords, p => p.Id, p => p.ModifiedAt),
            Folders = folders,
            RemovedIds = removedIds.ToList(),
            RemovedFolders = local.RemovedFolders.Concat(remote.RemovedFolders).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        };
        return new Result(merged, added, updated, removed);
    }
}

// Keeps a vault in step with a shared copy (a file in OneDrive / Google Drive / a USB stick) that other devices
// also sync with. The shared copy is the normal encrypted vault file, so the cloud only ever sees ciphertext.
public static class VaultSync
{
    public sealed record Outcome(VaultMerge.Result Merge, byte[] SharedCopy);

    // remoteBytes: current contents of the shared copy, or null when it doesn't exist yet.
    // remotePassword: only needed when the shared copy was saved with a different master password
    // (VaultAuthenticationException tells the caller to ask for it).
    // Saves the merged vault locally and returns the bytes to write back to the shared copy.
    public static async Task<Outcome> SyncAsync(FileVaultStore store, VaultData data, string masterPassword,
        byte[]? remoteBytes, string? remotePassword = null)
    {
        VaultMerge.Result result;
        if (remoteBytes is { Length: > 0 })
        {
            var remote = await Task.Run(() => FileVaultStore.LoadFromBytes(remoteBytes, remotePassword ?? masterPassword));
            result = VaultMerge.Merge(data, remote);
            VaultOperations.ReplaceContents(data, result.Data);
        }
        else
        {
            result = new VaultMerge.Result(data, 0, 0, 0);
        }

        await store.SaveAsync(data, masterPassword);
        return new Outcome(result, await File.ReadAllBytesAsync(store.FilePath));
    }
}
