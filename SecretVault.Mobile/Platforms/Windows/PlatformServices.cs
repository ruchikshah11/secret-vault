namespace SecretVault.Mobile;

// Windows build of the MAUI app (used to run and check the same screens on a PC). Fingerprint unlock is
// Android-only here; files use ordinary paths.

public static class BiometricUnlock
{
    public static bool IsAvailable => false;
    public static bool IsEnabled => false;
    public static bool Declined { get; set; }
    public static Task<bool> EnableAsync(string masterPassword) => Task.FromResult(false);
    public static Task<string?> UnlockAsync() => Task.FromResult<string?>(null);
    public static void Disable() { }
}

public static class SecureClipboard
{
    public static readonly TimeSpan ClearAfter = TimeSpan.FromMinutes(5);
    private static CancellationTokenSource? _pendingClear;

    public static void Copy(string text, string what)
    {
        _ = Clipboard.Default.SetTextAsync(text);
        _pendingClear?.Cancel();
        var cancel = _pendingClear = new CancellationTokenSource();
        _ = Task.Delay(ClearAfter, cancel.Token).ContinueWith(_ =>
            MainThread.BeginInvokeOnMainThread(async () => await Clipboard.Default.SetTextAsync(null)), TaskContinuationOptions.OnlyOnRanToCompletion);
    }
}

public static class DeviceFiles
{
    public static async Task<(string Token, string Name)?> PickSyncFileAsync(bool create)
    {
        if (create)
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SecretVault-sync.db");
            return (path, Path.GetFileName(path));
        }
        var file = await FilePicker.Default.PickAsync();
        return file is null ? null : (file.FullPath, file.FileName);
    }

    public static async Task<byte[]?> ReadAsync(string token) => File.Exists(token) ? await File.ReadAllBytesAsync(token) : null;

    public static async Task WriteAsync(string token, byte[] bytes)
    {
        var temp = token + ".tmp";
        await File.WriteAllBytesAsync(temp, bytes);
        File.Move(temp, token, overwrite: true);
    }

    public static async Task<bool> SaveAsAsync(string suggestedName, string mimeType, byte[] bytes)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), suggestedName);
        await File.WriteAllBytesAsync(path, bytes);
        return true;
    }
}
