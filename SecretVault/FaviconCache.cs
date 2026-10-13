using SecretVault.Storage;

namespace SecretVault.Gui;

// Downloads website icons straight from each site (https://host/favicon.ico) and keeps them in memory only,
// so no list of your sites is written to disk or sent to a third-party icon service.
public static class FaviconCache
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(6) };
    private static readonly Dictionary<string, Image?> Icons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Pending = new(StringComparer.OrdinalIgnoreCase);

    // Raised on the UI thread when an icon finishes loading, so lists can repaint.
    public static event Action? IconLoaded;

    static FaviconCache() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 SecretVault");

    // Returns the icon if cached; otherwise starts a download (once) and returns null for now.
    public static Image? Get(string url, Control uiThread)
    {
        if (!AppSettings.Current.ShowWebsiteIcons) return null;
        var host = CsvTransfer.HostOf(url);
        if (host is null) return null;
        if (Icons.TryGetValue(host, out var cached)) return cached;
        if (Pending.Add(host)) _ = LoadAsync(host, uiThread);
        return null;
    }

    private static async Task LoadAsync(string host, Control uiThread)
    {
        Image? image = null;
        try
        {
            var bytes = await Http.GetByteArrayAsync($"https://{host}/favicon.ico");
            image = Decode(bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException or InvalidOperationException)
        {
            // No icon available; the letter avatar stays.
        }

        void Store()
        {
            Icons[host] = image;
            Pending.Remove(host);
            if (image is not null) IconLoaded?.Invoke();
        }

        if (uiThread.IsHandleCreated && !uiThread.IsDisposed) uiThread.BeginInvoke(Store);
        else Store();
    }

    private static Image? Decode(byte[] bytes)
    {
        if (bytes.Length == 0) return null;
        try
        {
            using var ms = new MemoryStream(bytes);
            using var icon = new Icon(ms, 32, 32);
            return icon.ToBitmap();
        }
        catch (ArgumentException)
        {
            // Many sites serve a PNG at /favicon.ico.
            try
            {
                using var ms = new MemoryStream(bytes);
                using var img = Image.FromStream(ms);
                return new Bitmap(img, 32, 32);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
