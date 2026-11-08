using Microsoft.Win32;

namespace BenchAutoClicker;

internal static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BenchAutoClicker";

    public static bool ReadEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            var command = key?.GetValue(ValueName) as string;
            // A moved executable may leave an older command under our owned value.
            // Treat it as enabled so the user can reliably turn it off; Apply repairs
            // the command to the current executable when startup remains enabled.
            return !string.IsNullOrWhiteSpace(command);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            throw new InvalidOperationException(AppStrings.T(
                "Не удалось прочитать настройку автозапуска Windows.",
                "The Windows startup setting could not be read."), exception);
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            var command = enabled ? BuildCommand(Application.ExecutablePath) : null;
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                ?? throw new IOException("The per-user Windows Run key is unavailable.");
            if (enabled)
            {
                key.SetValue(ValueName, command!, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            throw new InvalidOperationException(AppStrings.T(
                "Не удалось изменить автозапуск. Проверьте разрешения Windows для текущего пользователя.",
                "Startup could not be changed. Check Windows permissions for the current user."), exception);
        }
    }

    internal static string BuildCommand(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath) ||
            executablePath.Contains('"') || !string.Equals(Path.GetExtension(executablePath), ".exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Path.GetFileName(executablePath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(AppStrings.T(
                "Для автозапуска используйте готовый BenchAutoClicker.exe, а не запуск через dotnet.",
                "Use the published BenchAutoClicker.exe for startup instead of running it through dotnet."));
        }

        var command = $"\"{Path.GetFullPath(executablePath)}\" --startup";
        if (command.Length > 260)
        {
            throw new InvalidOperationException(AppStrings.T(
                "Путь к приложению слишком длинный для автозапуска Windows. Переместите приложение в папку с более коротким путём.",
                "The application path is too long for Windows startup. Move it to a folder with a shorter path."));
        }
        return command;
    }
}
