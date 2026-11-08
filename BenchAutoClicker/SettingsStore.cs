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
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            var json = JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true });
            // A crash while writing must not truncate the last valid preferences.
            // The final rename stays on the same volume and replaces the file atomically.
            temporaryPath = Path.Combine(SettingsDirectory, $"settings-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        catch
        {
            // Настройки необязательны: приложение продолжит работать с текущими значениями.
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                    // A stale temporary file must not prevent the application from exiting.
                }
            }
        }
    }
}
