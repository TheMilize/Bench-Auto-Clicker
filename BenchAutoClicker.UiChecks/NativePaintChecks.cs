using BenchAutoClicker;
using System.Reflection;

/// <summary>
/// Exercises the real HWND paint path. DrawToBitmap/WM_PRINT deliberately are not
/// used: both can paint missing controls while producing a falsely healthy image.
/// </summary>
internal static class NativePaintChecks
{
    public static async Task RunAsync(MainForm main, string folder, List<string> report)
    {
        if (main.InvokeRequired || !main.Visible)
        {
            throw new InvalidOperationException("Native paint checks must run on the visible form's UI thread.");
        }
        var engine = (ClickEngine)Field("_engine");
        if (engine.IsRunning)
        {
            throw new InvalidOperationException("Native paint checks require a stopped fake-input session.");
        }

        Directory.CreateDirectory(folder);
        var originalLocation = main.Location;
        var originalTopMost = main.TopMost;
        var originalMode = (ClickMode)Field("_selectedMode");
        var logicalClientSize = new Size(
            (int)Math.Round(main.ClientSize.Width * 96F / main.DeviceDpi),
            (int)Math.Round(main.ClientSize.Height * 96F / main.DeviceDpi));
        var probes = new Dictionary<Control, PaintProbe>();
        try
        {
            AttachTree(main);
            foreach (Control mode in (Array)Field("_modeViews"))
            {
                AttachTree(mode);
            }
            main.TopMost = true;
            main.BringToFront();
            main.Activate();
            await DrainPaintAsync();
            CaptureAndCheck("initial");

            foreach (var screen in Screen.AllScreens)
            {
                main.Location = new Point(screen.WorkingArea.Left + 12, screen.WorkingArea.Top + 12);
                await DrainPaintAsync();
                foreach (var logicalSize in new[] { new Size(360, 800), new Size(760, 620) })
                {
                    var scale = main.DeviceDpi / 96F;
                    var physicalSize = new Size(
                        (int)Math.Ceiling(logicalSize.Width * scale),
                        (int)Math.Ceiling(logicalSize.Height * scale));
                    var outerSize = new Size(physicalSize.Width + main.Width - main.ClientSize.Width,
                        physicalSize.Height + main.Height - main.ClientSize.Height);
                    if (outerSize.Width + 24 > screen.WorkingArea.Width ||
                        outerSize.Height + 24 > screen.WorkingArea.Height)
                    {
                        report.Add($"SKIP native paint {logicalSize.Width}x{logicalSize.Height} at {main.DeviceDpi} DPI: working area too small for physical capture");
                        continue;
                    }
                    main.ClientSize = new Size(physicalSize.Width, main.ClientSize.Height);
                    await DrainPaintAsync();
                    main.ClientSize = physicalSize;
                    await DrainPaintAsync();
                    var name = $"dpi{main.DeviceDpi}-{logicalSize.Width}";

                    foreach (var mode in Enum.GetValues<ClickMode>())
                    {
                        Call("SelectMode", mode);
                        await DrainPaintAsync();
                        AttachTree(main);
                        CaptureAndCheck($"{name}-{mode}-switch");
                    }

                    // Repeated switches are made in one message turn, like a burst
                    // of user input before pending layout/paint messages have run.
                    for (var index = 0; index < 24; index++)
                    {
                        Call("SelectMode", (ClickMode)(index % 3));
                    }
                    await DrainPaintAsync();
                    CaptureAndCheck($"{name}-rapid-switch");

                    var visible = VisibleTree(main).Where(control => control.IsHandleCreated).ToArray();
                    var baseline = visible.ToDictionary(control => control,
                        control => probes.TryGetValue(control, out var probe) ? probe.PaintCount : 0);
                    foreach (var control in visible.Where(control => control.Controls.Count == 0))
                    {
                        // Dirty a tiny part of each leaf's real client DC. A later
                        // native repaint must replace it; a bitmap-print path would
                        // bypass the dirty on-screen pixels and miss this regression.
                        using var graphics = control.CreateGraphics();
                        using var marker = new SolidBrush(Color.Magenta);
                        graphics.FillRectangle(marker,
                            Math.Max(0, control.ClientSize.Width / 2 - 1),
                            Math.Max(0, control.ClientSize.Height / 2 - 1), 3, 3);
                    }
                    main.Invalidate(invalidateChildren: true);
                    await DrainPaintAsync();
                    CaptureAndCheck($"{name}-invalidate");
                    var missed = visible.Where(IsTextSurface)
                        .Where(control => probes.TryGetValue(control, out var probe) &&
                                          probe.PaintCount <= baseline[control])
                        .Select(Describe).ToArray();
                    if (missed.Length > 0)
                    {
                        throw new InvalidOperationException("Visible controls did not receive native WM_PAINT after invalidation: " + string.Join("; ", missed));
                    }
                    report.Add($"PASS native WM_PAINT reaches visible text controls at {name}");

                    // An owned cover exercises real occlusion/exposure rather than
                    // synthesizing OnPaint calls or manually refreshing children.
                    using (var cover = new Form
                    {
                        FormBorderStyle = FormBorderStyle.None,
                        ShowInTaskbar = false,
                        StartPosition = FormStartPosition.Manual,
                        BackColor = Theme.Window,
                        Bounds = main.Bounds,
                        TopMost = true
                    })
                    {
                        cover.Show(main);
                        await Task.Delay(35);
                        cover.Hide();
                    }
                    main.Activate();
                    await DrainPaintAsync();
                    CaptureAndCheck($"{name}-exposed");

                    main.Hide();
                    await Task.Delay(25);
                    main.Show();
                    main.Activate();
                    await DrainPaintAsync();
                    CaptureAndCheck($"{name}-reshown");
                }
            }
        }
        finally
        {
            foreach (var probe in probes.Values)
            {
                probe.Dispose();
            }
            if (!main.IsDisposed)
            {
                main.TopMost = originalTopMost;
                main.Location = originalLocation;
                Call("SelectMode", originalMode);
                await DrainPaintAsync();
                main.ClientSize = new Size(
                    (int)Math.Round(logicalClientSize.Width * main.DeviceDpi / 96F),
                    (int)Math.Round(logicalClientSize.Height * main.DeviceDpi / 96F));
            }
        }

        object Field(string name) => typeof(MainForm)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(main)!;
        void Call(string name, params object[] values) => typeof(MainForm)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, values);

        void AttachTree(Control root)
        {
            if (!probes.ContainsKey(root))
            {
                probes.Add(root, new PaintProbe(root));
            }
            foreach (Control child in root.Controls)
            {
                AttachTree(child);
            }
        }

        async Task DrainPaintAsync()
        {
            // Yield to the application's ordinary layout + WM_PAINT dispatch.
            // Do not call Refresh/Update/DrawToBitmap here: that masks missing paints.
            await Task.Delay(65);
            await Task.Delay(35);
        }

        void CaptureAndCheck(string name)
        {
            var origin = main.PointToScreen(Point.Empty);
            var size = main.ClientSize;
            using var frame = new Bitmap(size.Width, size.Height);
            using (var graphics = Graphics.FromImage(frame))
            {
                graphics.CopyFromScreen(origin, Point.Empty, size, CopyPixelOperation.SourceCopy);
            }
            var path = Path.Combine(folder, "native-" + name + ".png");
            frame.Save(path);
            var failures = new List<string>();
            foreach (var control in VisibleTree(main).Where(IsTextSurface))
            {
                var rectangle = VisibleClientRectangle(control, main);
                if (rectangle.Width < 3 || rectangle.Height < 3)
                {
                    failures.Add(Describe(control) + " has no drawable text area");
                    continue;
                }
                var expected = TextColor(control);
                var ink = 0;
                for (var y = rectangle.Top; y < rectangle.Bottom && ink < 3; y++)
                {
                    for (var x = rectangle.Left; x < rectangle.Right && ink < 3; x++)
                    {
                        var pixel = frame.GetPixel(x, y);
                        if (Math.Abs(pixel.R - expected.R) <= 42 &&
                            Math.Abs(pixel.G - expected.G) <= 42 &&
                            Math.Abs(pixel.B - expected.B) <= 42)
                        {
                            ink++;
                        }
                    }
                }
                if (ink < 3)
                {
                    failures.Add(Describe(control) + " has no native foreground pixels");
                }
            }
            if (failures.Count > 0)
            {
                throw new InvalidOperationException($"Native frame '{name}' contains blank text surfaces ({path}): " +
                    string.Join("; ", failures));
            }
            report.Add($"PASS native frame {name}: visible captions and values painted without bitmap-print fallback");
        }
    }

    private static bool IsTextSurface(Control control) => control.Visible && control.Enabled &&
        !string.IsNullOrWhiteSpace(control.Text) &&
        control is Label or TextBoxBase or RoundedButton or SelectBox or ToggleSwitch or StatusPill;

    private static Color TextColor(Control control) => control is StatusPill pill
        ? pill.State switch { AppRunState.Running => Theme.Red, AppRunState.Countdown => Theme.Amber, _ => Theme.Muted }
        : control.ForeColor;

    private static string Describe(Control control) =>
        $"{control.GetType().Name} '{control.Text}' {control.Bounds}";

    private static IEnumerable<Control> VisibleTree(Control root)
    {
        if (!root.Visible)
        {
            yield break;
        }
        yield return root;
        foreach (Control child in root.Controls)
        {
            foreach (var descendant in VisibleTree(child))
            {
                yield return descendant;
            }
        }
    }

    private static Rectangle VisibleClientRectangle(Control control, Form main)
    {
        var screenRectangle = control.RectangleToScreen(control.ClientRectangle);
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            screenRectangle.Intersect(parent.RectangleToScreen(parent.ClientRectangle));
        }
        var origin = main.PointToScreen(Point.Empty);
        screenRectangle.Offset(-origin.X, -origin.Y);
        screenRectangle.Intersect(main.ClientRectangle);
        return screenRectangle;
    }

    private sealed class PaintProbe : NativeWindow, IDisposable
    {
        private readonly Control _control;
        private bool _disposed;
        public int PaintCount { get; private set; }

        public PaintProbe(Control control)
        {
            _control = control;
            control.HandleCreated += HandleCreated;
            control.HandleDestroyed += HandleDestroyed;
            if (control.IsHandleCreated)
            {
                AssignHandle(control.Handle);
            }
        }

        private void HandleCreated(object? sender, EventArgs e)
        {
            if (!_disposed)
            {
                AssignHandle(_control.Handle);
            }
        }

        private void HandleDestroyed(object? sender, EventArgs e)
        {
            if (Handle != IntPtr.Zero)
            {
                ReleaseHandle();
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x000F) // WM_PAINT, not WM_PRINT / WM_PRINTCLIENT.
            {
                PaintCount++;
            }
            base.WndProc(ref message);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _control.HandleCreated -= HandleCreated;
            _control.HandleDestroyed -= HandleDestroyed;
            if (Handle != IntPtr.Zero)
            {
                ReleaseHandle();
            }
        }
    }
}
