namespace BenchAutoClicker;

internal sealed record AppPreferences(string Language, bool LaunchAtLogin, bool TrayMode, HotkeySettings? Hotkeys = null);

internal sealed class PreferencesForm : BufferedForm
{
    private readonly SelectBox _language;
    private readonly ToggleSwitch _startup;
    private readonly ToggleSwitch _tray;
    private readonly RoundedButton _applyButton;
    private readonly HotkeyBox[] _hotkeyBoxes;
    private readonly Label _error;
    private readonly Func<HotkeySettings, string?>? _validateHotkeys;

    public AppPreferences Result { get; private set; }

    public PreferencesForm(AppPreferences preferences, Func<HotkeySettings, string?>? validateHotkeys = null)
    {
        SuspendLayout();
        Result = preferences;
        _validateHotkeys = validateHotkeys;
        Text = AppStrings.T("Настройки приложения", "Application settings");
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 550);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Icon = BrandAssets.LoadIcon();
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9F);

        var layout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(16), Margin = Padding.Empty,
            ColumnCount = 1, RowCount = 7, BackColor = Theme.Window
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 28F, 60F, 100F, 210F, 30F, 36F })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        var title = Label(AppStrings.T("Под себя", "Make it yours"), 15F, FontStyle.Bold, Theme.Text);
        title.Dock = DockStyle.Fill;
        layout.Controls.Add(title, 0, 0);

        _language = new SelectBox([
            AppStrings.T("Системный язык", "System language"), "English", "Русский"
        ])
        {
            Dock = DockStyle.Fill,
            SelectedIndex = preferences.Language switch { "en" => 1, "ru" => 2, _ => 0 },
            AccessibleName = AppStrings.T("Язык интерфейса", "Interface language")
        };
        var languageCard = Card();
        var languageLayout = Grid(2, 1);
        languageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        languageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        languageLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var languageLabel = Label(AppStrings.T("Язык интерфейса", "Interface language"), 9F, FontStyle.Bold, Theme.Text);
        languageLabel.Dock = DockStyle.Fill;
        languageLayout.Controls.Add(languageLabel, 0, 0);
        languageLayout.Controls.Add(new InputSurface(_language) { Dock = DockStyle.Fill, Margin = Padding.Empty }, 1, 0);
        languageCard.Controls.Add(languageLayout);
        layout.Controls.Add(languageCard, 0, 1);

        _startup = new ToggleSwitch
        {
            Text = AppStrings.T("При входе в Windows", "When signing in"),
            Checked = preferences.LaunchAtLogin,
            AccessibleName = AppStrings.T("Автозапуск приложения", "Launch at login")
        };
        _tray = new ToggleSwitch
        {
            Text = AppStrings.T("Работать из трея", "Use the notification area"),
            Checked = preferences.TrayMode,
            AccessibleName = AppStrings.T("Компактное меню в области уведомлений", "Compact notification-area menu")
        };
        var options = Grid(2, 1, Theme.Window);
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var startupCard = OptionCard(AppStrings.T("Автозапуск", "Launch at login"),
            AppStrings.T("Открывает приложение без автокликов.", "Opens the app without automatic clicks."), _startup);
        startupCard.Margin = new Padding(0, 0, 4, 8);
        var trayCard = OptionCard(AppStrings.T("Меню возле часов", "Menu next to the clock"),
            AppStrings.T("Основное окно — через меню значка.", "Open the main window from the icon."), _tray);
        trayCard.Margin = new Padding(4, 0, 0, 8);
        options.Controls.Add(startupCard, 0, 0);
        options.Controls.Add(trayCard, 1, 0);
        layout.Controls.Add(options, 0, 2);

        var hotkeyCard = Card();
        var hotkeyLayout = Grid(1, 3);
        hotkeyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hotkeyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        hotkeyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        hotkeyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        var hotkeyHeader = Grid(2, 1);
        hotkeyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hotkeyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        hotkeyHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var hotkeyTitle = Label(AppStrings.T("Горячие клавиши", "Keyboard shortcuts"), 10F, FontStyle.Bold, Theme.Text);
        hotkeyTitle.Dock = DockStyle.Fill;
        var reset = new RoundedButton
        {
            Text = AppStrings.T("По умолчанию", "Reset defaults"), Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 2), Font = new Font("Segoe UI", 8F),
            FillColor = Theme.Input, HoverColor = Theme.CardHover, BorderColor = Theme.Border
        };
        hotkeyHeader.Controls.Add(hotkeyTitle, 0, 0);
        hotkeyHeader.Controls.Add(reset, 1, 0);
        hotkeyLayout.Controls.Add(hotkeyHeader, 0, 0);

        var hotkeys = (preferences.Hotkeys ?? HotkeySettings.Defaults).Normalize();
        _hotkeyBoxes = [
            new HotkeyBox { Value = hotkeys.Toggle }, new HotkeyBox { Value = hotkeys.Speed },
            new HotkeyBox { Value = hotkeys.Random }, new HotkeyBox { Value = hotkeys.Burst }
        ];
        var labels = new[] {
            AppStrings.T("Запуск / стоп", "Start / stop"), AppStrings.T("Режим «Скорость»", "Speed mode"),
            AppStrings.T("Режим «Случайно»", "Random mode"), AppStrings.T("Режим «Серии»", "Burst mode")
        };
        var shortcuts = Grid(2, 2);
        shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        shortcuts.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        shortcuts.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        for (var index = 0; index < _hotkeyBoxes.Length; index++)
        {
            var field = Grid(1, 2);
            field.Margin = new Padding(index % 2 == 0 ? 0 : 5, 2, index % 2 == 0 ? 5 : 0, 4);
            field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            field.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));
            field.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var label = Label(labels[index], 8.3F, FontStyle.Regular, Theme.Muted);
            label.Dock = DockStyle.Fill;
            var box = _hotkeyBoxes[index];
            box.Dock = DockStyle.Fill;
            box.Margin = Padding.Empty;
            box.AccessibleName = labels[index];
            field.Controls.Add(label, 0, 0);
            field.Controls.Add(box, 0, 1);
            shortcuts.Controls.Add(field, index % 2, index / 2);
        }
        hotkeyLayout.Controls.Add(shortcuts, 0, 1);
        var hint = Label(AppStrings.T(
            "Нажмите поле и сочетание. Esc — отмена.\nF7 — точка · Ctrl+Shift+F6 — аварийный стоп.",
            "Click a field and press a shortcut. Esc cancels.\nF7: capture point · Ctrl+Shift+F6: emergency stop."),
            7.7F, FontStyle.Regular, Theme.Muted);
        hint.Dock = DockStyle.Fill;
        hotkeyLayout.Controls.Add(hint, 0, 2);
        hotkeyCard.Controls.Add(hotkeyLayout);
        layout.Controls.Add(hotkeyCard, 0, 3);

        var themeText = Label(AppStrings.T(
            "Тема и цвет акцента автоматически подстраиваются под Windows.",
            "Theme and accent color automatically follow Windows."), 8F, FontStyle.Regular, Theme.Muted);
        themeText.Dock = DockStyle.Fill;
        layout.Controls.Add(themeText, 0, 4);
        _error = Label(string.Empty, 8F, FontStyle.Regular, Theme.Red);
        _error.Dock = DockStyle.Fill;
        layout.Controls.Add(_error, 0, 5);

        foreach (var box in _hotkeyBoxes)
        {
            box.ValueChanged += (_, _) => ValidateCurrentHotkeys();
            box.CaptureStateChanged += (_, _) =>
            {
                if (box.CaptureError.Length > 0) _error.Text = box.CaptureError;
                else ValidateCurrentHotkeys();
            };
        }
        reset.Click += (_, _) =>
        {
            var defaults = HotkeySettings.Defaults;
            foreach (var box in _hotkeyBoxes) box.CancelCapture();
            _hotkeyBoxes[0].Value = defaults.Toggle;
            _hotkeyBoxes[1].Value = defaults.Speed;
            _hotkeyBoxes[2].Value = defaults.Random;
            _hotkeyBoxes[3].Value = defaults.Burst;
            ValidateCurrentHotkeys();
        };

        var actions = Grid(2, 1, Theme.Window);
        actions.Padding = new Padding(0, 6, 0, 0);
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var cancel = new RoundedButton
        {
            Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0), Font = new Font("Segoe UI", 9F),
            FillColor = Theme.Card, HoverColor = Theme.CardHover,
            Text = AppStrings.T("Отмена", "Cancel"), DialogResult = DialogResult.Cancel
        };
        _applyButton = new RoundedButton
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty, Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            FillColor = Theme.Accent, HoverColor = Theme.AccentHover, ForeColor = Theme.OnAccent,
            BorderColor = Theme.Accent, Text = AppStrings.T("Применить", "Apply")
        };
        _applyButton.Click += (_, _) => Apply();
        actions.Controls.Add(cancel, 0, 0);
        actions.Controls.Add(_applyButton, 1, 0);
        layout.Controls.Add(actions, 0, 6);
        AcceptButton = _applyButton;
        CancelButton = cancel;
        RefreshAppearance();
        ResumeLayout(true);
    }

    private HotkeySettings CurrentHotkeys() => new(_hotkeyBoxes[0].Value, _hotkeyBoxes[1].Value,
        _hotkeyBoxes[2].Value, _hotkeyBoxes[3].Value);

    private void ValidateCurrentHotkeys()
    {
        CurrentHotkeys().TryValidate(out _, out var error);
        _error.Text = error;
    }

    private void Apply()
    {
        if (!CurrentHotkeys().TryValidate(out var hotkeys, out var error))
        {
            _error.Text = error;
            return;
        }
        var registrationError = _validateHotkeys?.Invoke(hotkeys);
        if (!string.IsNullOrEmpty(registrationError))
        {
            _error.Text = registrationError;
            return;
        }
        Result = new AppPreferences(_language.SelectedIndex switch { 1 => "en", 2 => "ru", _ => "system" },
            _startup.Checked, _tray.Checked, hotkeys);
        DialogResult = DialogResult.OK;
        Close();
    }

    public void RefreshAppearance()
    {
        Theme.ApplyTo(this);
        _applyButton.FillColor = Theme.Accent;
        _applyButton.HoverColor = Theme.AccentHover;
        _applyButton.BorderColor = Theme.Accent;
        _applyButton.ForeColor = Theme.OnAccent;
        if (IsHandleCreated) NativeInterop.ApplyWindowFrame(Handle, Theme.IsDark && !Theme.HighContrast);
        Invalidate(true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeInterop.ApplyWindowFrame(Handle, Theme.IsDark && !Theme.HighContrast);
    }

    private static BufferedTableLayoutPanel Grid(int columns, int rows, Color? background = null) => new()
    {
        Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty,
        ColumnCount = columns, RowCount = rows, BackColor = background ?? Theme.Card
    };

    private static CardPanel Card() => new()
    {
        Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(12), BackColor = Theme.Card
    };

    private static CardPanel OptionCard(string title, string description, ToggleSwitch option)
    {
        var card = Card();
        var layout = Grid(1, 3);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 19));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = Label(title, 9F, FontStyle.Bold, Theme.Text);
        var detail = Label(description, 7.7F, FontStyle.Regular, Theme.Muted);
        heading.Dock = DockStyle.Fill;
        detail.Dock = DockStyle.Fill;
        option.Dock = DockStyle.Fill;
        option.BackColor = Theme.Card;
        option.Margin = Padding.Empty;
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(detail, 0, 1);
        layout.Controls.Add(option, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private static Label Label(string text, float size, FontStyle style, Color color) => new()
    {
        Text = text, Font = new Font("Segoe UI", size, style), ForeColor = color,
        BackColor = Color.Transparent, AutoEllipsis = true, UseMnemonic = false,
        Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft
    };
}
