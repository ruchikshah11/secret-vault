using SecretVault.Models;
using SecretVault.Storage;

namespace SecretVault.Gui;

// Keeps this PC's vault in step with a shared copy (e.g. in OneDrive or Google Drive) that the Android app also
// syncs with. The merge itself is SecretVault.Core's VaultSync, shared with the phone.
public sealed class DesktopSync
{
    private readonly FileVaultStore _store;
    private readonly VaultData _data;
    private readonly Func<string> _masterPassword;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DesktopSync(FileVaultStore store, VaultData data, Func<string> masterPassword)
    {
        _store = store;
        _data = data;
        _masterPassword = masterPassword;
    }

    public static string? SharedPath => AppSettings.Current.SyncFilePath;
    public static bool Enabled => !string.IsNullOrEmpty(SharedPath);

    public static string SuggestedFolder()
    {
        var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
        return !string.IsNullOrEmpty(oneDrive) && Directory.Exists(oneDrive)
            ? oneDrive
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    // Returns null when nothing ran (no sync file, or another sync in progress), otherwise a short summary.
    // interactive: may ask for the shared copy's password when it differs from this PC's master password.
    public async Task<VaultMerge.Result?> SyncAsync(IWin32Window? owner, bool interactive)
    {
        if (SharedPath is not { } path || !await _gate.WaitAsync(0)) return null;
        try
        {
            byte[]? remote = null;
            if (File.Exists(path))
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                remote = ms.ToArray();
            }

            VaultSync.Outcome outcome;
            try
            {
                outcome = await VaultSync.SyncAsync(_store, _data, _masterPassword(), remote);
            }
            catch (VaultAuthenticationException)
            {
                if (!interactive) throw;
                using var prompt = new PasswordPromptForm("Shared vault password",
                    "The shared copy was saved with a different master password (changed on another device). Enter that password to merge it.");
                if (prompt.ShowDialog(owner) != DialogResult.OK) return null;
                outcome = await VaultSync.SyncAsync(_store, _data, _masterPassword(), remote, prompt.Password);
            }

            // Write next to the target first, then swap, so a cloud client never uploads a half-written file.
            var temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, outcome.SharedCopy);
            File.Move(temp, path, overwrite: true);

            AppSettings.Current.LastSyncUtc = DateTime.UtcNow;
            AppSettings.Current.LastSyncError = null;
            AppSettings.Current.Save();
            return outcome.Merge;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or VaultAuthenticationException)
        {
            AppSettings.Current.LastSyncError = ex is VaultAuthenticationException
                ? "The shared copy uses a different master password. Open Settings › Sync and tap Sync now."
                : ex.Message;
            AppSettings.Current.Save();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }
}
