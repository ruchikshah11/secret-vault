using System.Text.Json;
using SecretVault.Crypto;

namespace SecretVault.Mobile;

// Non-secret preferences (the phone's equivalent of the desktop settings.json).
public static class AppPrefs
{
    public static readonly int[] AutoLockChoices = { 1, 2, 5, 15, 30, 0 }; // minutes; 0 = only when you tap Lock

    public static int AutoLockMinutes
    {
        get => Preferences.Get("autolock_minutes", 2);
        set => Preferences.Set("autolock_minutes", value);
    }

    public static string DescribeAutoLock(int minutes) =>
        minutes == 0 ? "Only when I tap Lock" : minutes == 1 ? "1 minute" : $"{minutes} minutes";

    // "System", "Light" or "Dark".
    public static string Theme
    {
        get => Preferences.Get("theme", "System");
        set { Preferences.Set("theme", value); ApplyTheme(); }
    }

    public static void ApplyTheme()
    {
        if (Application.Current is { } app)
            app.UserAppTheme = Theme switch { "Light" => AppTheme.Light, "Dark" => AppTheme.Dark, _ => AppTheme.Unspecified };
    }

    public static PasswordOptions Generator
    {
        get
        {
            try { return JsonSerializer.Deserialize<PasswordOptions>(Preferences.Get("generator", "")) ?? new PasswordOptions(); }
            catch (JsonException) { return new PasswordOptions(); }
        }
        set => Preferences.Set("generator", JsonSerializer.Serialize(value));
    }

    public static bool SortByRecent
    {
        get => Preferences.Get("sort_recent", false);
        set => Preferences.Set("sort_recent", value);
    }

    public static DateTimeOffset? RecoveryReminderSnoozedUntil
    {
        get => Preferences.Get("recovery_snooze", 0L) is var t and > 0 ? new DateTimeOffset(t, TimeSpan.Zero) : null;
        set => Preferences.Set("recovery_snooze", value?.UtcTicks ?? 0L);
    }
}
