using SecretVault.Storage;

namespace SecretVault.Mobile;

// Keeps this phone's vault in step with the shared copy that the PC also syncs with (a file in OneDrive,
// Google Drive…, opened through Android's file picker with lasting read/write access).
// The merge is SecretVault.Core's VaultSync, the same code the desktop app runs.
public static class MobileSync
{
    private const string FileKey = "sync_file";
    private const string NameKey = "sync_name";
    private const string LastKey = "sync_last";
    private const string ErrorKey = "sync_error";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static CancellationTokenSource? _soon;

    public static string? FileToken => Preferences.Get(FileKey, null as string);
    public static bool Enabled => FileToken is not null;
    public static string? FileName => Preferences.Get(NameKey, null as string);
    public static string? LastError => Preferences.Get(ErrorKey, null as string);
    public static DateTimeOffset? LastSync =>
        Preferences.Get(LastKey, 0L) is var t and > 0 ? new DateTimeOffset(t, TimeSpan.Zero) : null;

    public static string Status => LastError is { } error ? "Last sync failed: " + error
        : LastSync is { } at ? $"Last synced {at.LocalDateTime:d MMM yyyy, HH:mm}" : "Not synced yet";

    public static void Use(string token, string? name)
    {
        Preferences.Set(FileKey, token);
        Preferences.Set(NameKey, name ?? "Shared vault file");
        Preferences.Remove(ErrorKey);
    }

    public static void Forget()
    {
        Preferences.Remove(FileKey);
        Preferences.Remove(NameKey);
        Preferences.Remove(LastKey);
        Preferences.Remove(ErrorKey);
    }

    // After a change: sync a few seconds later (several quick edits become one sync).
    public static void SyncSoon()
    {
        if (!Enabled) return;
        _soon?.Cancel();
        var cancel = _soon = new CancellationTokenSource();
        _ = Task.Delay(TimeSpan.FromSeconds(3), cancel.Token).ContinueWith(
            _ => MainThread.BeginInvokeOnMainThread(async () => await SyncAsync(null)), TaskContinuationOptions.OnlyOnRanToCompletion);
    }

    // page != null: interactive (may ask for the shared copy's password, shows errors). Returns the summary, or null.
    public static async Task<string?> SyncAsync(Page? page)
    {
        if (FileToken is not { } token || VaultSession.Data is null || !await Gate.WaitAsync(0)) return null;
        try
        {
            var remote = await DeviceFiles.ReadAsync(token);
            VaultSync.Outcome outcome;
            try
            {
                outcome = await VaultSync.SyncAsync(VaultSession.Store, VaultSession.Vault, VaultSession.Password, remote);
            }
            catch (VaultAuthenticationException) when (page is not null)
            {
                var other = await PasswordPromptPage.AskAsync(page, "Shared vault password",
                    "The shared copy was saved with a different master password (changed on another device). Enter that password to merge it.");
                if (other is null) return null;
                outcome = await VaultSync.SyncAsync(VaultSession.Store, VaultSession.Vault, VaultSession.Password, remote, other);
            }

            await DeviceFiles.WriteAsync(token, outcome.SharedCopy);
            Preferences.Set(LastKey, DateTimeOffset.UtcNow.UtcTicks);
            Preferences.Remove(ErrorKey);
            if (outcome.Merge.ChangedLocal) VaultSession.NotifyChanged();
            return outcome.Merge.Summary;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var message = ex is VaultAuthenticationException
                ? "the shared copy uses a different master password. Tap Sync now in Settings."
                : ex.Message;
            Preferences.Set(ErrorKey, message);
            if (page is not null) await page.DisplayAlertAsync("Sync failed", message, "OK");
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }
}
