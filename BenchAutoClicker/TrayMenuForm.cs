namespace BenchAutoClicker;

internal sealed class TrayMenuForm : BufferedForm
{
    private readonly Action _toggle;
    private readonly Action<ClickMode> _selectMode;
    private readonly Action _settings;
    private readonly Action _exit;
    private readonly Label _subtitle;
    private readonly Label _status;
    private readonly Label _clicks;
    private readonly Label _hint;
    private readonly RoundedButton[] _modes;
    private readonly PrimaryButton _toggleButton;
    private readonly RoundedButton _settingsButton;
    private readonly RoundedButton _exitButton;
    private readonly StatusPill _statusPill;
    private TraySnapshot _snapshot = new(ClickMode.Speed, false, false, 0, string.Empty);
    private Point _anchor;
    private bool _localToggleDown;

    public TrayMenuForm(Action toggle, Action<ClickMode> selectMode, Action settings, Action exit)
    {
        SuspendLayout();
        _toggle = toggle;
        _selectMode = selectMode;
        _settings = settings;
        _exit = exit;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(356, 328);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9F);
        Text = "Bench Auto Clicker";
        Icon = BrandAssets.LoadIcon();

        var layout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Window,
            Padding = new Padding(16),
            Margin = Padding.Empty,
            RowCount = 6,
            ColumnCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        Controls.Add(layout);

        var header = new BufferedPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Theme.Window };
        var logo = new LogoBadge(BrandAssets.LoadBitmap()) { Location = new Point(0, 5), Size = new Size(32, 32) };
        var title = Label("Bench Auto Clicker", 11.6F, FontStyle.Bold);
        title.SetBounds(43, 2, 274, 23);
        _subtitle = Label(string.Empty, 8F, FontStyle.Regular, Theme.Muted);
        _subtitle.SetBounds(43, 27, 274, 20);
        header.Controls.AddRange([logo, title, _subtitle]);
        layout.Controls.Add(header, 0, 0);

        var modes = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Window,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        modes.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _modes = new RoundedButton[3];
        for (var index = 0; index < _modes.Length; index++)
        {
            var mode = (ClickMode)index;
            modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
            var button = new RoundedButton
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, index < 2 ? 6 : 0, 0),
                Font = new Font("Segoe UI", 8.4F, FontStyle.Bold),
                FillColor = Theme.Card,
                HoverColor = Theme.CardHover
            };
            button.Click += (_, _) => _selectMode(mode);
            _modes[index] = button;
            modes.Controls.Add(button, index, 0);
        }
        layout.Controls.Add(modes, 0, 1);

        var summary = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 10, 0, 10),
            Padding = new Padding(10)
        };
        var summaryLayout = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Theme.Card
        };
        summary.Controls.Add(summaryLayout);
        var metrics = new BufferedPanel
        {
            BackColor = Theme.Card,
            Margin = Padding.Empty
        };
        _status = Label(string.Empty, 9F, FontStyle.Bold);
        _clicks = Label(string.Empty, 8F, FontStyle.Regular, Theme.Muted);
        metrics.Controls.AddRange([_status, _clicks]);
        _statusPill = new StatusPill { Margin = Padding.Empty };
        summaryLayout.Controls.AddRange([metrics, _statusPill]);
        summaryLayout.Layout += (_, _) =>
        {
            var pillWidth = LogicalToDeviceUnits(92);
            var pillHeight = LogicalToDeviceUnits(26);
            var textWidth = Math.Max(1, summaryLayout.ClientSize.Width - pillWidth - LogicalToDeviceUnits(8));
            metrics.SetBounds(0, 0, textWidth, summaryLayout.ClientSize.Height);
            var split = metrics.Height / 2;
            _status.SetBounds(0, 0, textWidth, split);
            _clicks.SetBounds(0, split, textWidth, metrics.Height - split);
            _statusPill.SetBounds(summaryLayout.ClientSize.Width - pillWidth,
                Math.Max(0, (summaryLayout.ClientSize.Height - pillHeight) / 2), pillWidth, pillHeight);
        };
        layout.Controls.Add(summary, 0, 2);

        _toggleButton = new PrimaryButton { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _toggleButton.Click += (_, _) => _toggle();
        layout.Controls.Add(_toggleButton, 0, 3);
        _hint = Label(string.Empty, 7.5F, FontStyle.Regular, Theme.Muted);
        _hint.Dock = DockStyle.Fill;
        _hint.TextAlign = ContentAlignment.MiddleCenter;
        layout.Controls.Add(_hint, 0, 4);

        var actions = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Window,
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 1
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _settingsButton = new RoundedButton
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 6, 0),
            Font = new Font("Segoe UI", 8.4F),
            FillColor = Theme.Card,
            HoverColor = Theme.CardHover,
            IconGlyph = "\uE713"
        };
        _exitButton = new RoundedButton
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Font = new Font("Segoe UI", 8.4F),
            FillColor = Theme.Card,
            HoverColor = Theme.CardHover
        };
        _settingsButton.Click += (_, _) => _settings();
        _exitButton.Click += (_, _) => _exit();
        actions.Controls.Add(_settingsButton, 0, 0);
        actions.Controls.Add(_exitButton, 1, 0);
        layout.Controls.Add(actions, 0, 5);
        AcceptButton = _toggleButton;
        RefreshAppearance();
        ResumeLayout(true);
    }

    public void ShowNear(Point anchor)
    {
        _anchor = anchor;
        Location = PositionNear(anchor, Size, Screen.FromPoint(anchor).WorkingArea);
        Show();
        // Moving from a differently scaled monitor may resize the window synchronously.
        Location = PositionNear(anchor, Size, Screen.FromPoint(anchor).WorkingArea);
        Activate();
    }

    internal static Point PositionNear(Point anchor, Size popupSize, Rectangle workingArea)
    {
        const int gap = 8;
        var left = anchor.X - popupSize.Width + 20;
        var top = anchor.Y - popupSize.Height - gap;
        if (anchor.X < workingArea.Left)
        {
            left = workingArea.Left + gap;
            top = anchor.Y - popupSize.Height / 2;
        }
        else if (anchor.X >= workingArea.Right)
        {
            left = workingArea.Right - popupSize.Width - gap;
            top = anchor.Y - popupSize.Height / 2;
        }
        else if (anchor.Y < workingArea.Top)
        {
            top = workingArea.Top + gap;
        }
        else if (anchor.Y >= workingArea.Bottom)
        {
            top = workingArea.Bottom - popupSize.Height - gap;
        }
        else if (top < workingArea.Top)
        {
            top = anchor.Y + gap;
        }
        return new Point(
            Math.Clamp(left, workingArea.Left + gap, Math.Max(workingArea.Left + gap, workingArea.Right - popupSize.Width - gap)),
            Math.Clamp(top, workingArea.Top + gap, Math.Max(workingArea.Top + gap, workingArea.Bottom - popupSize.Height - gap)));
    }

    public void UpdateSnapshot(TraySnapshot snapshot)
    {
        _snapshot = snapshot;
        for (var index = 0; index < _modes.Length; index++)
        {
            _modes[index].Text = TrayController.ModeCaption((ClickMode)index);
            _modes[index].Selected = index == (int)snapshot.Mode;
            _modes[index].Enabled = !snapshot.IsBusy;
        }
        _status.Text = snapshot.StatusText;
        _clicks.Text = AppStrings.T($"{snapshot.Clicks:N0} кликов за сессию", $"{snapshot.Clicks:N0} session clicks");
        _statusPill.State = snapshot.IsCountingDown ? AppRunState.Countdown : snapshot.IsRunning ? AppRunState.Running : AppRunState.Stopped;
        _toggleButton.HotKeyHint = snapshot.Shortcuts.Toggle;
        _toggleButton.ApplyState(snapshot.IsBusy);
        var toggleHint = snapshot.Shortcuts.Toggle + (snapshot.GlobalToggleAvailable ? string.Empty : AppStrings.T(" в меню", " in menu"));
        _hint.Text = AppStrings.T($"{toggleHint} · {snapshot.EmergencyHotKey} стоп · Esc закрыть",
            $"{toggleHint} · {snapshot.EmergencyHotKey} stop · Esc close");
    }

    public void RefreshAppearance()
    {
        Theme.ApplyTo(this);
        _subtitle.Text = AppStrings.T("Быстрое управление", "Quick controls");
        _settingsButton.Text = AppStrings.T("Открыть настройки", "Open settings");
        _exitButton.Text = AppStrings.T("Выход", "Exit");
        UpdateSnapshot(_snapshot);
        ApplyFrame();
        Invalidate(true);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        _localToggleDown = false;
        base.OnDeactivate(e);
        Hide();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Hide();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        else if (!_snapshot.GlobalToggleAvailable && HotkeyGesture.TryParse(_snapshot.Shortcuts.Toggle, out var gesture) && gesture.Matches(e.KeyData))
        {
            if (!_localToggleDown)
            {
                _localToggleDown = true;
                _toggle();
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (HotkeyGesture.TryParse(_snapshot.Shortcuts.Toggle, out var gesture) && e.KeyCode == gesture.Key)
        {
            _localToggleDown = false;
        }
        base.OnKeyUp(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Location = PositionNear(_anchor, Size, Screen.FromPoint(_anchor).WorkingArea);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyFrame();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
    }

    private void ApplyFrame()
    {
        if (!IsHandleCreated)
        {
            return;
        }
        NativeInterop.ApplyWindowFrame(Handle, Theme.IsDark && !Theme.HighContrast);
    }

    private static Label Label(string text, float size, FontStyle style, Color? color = null) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", size, style),
        ForeColor = color ?? Theme.Text,
        BackColor = Color.Transparent,
        Margin = Padding.Empty,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        UseMnemonic = false
    };
}
