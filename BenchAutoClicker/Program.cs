namespace BenchAutoClicker;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var launchedAtLogin = args.Contains("--startup", StringComparer.OrdinalIgnoreCase);
        using var singleInstance = new Mutex(true, @"Local\BenchAutoClicker.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (!launchedAtLogin)
            {
                AppActivation.TryActivate();
            }
            return;
        }

        ApplicationConfiguration.Initialize();
        Theme.RefreshFromWindows();
        using var mainWindow = new MainForm();
        mainWindow.ConfigureShell(launchedAtLogin);
        using var activation = new AppActivation(mainWindow, mainWindow.ShowMainSettings);
        Application.Run(mainWindow);
    }
}
