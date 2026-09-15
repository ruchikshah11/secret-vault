using System.Text.Json;

namespace SecretVault.Gui;

public static class ThemeSettings
{
    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SecretVault", "theme.json");

    public static ThemeMode Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return ThemeMode.Dark;
            var json = File.ReadAllText(SettingsPath);
            var data = JsonSerializer.Deserialize<ThemeData>(json);
            return data?.Theme == "Light" ? ThemeMode.Light : ThemeMode.Dark;
        }
        catch (IOException)
        {
            return ThemeMode.Dark;
        }
        catch (JsonException)
        {
            return ThemeMode.Dark;
        }
    }

    public static void Save(ThemeMode mode)
    {
        var dir = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(new ThemeData { Theme = mode.ToString() });
        File.WriteAllText(SettingsPath, json);
    }

    private class ThemeData
    {
        public string Theme { get; set; } = "Dark";
    }
}
