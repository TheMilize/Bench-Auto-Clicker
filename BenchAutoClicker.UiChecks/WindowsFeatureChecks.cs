using BenchAutoClicker;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

internal static class WindowsFeatureChecks
{
    public static async Task RunAsync(MainForm main, string folder, List<string> report, Action<Control> checkLayout)
    {
        var secondary = Screen.AllScreens.OrderBy(screen => screen.Bounds.Width).First();
        main.Location = secondary.WorkingArea.Location + new Size(15, 15);
        Application.DoEvents();
        Check(AppStrings.GetEffectiveLanguage("system", CultureInfo.GetCultureInfo("ru-RU")) == "ru", "Russian system language");
        Check(AppStrings.GetEffectiveLanguage("system", CultureInfo.GetCultureInfo("de-DE")) == "en", "Unsupported language falls back to English");
        Check(AppStrings.GetEffectiveLanguage("en", CultureInfo.GetCultureInfo("ru-RU")) == "en", "Explicit language overrides Windows");
        Check(new UserPreferences() is { Language: "system", LaunchAtLogin: false, TrayMode: false }, "Safe default shell preferences");

        Check(WindowsStartup.BuildCommand(@"C:\My Apps\BenchAutoClicker.exe") == "\"C:\\My Apps\\BenchAutoClicker.exe\" --startup".Replace("\\\\", "\\"),
            "Startup command quotes executable paths with spaces");
        RejectStartup("dotnet.exe");
        RejectStartup(@"C:\Program Files\dotnet\dotnet.exe");
        RejectStartup("relative.exe");
        RejectStartup("C:\\bad\"path\\BenchAutoClicker.exe");
        Check(true, "Invalid startup commands are rejected without modifying the registry");

        using (var logo = BrandAssets.LoadBitmap())
        {
            Check(new[] { logo.GetPixel(0, 0), logo.GetPixel(logo.Width - 1, 0),
                logo.GetPixel(0, logo.Height - 1), logo.GetPixel(logo.Width - 1, logo.Height - 1) }.All(color => color.A == 0),
                "New application logo has transparent corners");
            var visiblePixels = 0;
            var white = true;
            for (var y = 0; y < logo.Height; y++)
            {
                for (var x = 0; x < logo.Width; x++)
                {
                    var pixel = logo.GetPixel(x, y);
                    if (pixel.A > 128)
                    {
                        visiblePixels++;
                        white &= pixel.R >= 248 && pixel.R == pixel.G && pixel.G == pixel.B;
                    }
                }
            }
            Check(visiblePixels > 0 && white, "Application logo remains monochrome white rather than using the Windows accent");
        }
        foreach (var dark in new[] { false, true })
        {
            using var icon = BrandAssets.LoadTrayIcon(dark);
            using var image = icon.ToBitmap();
            Check(image.GetPixel(0, 0).A == 0 && image.Width >= 16, $"Tray icon supports transparent {(dark ? "dark" : "light")} Windows taskbar");
        }

        var speed = (NumberBox)Field("_speedIntervalMs");
        var oldValue = speed.Value;
        foreach (var language in new[] { "en", "ru" })
        {
            main.SetLanguage(language);
            Check(speed.Value == oldValue, $"Language {language} preserves exact numeric settings");
            if (language == "en")
            {
                var russianText = Controls(main).Where(control => control.Visible && Regex.IsMatch(control.Text, "[А-Яа-яЁё]"))
                    .Select(control => control.Text).ToArray();
                Check(russianText.Length == 0, "English main window has no untranslated Russian captions: " + string.Join(", ", russianText));
            }
            foreach (var dark in new[] { false, true })
            {
                Theme.SetForTesting(dark, dark ? Color.FromArgb(96, 205, 255) : Color.FromArgb(0, 103, 192));
                Theme.ApplyTo(main);
                main.ClientSize = new Size((int)Math.Ceiling(760 * main.DeviceDpi / 96F), main.ClientSize.Height);
                Call("ApplyResponsiveLayout");
                Application.DoEvents();
                checkLayout((Control)Field("_rootLayout"));
                Check(main.BackColor == Theme.Window, $"Existing main window follows {(dark ? "dark" : "light")} theme in {language}");
                Save(main, $"shell-{language}-{(dark ? "dark" : "light")}-main");

                using var preferences = new PreferencesForm(new AppPreferences(language, false, false));
                preferences.Show(main);
                Application.DoEvents();
                Check(Math.Abs(preferences.ClientSize.Width * 96F / preferences.DeviceDpi - 520) <= 2,
                    "Preferences retain their logical size on the secondary monitor");
                checkLayout(preferences);
                Check(preferences.Result is { LaunchAtLogin: false, TrayMode: false }, "Preferences dialog does not change settings on open");
                Save(preferences, $"shell-{language}-{(dark ? "dark" : "light")}-preferences");
                for (var screenIndex = 0; screenIndex < Screen.AllScreens.Length; screenIndex++)
                {
                    var screen = Screen.AllScreens[screenIndex];
                    preferences.Location = screen.WorkingArea.Location + new Size(12, 12);
                    Application.DoEvents();
                    // Crossing monitors first updates DPI and physical dimensions; center only afterwards.
                    preferences.Location = new Point(screen.WorkingArea.Left + (screen.WorkingArea.Width - preferences.Width) / 2,
                        screen.WorkingArea.Top + (screen.WorkingArea.Height - preferences.Height) / 2);
                    Application.DoEvents();
                    Check(screen.WorkingArea.Contains(preferences.Bounds),
                        $"Preferences fit monitor {screenIndex + 1} at {preferences.DeviceDpi} DPI: {preferences.Bounds} inside {screen.WorkingArea}");
                    checkLayout(preferences);
                    Save(preferences, $"shell-{language}-{(dark ? "dark" : "light")}-preferences-screen{screenIndex + 1}-dpi{preferences.DeviceDpi}");
                }
                preferences.Close();

                var toggles = 0;
                using var popup = new TrayMenuForm(() => toggles++, _ => { }, () => { }, () => { });
                popup.UpdateSnapshot(new TraySnapshot(ClickMode.RandomInterval, false, false, 123, AppStrings.T("Всё готово")));
                popup.ShowNear(Screen.FromControl(main).WorkingArea.Location + new Size(80, 80));
                Application.DoEvents();
                Check(Math.Abs(popup.ClientSize.Width * 96F / popup.DeviceDpi - 356) <= 2,
                    "Tray flyout retains its logical size on the secondary monitor");
                checkLayout(popup);
                var pill = Controls(popup).OfType<StatusPill>().Single();
                Check(pill.Width * 96F / pill.DeviceDpi >= 90 && pill.Height * 96F / pill.DeviceDpi >= 24,
                    $"Tray status badge keeps readable bounds ({pill.Size}, {pill.DeviceDpi} DPI)");
                Save(popup, $"shell-{language}-{(dark ? "dark" : "light")}-tray");
                Invoke(popup, "OnKeyDown", new KeyEventArgs(Keys.F6));
                Check(toggles == 0, "Flyout does not double-trigger registered global F6");
                popup.UpdateSnapshot(new TraySnapshot(ClickMode.Speed, false, false, 0, "", GlobalToggleAvailable: false));
                Invoke(popup, "OnKeyDown", new KeyEventArgs(Keys.F6));
                Check(toggles == 1, "Flyout F6 fallback works when global hotkey is unavailable");
                popup.Close();
            }
        }
        foreach (var workArea in new[] { new Rectangle(0, 0, 1920, 1040), new Rectangle(-1080, 40, 1080, 1840) })
        {
            foreach (var anchor in new[] { new Point(workArea.Left - 30, workArea.Top + 50),
                         new Point(workArea.Right + 30, workArea.Top + 50), new Point(workArea.Left + 100, workArea.Top - 30),
                         new Point(workArea.Right - 20, workArea.Bottom + 30) })
            {
                var size = new Size(356, 328);
                var position = TrayMenuForm.PositionNear(anchor, size, workArea);
                Check(workArea.Contains(new Rectangle(position, size)), "Flyout stays on screen for taskbars on all four edges");
            }
        }

        main.ApplyAppPreferences(new AppPreferences("en", false, true));
        Check(!main.Visible && !main.ShowInTaskbar, "Tray mode hides the main window and taskbar entry");
        var controller = (TrayController)Field("_trayController");
        Check(controller.IsVisible, "Tray mode keeps the notification icon visible");
        typeof(MainForm).GetField("_fixedPoint", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(main, TargetMath.CenterOf(main.Bounds));
        ((SelectBox)Field("_targetMode")).SelectedIndex = (int)TargetMode.FixedPoint;
        Check(Call("CreateConfiguration") is ClickConfiguration,
            "Hidden main-window bounds do not block fixed-point tray clicks");
        ((SelectBox)Field("_targetMode")).SelectedIndex = (int)TargetMode.Cursor;

        var activations = 0;
        var callbackThread = 0;
        var visibleAfterActivation = false;
        using (var activation = new AppActivation(main, () =>
        {
            activations++;
            callbackThread = Environment.CurrentManagedThreadId;
            main.ShowMainSettings();
            visibleAfterActivation = main.Visible;
        }))
        {
            Check(AppActivation.TryActivate(), "Second-instance activation signal is delivered");
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (activations == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(10);
            }
            Check(activations == 1 && main.Visible && !((ClickEngine)Field("_engine")).IsRunning,
                "Second launch restores the hidden window without starting clicks; " +
                $"activations={activations}; visible={main.Visible}; callbackVisible={visibleAfterActivation}; " +
                $"handle={main.IsHandleCreated}; allowMain={Field("_allowMainWindow")}; closing={Field("_closing")}; " +
                $"state={main.WindowState}; uiThread={Environment.CurrentManagedThreadId}; callbackThread={callbackThread}; " +
                $"context={SynchronizationContext.Current?.GetType().Name ?? "null"}; " +
                $"threadPool={ThreadPool.ThreadCount}; pending={ThreadPool.PendingWorkItemCount}");
        }
        main.ShowMainSettings();
        Check(main.Visible && main.ShowInTaskbar, "Tray settings action restores the main window");
        main.Close();
        Check(!main.IsDisposed && !main.Visible && controller.IsVisible, "Closing main window returns to tray without exiting");
        main.ShowMainSettings();
        main.ApplyAppPreferences(new AppPreferences("en", false, false));
        Check(main.Visible && !controller.IsVisible, "Disabling tray mode restores the regular window and removes the icon");

        // Switching UI language during a real fake-input session must not restart it.
        ((NumberBox)Field("_startDelay")).Value = 0;
        ((NumberBox)Field("_maxClicks")).Value = 0;
        ((SelectBox)Field("_targetMode")).SelectedIndex = (int)TargetMode.Cursor;
        await (Task)Call("ToggleRunningAsync")!;
        var engine = (ClickEngine)Field("_engine");
        await Task.Delay(60);
        var count = engine.TotalClicks;
        main.SetLanguage("ru");
        await Task.Delay(60);
        Check(engine.IsRunning && engine.TotalClicks >= count, "Changing language keeps the active engine session running");
        await (Task)Call("ToggleRunningAsync")!;
        Theme.RefreshFromWindows();
        Theme.ApplyTo(main);
        main.SetLanguage("system");

        await using var hiddenEngine = new ClickEngine(configuration => ClickResult.Sent(configuration.FixedPoint, 1));
        using var hidden = new MainForm(hiddenEngine, persistSettings: false);
        var hiddenPreferences = (UserPreferences)typeof(MainForm)
            .GetField("_preferences", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(hidden)!;
        hiddenPreferences.TrayMode = true;
        hidden.ConfigureShell(launchedAtLogin: true);
        hidden.Show();
        Application.DoEvents();
        Check(!hidden.Visible && !hidden.ShowInTaskbar && !hiddenEngine.IsRunning,
            "Login launch in tray mode creates no main window and starts no clicks");
        hidden.ExitApplication();
        Check(hidden.IsDisposed && !hiddenEngine.IsRunning,
            "Explicit tray exit disposes the app and stops the engine");

        void Check(bool condition, string name)
        {
            if (!condition)
            {
                throw new InvalidOperationException(name);
            }
            report.Add("PASS " + name);
        }
        object Field(string name) => typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        object? Call(string name, params object[] values) => Invoke(main, name, values);
        void Save(Form form, string name)
        {
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(folder, name + ".png"));
        }
    }

    private static object? Invoke(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);

    private static IEnumerable<Control> Controls(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
        {
            foreach (var descendant in Controls(child))
            {
                yield return descendant;
            }
        }
    }

    private static void RejectStartup(string executable)
    {
        try
        {
            WindowsStartup.BuildCommand(executable);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException("Unsafe startup command was accepted");
    }
}
