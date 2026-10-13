using Android.Content;
using Android.OS;
using Android.Widget;

namespace SecretVault.Mobile;

// Copies are marked sensitive (Android 13+ hides them in the clipboard preview and keyboard suggestions)
// and cleared after 5 minutes, like the desktop app.
public static class SecureClipboard
{
    public static readonly TimeSpan ClearAfter = TimeSpan.FromMinutes(5);
    private static CancellationTokenSource? _pendingClear;

    public static void Copy(string text, string what)
    {
        var clipboard = (ClipboardManager)Platform.AppContext.GetSystemService(Context.ClipboardService)!;
        var clip = ClipData.NewPlainText(what, text)!;
        var extras = new PersistableBundle();
        extras.PutBoolean("android.content.extra.IS_SENSITIVE", true);
        clip.Description!.Extras = extras;
        clipboard.PrimaryClip = clip;

        // Android 13+ shows its own "Copied" confirmation.
        if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            Toast.MakeText(Platform.AppContext, $"{what} copied · clears in 5 min", ToastLength.Short)!.Show();

        _pendingClear?.Cancel();
        var cancel = _pendingClear = new CancellationTokenSource();
        _ = Task.Delay(ClearAfter, cancel.Token).ContinueWith(_ =>
            MainThread.BeginInvokeOnMainThread(() => clipboard.ClearPrimaryClip()), TaskContinuationOptions.OnlyOnRanToCompletion);
    }
}
