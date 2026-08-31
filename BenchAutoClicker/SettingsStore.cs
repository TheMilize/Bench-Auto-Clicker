using System.Text.Json;

namespace BenchAutoClicker;

internal static class SettingsStore
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BenchAutoClicker");

    private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");

    public static UserPreferences Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new UserPreferences();
            }

            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<UserPreferences>(json) ?? new UserPreferences();
        }
        catch
        {
            return new UserPreferences();
        }
    }

    public static void Save(UserPreferences preferences)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Настройки необязательны: приложение продолжит работать с текущими значениями.
        }
    }
}
