using SecretVault.Crypto;

namespace SecretVault.Models;

// Vault edits shared by the desktop and mobile apps, so both behave exactly the same:
// folders, moving, deleting/restoring, and merging an import.
public static class VaultOperations
{
    public const string DefaultFolder = "General";
    private static readonly char[] FolderSeparators = { '\\', '/' };

    public static IEnumerable<VaultEntry> Live(VaultData data) => data.Entries.Where(e => e.DeletedAt is null);
    public static IEnumerable<VaultEntry> Deleted(VaultData data) => data.Entries.Where(e => e.DeletedAt is not null);

    // ---------- folders ----------

    // "Work / Servers\" -> "Work\Servers"
    public static string NormalizeFolder(string folder) =>
        string.Join("\\", folder.Split(FolderSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    // "Infrastructure\Servers" -> "Servers"
    public static string LeafFolderName(string folder)
    {
        var parts = folder.Split(FolderSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 ? parts[^1] : folder;
    }

    public static string JoinFolder(string? parent, string name) => parent is null ? name.Trim() : parent + "\\" + name.Trim();

    public static bool IsInFolder(VaultEntry entry, string path) => IsSameOrInside(NormalizeFolder(entry.Folder), path);

    private static bool IsSameOrInside(string folder, string path) =>
        folder.Equals(path, StringComparison.OrdinalIgnoreCase) || folder.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase);

    // Folders used by records plus folders created explicitly (which may be empty).
    public static IEnumerable<string> AllFolders(VaultData data) => Live(data).Select(e => e.Folder).Concat(data.Folders);

    // Every distinct folder path including parents ("A\B" also yields "A"), sorted.
    public static List<string> FolderTree(VaultData data)
    {
        var paths = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in AllFolders(data).Select(NormalizeFolder).Where(f => f.Length > 0))
        {
            var parts = folder.Split('\\');
            for (var i = 1; i <= parts.Length; i++) paths.Add(string.Join("\\", parts[..i]));
        }
        return paths.ToList();
    }

    public static bool FolderExists(VaultData data, string path) =>
        AllFolders(data).Select(NormalizeFolder).Any(f => IsSameOrInside(f, path));

    // Null when the name is fine, otherwise the reason it isn't.
    public static string? CheckFolderName(string name) =>
        string.IsNullOrWhiteSpace(name) ? "Enter a folder name."
        : name.IndexOfAny(FolderSeparators) >= 0 ? "A folder name can't contain \\ or /." : null;

    public static string? CheckNewFolder(VaultData data, string? parent, string name) =>
        CheckFolderName(name) ?? (FolderExists(data, JoinFolder(parent, name)) ? "A folder with that name already exists." : null);

    public static string CreateFolder(VaultData data, string? parent, string name)
    {
        var path = JoinFolder(parent, name);
        data.Folders.Add(path);
        data.RemovedFolders.RemoveAll(f => NormalizeFolder(f).Equals(NormalizeFolder(path), StringComparison.OrdinalIgnoreCase));
        return path;
    }

    // Renames a folder; its records (including Deleted Items) and subfolders move with it. Returns the new path.
    public static string RenameFolder(VaultData data, string path, string newName, IList<string>? alsoRebase = null)
    {
        var cut = path.LastIndexOf('\\');
        var newPath = JoinFolder(cut < 0 ? null : path[..cut], newName);
        string Rebase(string folder)
        {
            var f = NormalizeFolder(folder);
            return f.Equals(path, StringComparison.OrdinalIgnoreCase) ? newPath
                : f.StartsWith(path + "\\", StringComparison.OrdinalIgnoreCase) ? newPath + f[path.Length..] : folder;
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in data.Entries)
        {
            var moved = Rebase(entry.Folder);
            if (moved == entry.Folder) continue;
            entry.Folder = moved;
            entry.ModifiedAt = now; // the move must win when this copy is merged with another device's
        }
        var renamedAway = data.Folders.Where(f => Rebase(f) != f).ToList();
        data.Folders = data.Folders.Select(Rebase).ToList();
        data.RemovedFolders.AddRange(renamedAway);
        data.RemovedFolders.Add(path);
        data.RemovedFolders.RemoveAll(f => NormalizeFolder(f).Equals(newPath, StringComparison.OrdinalIgnoreCase));
        if (alsoRebase is not null)
            for (var i = 0; i < alsoRebase.Count; i++) alsoRebase[i] = Rebase(alsoRebase[i]);
        return newPath;
    }

    public static bool CanDeleteFolder(VaultData data, string path) => !Live(data).Any(e => IsInFolder(e, path));

    // Only empty folders can be deleted. Returns false (and changes nothing) otherwise.
    public static bool DeleteFolder(VaultData data, string path)
    {
        if (!CanDeleteFolder(data, path)) return false;
        data.RemovedFolders.AddRange(data.Folders.Where(f => IsSameOrInside(NormalizeFolder(f), path)));
        data.RemovedFolders.Add(path);
        data.Folders.RemoveAll(f => IsSameOrInside(NormalizeFolder(f), path));
        return true;
    }

    // ---------- records ----------

    // Returns false when the record is already in that folder.
    public static bool MoveEntry(VaultEntry entry, string folder)
    {
        if (string.Equals(NormalizeFolder(entry.Folder), NormalizeFolder(folder), StringComparison.OrdinalIgnoreCase)) return false;
        entry.Folder = folder;
        entry.ModifiedAt = DateTimeOffset.UtcNow;
        return true;
    }

    // Every change bumps ModifiedAt: when two copies are merged, the newer version of a record wins.
    public static void MoveToDeletedItems(VaultEntry entry) => entry.DeletedAt = entry.ModifiedAt = DateTimeOffset.UtcNow;

    public static void Restore(VaultEntry entry)
    {
        entry.DeletedAt = null;
        entry.ModifiedAt = DateTimeOffset.UtcNow;
    }

    public static void ToggleFavorite(VaultEntry entry)
    {
        entry.Favorite = !entry.Favorite;
        entry.ModifiedAt = DateTimeOffset.UtcNow;
    }

    public static void DeleteForever(VaultData data, VaultEntry entry)
    {
        data.Entries.Remove(entry);
        if (!string.IsNullOrEmpty(entry.Id)) data.RemovedIds.Add(entry.Id);
    }

    public static void EmptyDeletedItems(VaultData data)
    {
        foreach (var entry in Deleted(data).ToList()) DeleteForever(data, entry);
    }

    // ---------- cards & personal info ----------

    public static PaymentCard StampCard(PaymentCard edited, PaymentCard? original)
    {
        var now = DateTimeOffset.UtcNow;
        edited.Id = original?.Id ?? (string.IsNullOrEmpty(edited.Id) ? Guid.NewGuid().ToString() : edited.Id);
        edited.CreatedAt = original?.CreatedAt ?? now;
        edited.ModifiedAt = now;
        return edited;
    }

    public static void SaveCard(VaultData data, PaymentCard edited, PaymentCard? original)
    {
        StampCard(edited, original);
        var index = original is null ? -1 : data.PaymentCards.IndexOf(original);
        if (index >= 0) data.PaymentCards[index] = edited;
        else data.PaymentCards.Add(edited);
    }

    public static void DeleteCard(VaultData data, PaymentCard card)
    {
        data.PaymentCards.Remove(card);
        data.RemovedIds.Add(card.Id);
    }

    public static void DeletePersonalInfo(VaultData data, PersonalInfo info)
    {
        data.PersonalInfoRecords.Remove(info);
        data.RemovedIds.Add(info.Id);
    }

    // Builds the saved record from an edit, keeping history and creation date of the original.
    public static VaultEntry Stamp(VaultEntry edited, VaultEntry? original)
    {
        var now = DateTimeOffset.UtcNow;
        if (original is not null) edited.Id = original.Id; // same record: keeps its identity for sync
        edited.Name = edited.Name.Trim();
        edited.Folder = string.IsNullOrWhiteSpace(edited.Folder) ? DefaultFolder : edited.Folder.Trim();
        edited.TotpSecret = string.IsNullOrWhiteSpace(edited.TotpSecret) ? null : edited.TotpSecret.Trim();
        edited.PasswordHistory = original?.PasswordHistory.ToList() ?? new List<PasswordHistoryItem>();
        edited.CreatedAt = original?.CreatedAt ?? now;
        edited.ModifiedAt = now;
        if (original is not null) edited.RememberPreviousPassword(original.Secret, now);
        return edited;
    }

    public static List<string> SplitTags(string text) =>
        text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public static List<string> SplitLines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    // ---------- import ----------

    public sealed record ImportPlan(List<VaultEntry> Fresh, int Duplicates, int SkippedRows)
    {
        public string Summary =>
            $"{Fresh.Count} new record(s) will be added to your vault." +
            (Duplicates > 0 ? $"\n{Duplicates} duplicate(s) will be skipped." : "") +
            (SkippedRows > 0 ? $"\n{SkippedRows} non-login row(s) (cards, identities) will be skipped." : "");
    }

    // Skips exact duplicates (same name, username and password) of records already in the vault.
    public static ImportPlan PlanImport(VaultData data, Storage.CsvTransfer.ImportResult result)
    {
        static string Key(VaultEntry e) => $"{e.Name}\u0001{e.Username}\u0001{e.Secret}".ToLowerInvariant();
        var existing = Live(data).Select(Key).ToHashSet();
        var fresh = result.Entries.Where(e => existing.Add(Key(e))).ToList();
        return new ImportPlan(fresh, result.Entries.Count - fresh.Count, result.SkippedRows);
    }

    // Replaces the vault's contents with a backup's (the caller saves, re-encrypting with the current password).
    public static void ReplaceContents(VaultData data, VaultData restored)
    {
        data.Entries = restored.Entries;
        data.PaymentCards = restored.PaymentCards;
        data.PersonalInfoRecords = restored.PersonalInfoRecords;
        data.Folders = restored.Folders;
        data.RemovedIds = restored.RemovedIds;
        data.RemovedFolders = restored.RemovedFolders;
    }
}

// Security Audit numbers, shared by both apps.
public sealed class SecurityReport
{
    public required List<VaultEntry> Entries { get; init; }
    public required List<VaultEntry> Weak { get; init; }
    public required HashSet<VaultEntry> Reused { get; init; }
    public bool Has2Fa { get; init; }
    public bool MasterStrong { get; init; }
    public int Score { get; init; }

    public string ScoreLabel => Score switch { >= 80 => "Strong", >= 60 => "Fair", _ => "Needs attention" };

    public static SecurityReport Create(VaultData data, string masterPassword)
    {
        var entries = VaultOperations.Live(data).ToList();
        var total = Math.Max(1, entries.Count);
        var weak = entries.Where(e => PasswordStrength.Evaluate(e.Secret) <= StrengthLevel.Fair).ToList();
        var reused = entries.Where(e => !string.IsNullOrEmpty(e.Secret)).GroupBy(e => e.Secret)
            .Where(g => g.Count() > 1).SelectMany(g => g).ToHashSet();
        var has2Fa = entries.Any(e => e.TotpSecret is not null);
        var masterStrong = PasswordStrength.Evaluate(masterPassword) >= StrengthLevel.Strong;

        // Up to 70 points for password health, scaled by the share of weak (60% weight) and reused (40%) passwords,
        // plus 15 each for a strong master password and for using 2FA.
        var baseScore = 70 * Math.Clamp(1 - (0.6 * weak.Count / total + 0.4 * reused.Count / total), 0, 1);
        var bonus = (masterStrong ? 15 : 0) + (has2Fa ? 15 : 0);
        return new SecurityReport
        {
            Entries = entries, Weak = weak, Reused = reused, Has2Fa = has2Fa, MasterStrong = masterStrong,
            Score = (int)Math.Clamp(baseScore + bonus, 0, 100)
        };
    }
}
