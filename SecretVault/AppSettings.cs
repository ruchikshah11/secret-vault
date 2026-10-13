using System.Text.Json;
using System.Text.Json.Serialization;
using SecretVault.Crypto;

namespace SecretVault.Gui;

// User preferences that are not secret (stored in plain JSON next to the vault; never contains vault data).
public class AppSettings
{
    public const int NeverLock = 0;

    // "Light" or "Dark". Null only while loading a settings file written before the theme moved in here.
    public string? Theme { get; set; }
    public int AutoLockMinutes { get; set; } = 5;
    public bool LockOnWindowsLock { get; set; } = true;
    public bool ShowWebsiteIcons { get; set; } = true;
    public int FolderPanelWidth { get; set; } = 280;
    public List<string> ExpandedFolders { get; set; } = new();
    public List<string> CollapsedSections { get; set; } = new();
    public DateTime? RecoveryReminderSnoozedUntilUtc { get; set; }

    // Unlock throttling: consecutive wrong master passwords and when the next attempt is allowed (survives restarts).
    public int FailedUnlockAttempts { get; set; }
    public DateTime? UnlockBlockedUntilUtc { get; set; }
    public PasswordOptions Generator { get; set; } = new();

    // Sync with other devices: the shared (still encrypted) vault copy, e.g. in OneDrive, and the last result.
    public string? SyncFilePath { get; set; }
    public DateTime? LastSyncUtc { get; set; }
    public string? LastSyncError { get; set; }

    [JsonIgnore]
    public ThemeMode ThemeMode
    {
        get => Theme == nameof(ThemeMode.Dark) ? ThemeMode.Dark : ThemeMode.Light;
        set => Theme = value.ToString();
    }

    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SecretVault");
    private static string SettingsPath => Path.Combine(Folder, "settings.json");
    private static string LegacyThemePath => Path.Combine(Folder, "theme.json"); // older builds kept the theme separately

    private static AppSettings? _current;
    public static AppSettings Current => _current ??= Load();

    private static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            settings = new AppSettings();
        }

        if (settings.Theme is null)
            MigrateLegacyTheme(settings);
        return settings;
    }

    // One-time move of theme.json into settings.json, keeping the user's choice; the old file is then removed.
    private static void MigrateLegacyTheme(AppSettings settings)
    {
        settings.ThemeMode = ThemeMode.Light;
        try
        {
            if (!File.Exists(LegacyThemePath)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(LegacyThemePath));
            if (doc.RootElement.TryGetProperty("Theme", out var theme) && theme.GetString() == nameof(ThemeMode.Dark))
                settings.ThemeMode = ThemeMode.Dark;
            settings.Save();
            File.Delete(LegacyThemePath);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Keep the default theme; the legacy file is left in place and retried next start.
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preferences are best-effort; failing to persist them must not break the app.
        }
    }
}
