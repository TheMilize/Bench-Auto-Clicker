using System.Reflection;
using BenchAutoClicker;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Theme.RefreshFromWindows();
        var folder = args.FirstOrDefault() ?? Path.Combine("TestResults", "ui");
        Directory.CreateDirectory(folder);
        var type = typeof(ClickConfiguration).Assembly.GetType("BenchAutoClicker.MainForm", true)!;
        using var form = new MainForm(new ClickEngine(configuration =>
            ClickResult.Sent(configuration.FixedPoint,
                configuration.MouseAction == MouseAction.DoubleLeft ? 2 : 1)), persistSettings: false);
        var report = new List<string>();
        Exception? error = null;
        form.Shown += async (_, _) =>
        {
            try
            {
                // Disable automatic persistence: diagnostics must not replace the user's settings.
                ((System.Windows.Forms.Timer)Field("_saveTimer")).Stop();
                if (!args.Contains("--shell-only") && !args.Contains("--native-only") && !args.Contains("--hotkeys-only"))
                {
                    var checks = new[] { (340, 800), (360, 800), (500, 680), (699, 680), (700, 620), (760, 620) };
                    foreach (var screen in Screen.AllScreens)
                    {
                        form.Location = new Point(screen.WorkingArea.Left + 12, screen.WorkingArea.Top + 12);
                        Application.DoEvents();
                        foreach (var (width, height) in checks)
                        {
                            var scale = form.DeviceDpi / 96F;
                            form.ClientSize = new Size((int)Math.Ceiling(width * scale), form.ClientSize.Height);
                            Call("ApplyResponsiveLayout");
                            form.ClientSize = new Size((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale));
                            foreach (var mode in Enum.GetValues<ClickMode>())
                            {
                                Call("SelectMode", mode);
                                Call("ApplyResponsiveLayout");
                                Application.DoEvents();
                                ((System.Windows.Forms.Timer)Field("_saveTimer")).Stop();
                                CheckVisible((Control)Field("_rootLayout"));
                                var content = (Panel)Field("_contentPanel");
                                if (content.VerticalScroll.Visible || content.HorizontalScroll.Visible)
                                {
                                    throw new InvalidOperationException("Unexpected scrolling");
                                }
                                var name = $"dpi{form.DeviceDpi}-{width}-{mode}";
                                CheckRoundedButtonCorners(form, folder, name);
                                if (width is 360 or 500 or 760)
                                {
                                    using var bitmap = new Bitmap(form.Width, form.Height);
                                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                                    bitmap.Save(Path.Combine(folder, name + ".png"));
                                }
                                report.Add($"PASS {name}; client={form.ClientSize}; profile={Field("_layoutProfile")}");
                                report.Add($"PASS {name}; clean button corners in normal, hover, selected, pressed and disabled states");
                            }
                        }
                    }

                    Call("SelectMode", ClickMode.Speed);
                    Call("ApplyPreset", 2);
                    EqualNumber("_speedIntervalMs", 50m);
                    Call("SelectMode", ClickMode.RandomInterval);
                    Call("ApplyPreset", 0);
                    EqualNumber("_randomMinimum", 1m);
                    EqualNumber("_randomMaximum", 15m);
                    Call("SelectMode", ClickMode.Burst);
                    Call("ApplyPreset", 1);
                    EqualNumber("_burstCount", 5m);
                    EqualNumber("_burstCps", 10m);
                    EqualNumber("_burstPause", 3m);
                    report.Add("PASS speed/random/burst presets");
                    Call("ResetPoint");
                    if (Field("_fixedPoint") is not null)
                    {
                        throw new InvalidOperationException("Reset failed");
                    }
                    report.Add("PASS point reset uses screen center");

                    // Exercise actual form lifecycle using fake input only.
                    var limit = (NumberBox)Field("_maxClicks");
                    limit.Value = 2.9m;
                    EqualNumber("_maxClicks", 3m);
                    report.Add("PASS integer input rounds stored value consistently with display");
                    ((NumberBox)Field("_startDelay")).Value = 0;
                    ((SelectBox)Field("_targetMode")).SelectedIndex = (int)TargetMode.Cursor;
                    Call("SelectMode", ClickMode.Speed);
                    ((NumberBox)Field("_speedIntervalMs")).Value = 20;
                    await TaskCall("ToggleRunningAsync");
                    var timeout = DateTime.UtcNow.AddSeconds(3);
                    while (((StatusPill)Field("_statusPill")).State != AppRunState.Stopped && DateTime.UtcNow < timeout)
                    {
                        await Task.Delay(20);
                    }
                    if (((StatusPill)Field("_statusPill")).State != AppRunState.Stopped ||
                        (long)Field("_sessionClicks") != 3 || !(ControlField("_speedCps").Enabled))
                    {
                        throw new InvalidOperationException("Auto-completion did not restore UI and exact physical count");
                    }
                    report.Add("PASS auto-limit restores stopped UI and exactly 3 physical clicks");

                    ((NumberBox)Field("_startDelay")).Value = 1;
                    limit.Value = 0;
                    var countdown = TaskCall("ToggleRunningAsync");
                    await Task.Delay(150);
                    if ((long)Field("_sessionClicks") != 0)
                    {
                        throw new InvalidOperationException("Old metrics leaked into new countdown");
                    }
                    await TaskCall("ToggleRunningAsync");
                    await countdown;
                    await Task.Delay(150);
                    if ((long)Field("_sessionClicks") != 0 ||
                        ((StatusPill)Field("_statusPill")).State != AppRunState.Stopped)
                    {
                        throw new InvalidOperationException("Countdown cancellation restored previous metrics");
                    }
                    report.Add("PASS cancelled restart keeps reset metrics and restores controls");
                }
                if (args.Contains("--native-only") || args.Contains("--native"))
                {
                    await NativePaintChecks.RunAsync(form, folder, report);
                }
                if (!args.Contains("--native-only"))
                {
                    await HotkeyChecks.RunAsync(form, folder, report);
                }
                if (!args.Contains("--native-only") && !args.Contains("--hotkeys-only"))
                {
                    await WindowsFeatureChecks.RunAsync(form, folder, report, CheckVisible);
                }
                Console.WriteLine(string.Join(Environment.NewLine, report));
            }
            catch (Exception exception)
            {
                error = exception;
                report.Add("FAIL " + exception);
                Console.Error.WriteLine(exception);
            }
            finally
            {
                // Avoid saving any diagnostic state from MainForm.OnFormClosing.
                form.Hide();
                form.Dispose();
                Application.ExitThread();
            }
        };
        Application.Run(form);
        File.WriteAllLines(Path.Combine(folder, "report.txt"), report);
        return error is null ? 0 : 1;

        object Field(string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        void Call(string name, params object[] values) => type.GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, values);
        Task TaskCall(string name) => (Task)type.GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null)!;
        Control ControlField(string name) => (Control)Field(name);
        void EqualNumber(string name, decimal expected)
        {
            var number = Field(name);
            var value = (decimal)number.GetType().GetProperty("Value")!.GetValue(number)!;
            if (value != expected)
            {
                throw new InvalidOperationException($"{name}: expected {expected}, got {value}");
            }
        }
    }

    private static void CheckVisible(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (!child.Visible)
            {
                continue;
            }
            if (child.Left < -1 || child.Top < -1 || child.Right > parent.ClientSize.Width + 1 ||
                child.Bottom > parent.ClientSize.Height + 1)
            {
                throw new InvalidOperationException($"Clipped {child.GetType().Name} '{child.Text}' in {parent.GetType().Name}: " +
                    $"{child.Bounds} outside {parent.ClientSize}");
            }
            CheckVisible(child);
        }
    }

    private static void CheckRoundedButtonCorners(Control parent, string folder, string name)
    {
        foreach (Control child in parent.Controls)
        {
            if (!child.Visible)
            {
                continue;
            }
            if (child is RoundedButton button)
            {
                var selected = button.Selected;
                var enabled = button.Enabled;
                try
                {
                    button.Enabled = true;
                    button.Selected = false;
                    PaintAndCheck("normal");
                    CallButton("OnMouseEnter", EventArgs.Empty);
                    PaintAndCheck("hover");
                    button.Selected = true;
                    PaintAndCheck("selected");
                    CallButton("OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1,
                        button.Width / 2, button.Height / 2, 0));
                    PaintAndCheck("pressed");
                    button.Enabled = false;
                    PaintAndCheck("disabled");
                }
                finally
                {
                    CallButton("OnMouseLeave", EventArgs.Empty);
                    button.Selected = selected;
                    button.Enabled = enabled;
                }

                void CallButton(string method, object argument) => typeof(RoundedButton)
                    .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(button, [argument]);

                void PaintAndCheck(string state)
                {
                    using var bitmap = new Bitmap(button.Width, button.Height);
                    using var graphics = Graphics.FromImage(bitmap);
                    // A real WM_PAINT skips background painting for an Opaque control.
                    // DrawToBitmap paints the background anyway and would miss this regression.
                    graphics.Clear(Color.Magenta);
                    var opaque = (bool)typeof(Control)
                        .GetMethod("GetStyle", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(button, [ControlStyles.Opaque])!;
                    using var paint = new PaintEventArgs(graphics, button.ClientRectangle);
                    if (!opaque)
                    {
                        CallButton("OnPaintBackground", paint);
                    }
                    CallButton("OnPaint", paint);
                    var background = button.Parent!.BackColor.ToArgb();
                    foreach (var corner in new[] { new Point(0, 0), new Point(button.Width - 1, 0),
                                 new Point(0, button.Height - 1), new Point(button.Width - 1, button.Height - 1) })
                    {
                        if (bitmap.GetPixel(corner.X, corner.Y).ToArgb() != background)
                        {
                            bitmap.Save(Path.Combine(folder, $"{name}-bad-corner-{state}.png"));
                            throw new InvalidOperationException($"Dirty {state} corner {corner} on '{button.Text}', Opaque={opaque}");
                        }
                    }
                }
            }
            CheckRoundedButtonCorners(child, folder, name);
        }
    }
}
