using Android.App;
using Android.Content;
using Android.Provider;
using AUri = Android.Net.Uri;

namespace SecretVault.Mobile;

// Files outside the app, through Android's document picker (works with phone storage, Google Drive, OneDrive…).
// The sync file keeps a persistable read/write grant, so the app can open it again later without asking.
public static class DeviceFiles
{
    private const int OpenRequest = 4101, CreateRequest = 4102, SaveRequest = 4103;
    private static TaskCompletionSource<AUri?>? _pending;

    // Called by MainActivity.OnActivityResult.
    public static void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode is not (OpenRequest or CreateRequest or SaveRequest)) return;
        _pending?.TrySetResult(resultCode == Result.Ok ? data?.Data : null);
    }

    private static Task<AUri?> StartAsync(Intent intent, int request)
    {
        _pending?.TrySetResult(null);
        _pending = new TaskCompletionSource<AUri?>();
        Platform.CurrentActivity!.StartActivityForResult(intent, request);
        return _pending.Task;
    }

    private static ContentResolver Resolver => Platform.AppContext.ContentResolver!;

    // Lets the user pick an existing file (create = false) or create a new one. Returns a token for later use.
    public static async Task<(string Token, string Name)?> PickSyncFileAsync(bool create)
    {
        var intent = new Intent(create ? Intent.ActionCreateDocument : Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(create ? "application/octet-stream" : "*/*");
        if (create) intent.PutExtra(Intent.ExtraTitle, "SecretVault-sync.db");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantPersistableUriPermission);

        var uri = await StartAsync(intent, create ? CreateRequest : OpenRequest);
        if (uri is null) return null;
        try
        {
            Resolver.TakePersistableUriPermission(uri, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        }
        catch (Java.Lang.SecurityException)
        {
            // Some providers don't offer lasting access; the file still works until the app restarts.
        }
        return (uri.ToString()!, DisplayName(uri) ?? "Shared vault file");
    }

    public static async Task<byte[]?> ReadAsync(string token)
    {
        var uri = AUri.Parse(token)!;
        await using var input = Resolver.OpenInputStream(uri);
        if (input is null) return null;
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    public static async Task WriteAsync(string token, byte[] bytes)
    {
        var uri = AUri.Parse(token)!;
        // "wt" = write + truncate, so a shorter file doesn't keep old bytes at the end.
        await using var output = Resolver.OpenOutputStream(uri, "wt") ?? throw new IOException("Can't write to the shared vault file.");
        await output.WriteAsync(bytes);
    }

    // "Save as…" for exports (encrypted backup, CSV). Returns false if the user cancelled.
    public static async Task<bool> SaveAsAsync(string suggestedName, string mimeType, byte[] bytes)
    {
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(mimeType);
        intent.PutExtra(Intent.ExtraTitle, suggestedName);
        var uri = await StartAsync(intent, SaveRequest);
        if (uri is null) return false;
        await using var output = Resolver.OpenOutputStream(uri, "wt") ?? throw new IOException("Can't write the file.");
        await output.WriteAsync(bytes);
        return true;
    }

    private static string? DisplayName(AUri uri)
    {
        try
        {
            using var cursor = Resolver.Query(uri, new[] { IOpenableColumns.DisplayName }, null, null, null);
            return cursor is not null && cursor.MoveToFirst() ? cursor.GetString(0) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
