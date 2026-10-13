using SecretVault.Models;
using SecretVault.Storage;

namespace SecretVault.Mobile;

// The unlocked vault. Like the desktop app, the master password stays in memory while unlocked (needed to save
// changes) and is dropped when the vault locks.
public static class VaultSession
{
    private const string ImportedAtKey = "vault_imported_at";
    private const string FailedAttemptsKey = "unlock_failed";
    private const string BlockedUntilKey = "unlock_blocked_until";

    public static string VaultPath => Path.Combine(FileSystem.AppDataDirectory, "vault.db");
    public static FileVaultStore Store { get; } = new(VaultPath);
    public static bool HasVault => File.Exists(VaultPath);
    public static VaultData? Data { get; private set; }
    public static string? MasterPassword { get; private set; }

    // Raised (on the UI thread) after the vault's contents changed, e.g. a sync brought in records from the PC.
    public static event Action? Changed;

    public static VaultData Vault => Data ?? throw new InvalidOperationException("The vault is locked.");
    public static string Password => MasterPassword ?? throw new InvalidOperationException("The vault is locked.");

    public static DateTimeOffset? ImportedAt =>
        Preferences.Get(ImportedAtKey, 0L) is var ticks and > 0 ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    public static IEnumerable<VaultEntry> LiveEntries => Data is null ? [] : VaultOperations.Live(Data);

    public static async Task<bool> TryUnlockAsync(string masterPassword)
    {
        try
        {
            // Argon2id takes a moment on a phone: keep it off the UI thread.
            Data = await Task.Run(() => Store.LoadAsync(masterPassword));
            MasterPassword = masterPassword;
            Preferences.Remove(FailedAttemptsKey);
            Preferences.Remove(BlockedUntilKey);
            return true;
        }
        catch (VaultAuthenticationException)
        {
            RegisterFailedAttempt();
            return false;
        }
    }

    public static async Task CreateAsync(string masterPassword)
    {
        var data = new VaultData();
        await Task.Run(() => Store.SaveAsync(data, masterPassword));
        Data = data;
        MasterPassword = masterPassword;
        Preferences.Set(ImportedAtKey, DateTimeOffset.UtcNow.UtcTicks);
    }

    // Used after "Forgot password" / changing the password: the vault is already open with the new password.
    public static void Opened(VaultData data, string masterPassword)
    {
        Data = data;
        MasterPassword = masterPassword;
    }

    public static async Task SaveAsync()
    {
        await Task.Run(() => Store.SaveAsync(Vault, Password));
        MobileSync.SyncSoon();
    }

    public static async Task ChangeMasterPasswordAsync(string newPassword)
    {
        await Task.Run(() => Store.ChangeMasterPasswordAsync(Vault, Password, newPassword));
        MasterPassword = newPassword;
        if (BiometricUnlock.IsEnabled) BiometricUnlock.Disable(); // it holds the old password
    }

    public static void NotifyChanged() => MainThread.BeginInvokeOnMainThread(() => Changed?.Invoke());

    public static void Lock()
    {
        Data = null;
        MasterPassword = null;
    }

    // ---------- wrong-password throttling (same rules as the desktop app) ----------

    public static TimeSpan RemainingBlock()
    {
        var until = Preferences.Get(BlockedUntilKey, 0L);
        var left = new DateTimeOffset(until, TimeSpan.Zero) - DateTimeOffset.UtcNow;
        return until > 0 && left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    private static void RegisterFailedAttempt()
    {
        var attempts = Preferences.Get(FailedAttemptsKey, 0) + 1;
        Preferences.Set(FailedAttemptsKey, attempts);
        if (attempts < 5) return;
        // 30 s after the 5th wrong password, doubling each time, at most 15 minutes.
        var wait = TimeSpan.FromSeconds(Math.Min(30 * Math.Pow(2, attempts - 5), 15 * 60));
        Preferences.Set(BlockedUntilKey, DateTimeOffset.UtcNow.Add(wait).UtcTicks);
    }

    // ---------- the vault file on this phone ----------

    // Replaces this phone's vault with a vault file's bytes (picked from storage, Drive, OneDrive…).
    public static void ImportBytes(byte[] bytes)
    {
        if (!FileVaultStore.LooksLikeVault(bytes))
            throw new InvalidDataException("This isn't a SecretVault vault file. Choose vault.db (or the sync file) from your PC.");
        if (HasVault) Store.CreateBackupNow();
        var temp = VaultPath + ".incoming";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, VaultPath, overwrite: true);
        Preferences.Set(ImportedAtKey, DateTimeOffset.UtcNow.UtcTicks);
        Lock();
    }

    public static void RemoveFromPhone()
    {
        Lock();
        if (File.Exists(VaultPath)) File.Delete(VaultPath);
        if (Directory.Exists(Store.BackupDirectory)) Directory.Delete(Store.BackupDirectory, recursive: true);
        Preferences.Remove(ImportedAtKey);
        Preferences.Remove(FailedAttemptsKey);
        Preferences.Remove(BlockedUntilKey);
        BiometricUnlock.Disable();
        MobileSync.Forget();
    }
}
