namespace BenchAutoClicker;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var singleInstance = new Mutex(true, @"Local\BenchAutoClicker.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Bench Auto Clicker уже запущен.",
                "Bench Auto Clicker",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
