using Microsoft.Win32;
using System.Drawing;

namespace BenchAutoClicker;

internal sealed partial class MainForm : BufferedForm
{
    private enum LayoutProfile
    {
        Wide,
        Compact,
        Phone
    }

    private const int HotKeyToggle = 1;
    private const int HotKeyCapture = 2;
    private const int HotKeyEmergency = 3;
    private const uint ModNoRepeat = 0x4000;

    private readonly ClickEngine _engine;
    private readonly bool _persistSettings;
    private readonly UserPreferences _preferences;
    private readonly SemaphoreSlim _transition = new(1, 1);
    private readonly object _startGate = new();
    private readonly System.Windows.Forms.Timer _displayTimer = new() { Interval = 100 };

    private readonly NumberBox _speedCps;
    private readonly NumberBox _speedIntervalMs;
    private readonly NumberBox _randomMinimum;
    private readonly NumberBox _randomMaximum;
    private readonly NumberBox _burstCount;
    private readonly NumberBox _burstCps;
    private readonly NumberBox _burstPause;
    private readonly NumberBox _jitterRadius;
    private readonly NumberBox _startDelay;
    private readonly NumberBox _maxClicks;
    private readonly NumberBox _maxDuration;
    private readonly SelectBox _mouseAction;
    private readonly SelectBox _targetMode;
    private readonly ToggleSwitch _alwaysOnTop;
    private readonly ToolTip _toolTip = new();

    private RoundedButton[] _modeButtons = [];
    private Panel _contentPanel = null!;
    private Panel _rootLayout = null!;
    private Control _headerSection = null!;
    private Control _modeStripSection = null!;
    private Control _modeCardSection = null!;
    private Control _commonCardSection = null!;
    private Control _footerCardSection = null!;
    private Panel _modeHost = null!;
    private Label _modeTitle = null!;
    private Label _modeSubtitle = null!;
    private Label _sessionCounter = null!;
    private Label _nextEventLabel = null!;
    private Label _helperLabel = null!;
    private Label _hotKeyHelpLabel = null!;
    private Label _brandTitle = null!;
    private TableLayoutPanel _brandLayout = null!;
    private Label _sessionTime = null!;
    private RoundedButton[] _presetButtons = [];
    private SessionProgressBar _sessionProgress = null!;
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 600 };
    private string? _completionMessage;
    private bool _sessionActive;
    private int _runGeneration;
    private ClickConfiguration? _sessionConfiguration;
    private bool _engineStartedForSession;
    private bool _limitCompleted;
    private StatusPill _statusPill = null!;
    private PrimaryButton _startButton = null!;
    private RoundedButton _captureButton = null!;
    private RoundedButton _resetPointButton = null!;
    private FieldGrid _commonFieldGrid = null!;
    private TableLayoutPanel _footerLayout = null!;
    private Control _footerMetric = null!;
    private Control _footerHelper = null!;
    private FieldGrid[] _modeViews = [];

    private ClickMode _selectedMode;
    private Point? _fixedPoint;
    private CancellationTokenSource? _countdownCancellation;
    private DateTimeOffset? _nextEventAt;
    private string _nextEventCaption = string.Empty;
    private long _sessionClicks;
    private long _blockedClicks;
    private volatile bool _closing;
    private bool _toggleHotKeyRegistered;
    private bool _captureHotKeyRegistered;
    private bool _emergencyHotKeyRegistered;
    private string _emergencyHotKeyText = "Ctrl + Shift + F6";
    private bool _syncingSpeedInputs;
    private bool _layoutRefreshQueued;
    private bool _applyingResponsiveLayout;
    private LayoutProfile? _layoutProfile;
    private int _appliedLayoutDpi;
    private int _countdownRemaining;

    public MainForm() : this(null, true)
    {
    }

    internal MainForm(ClickEngine? engine, bool persistSettings)
    {
        _engine = engine ?? new ClickEngine();
        _persistSettings = persistSettings;
        _preferences = persistSettings ? SettingsStore.Load() : new UserPreferences();
        if (persistSettings)
        {
            Theme.RefreshFromWindows();
        }
        AppStrings.SetLanguage(_preferences.Language);
        _hotkeys = new HotkeySettings(_preferences.ToggleHotkey, _preferences.SpeedHotkey,
            _preferences.RandomHotkey, _preferences.BurstHotkey).Normalize();
        _speedCps = CreateNumber(0.2m, 50, 5, 0.2m, 1, " CPS");
        _speedIntervalMs = CreateNumber(20, 5000, 200, 10, 0, AppStrings.T(" мс"));
        _randomMinimum = CreateNumber(0.1m, 3600, 1, 0.5m, 1, AppStrings.T(" с"));
        _randomMaximum = CreateNumber(0.1m, 3600, 15, 0.5m, 1, AppStrings.T(" с"));
        _burstCount = CreateNumber(1, 500, 5, 1, 0, AppStrings.T(" кликов"));
        _burstCps = CreateNumber(0.2m, 50, 10, 0.5m, 1, " CPS");
        _burstPause = CreateNumber(0.1m, 3600, 3, 0.5m, 1, AppStrings.T(" с"));
        _jitterRadius = CreateNumber(0, 100, 0, 1, 0, " px");
        _startDelay = CreateNumber(0, 10, 3, 1, 0, AppStrings.T(" с"));
        _maxClicks = CreateNumber(0, 1_000_000_000, 0, 100, 0, "");
        _maxDuration = CreateNumber(0, 86400, 0, 10, 0, AppStrings.T(" с"));
        _mouseAction = CreateComboBox([AppStrings.T("Левая кнопка"), AppStrings.T("Правая кнопка"), AppStrings.T("Средняя кнопка"), AppStrings.T("Двойной левый")]);
        _targetMode = CreateComboBox([AppStrings.T("За курсором"), AppStrings.T("Фиксированная")]);
        _alwaysOnTop = new ToggleSwitch
        {
            Text = AppStrings.T("Поверх окон"),
            ForeColor = Theme.Text,
            Padding = Padding.Empty
        };

        BuildWindow();
        ApplyPreferences();
        WireEvents();
        ConfigureExperience();
    }

    internal void SetLanguage(string language)
    {
        if (_closing)
        {
            return;
        }
        CommitNumberInputs();
        AppStrings.SetLanguage(language);
        _preferences.Language = AppStrings.Language;
        AppStrings.Refresh(this);
        foreach (var view in _modeViews)
        {
            AppStrings.Refresh(view);
        }
        _mouseAction.ReplaceItems([AppStrings.T("Левая кнопка"), AppStrings.T("Правая кнопка"),
            AppStrings.T("Средняя кнопка"), AppStrings.T("Двойной левый")]);
        _targetMode.ReplaceItems([AppStrings.T("За курсором"), AppStrings.T("Фиксированная")]);
        _alwaysOnTop.Text = AppStrings.T("Поверх окон");
        _resetPointButton.AccessibleName = AppStrings.T("Сбросить точку и использовать центр экрана");
        if (_completionMessage is not null)
        {
            _completionMessage = AppStrings.T(_completionMessage);
        }
        foreach (var number in new[] { _speedCps, _speedIntervalMs, _randomMinimum, _randomMaximum,
                     _burstCount, _burstCps, _burstPause, _jitterRadius, _startDelay, _maxClicks, _maxDuration })
        {
            number.Value = number.Value;
        }
        SelectMode(_selectedMode);
        UpdateHotKeyUi();
        SetRunState(_statusPill.State);
        if (_statusPill.State == AppRunState.Countdown)
        {
            _nextEventLabel.Text = AppStrings.Format("Старт через {0} с", "Starting in {0} s", _countdownRemaining);
            _helperLabel.Text = AppStrings.T("Переключитесь в нужное окно");
        }
        else if (_sessionActive)
        {
            _helperLabel.Text = BuildSessionHint();
            if (_selectedMode == ClickMode.Speed)
            {
                _nextEventLabel.Text = AppStrings.Format("Интервал: {0:0} мс", "Interval: {0:0} ms", _speedIntervalMs.Value);
            }
        }
        else if (_engineStartedForSession)
        {
            _nextEventLabel.Text = _completionMessage ?? AppStrings.T("Сессия завершена");
            _helperLabel.Text = AppStrings.Format("{0:N0} кликов · {1:mm\\:ss}", "{0:N0} clicks · {1:mm\\:ss}",
                _sessionClicks, _engine.Elapsed);
        }
        else if (!_showingHotkeyWarning)
        {
            _helperLabel.Text = AppStrings.T($"{_hotkeys.Toggle} — запуск · F7 — точка", $"{_hotkeys.Toggle} — start · F7 — point");
        }
        RefreshSession();
        RefreshShell();
        SavePreferences();
        if (IsHandleCreated)
        {
            ApplyResponsiveLayout();
        }
        Invalidate(true);
    }

    private void BuildWindow()
    {
        SuspendLayout();
        Text = "Bench Auto Clicker 2.2.0";
        Icon = BrandAssets.LoadIcon();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(760, 620);
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9.25F);
        KeyPreview = true;

        Controls.Add(BuildContent());
        ResumeLayout(true);
    }

    private Control BuildContent()
    {
        _contentPanel = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Window,
            Padding = new Padding(16, 14, 16, 14)
        };
        _rootLayout = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Theme.Window
        };
        _contentPanel.Controls.Add(_rootLayout);

        _headerSection = BuildHeader();
        _modeStripSection = BuildModeStrip();
        _modeCardSection = BuildModeCard();
        _commonCardSection = BuildCommonCard();
        _footerCardSection = BuildFooterCard();
        foreach (var section in new[] { _headerSection, _modeStripSection, _modeCardSection, _commonCardSection, _footerCardSection })
        {
            section.Dock = DockStyle.None;
            section.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        }
        _rootLayout.Controls.AddRange([
            _headerSection,
            _modeStripSection,
            _modeCardSection,
            _commonCardSection,
            _footerCardSection]);
        return _contentPanel;
    }

    private Control BuildHeader()
    {
        var panel = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Window,
            ColumnCount = 4,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var logo = new LogoBadge(BrandAssets.LoadBitmap())
        {
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            Size = new Size(36, 36)
        };
        panel.Controls.Add(logo, 0, 0);

        var brand = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Window,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(8, 4, 4, 4)
        };
        _brandLayout = brand;
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _brandTitle = CreateLabel("Bench Auto Clicker", 15F, FontStyle.Bold, Theme.Text);
        _brandTitle.Dock = DockStyle.Fill;
        _brandTitle.TextAlign = ContentAlignment.MiddleLeft;
        _hotKeyHelpLabel = CreateLabel(AppStrings.T("АВТОМАТИЗАЦИЯ ПОД КОНТРОЛЕМ · v2.0"), 7.4F, FontStyle.Regular, Theme.Muted);
        _hotKeyHelpLabel.Dock = DockStyle.Fill;
        brand.Controls.Add(_brandTitle, 0, 0);
        brand.Controls.Add(_hotKeyHelpLabel, 0, 1);
        panel.Controls.Add(brand, 1, 0);

        _statusPill = new StatusPill
        {
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty
        };
        panel.Controls.Add(_statusPill, 2, 0);
        _settingsButton = new RoundedButton
        {
            Text = "\uE713",
            Font = new Font("Segoe MDL2 Assets", 11F),
            Size = new Size(30, 30),
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
            FillColor = Theme.Window,
            HoverColor = Theme.CardHover,
            AccessibleName = AppStrings.T("Настройки приложения", "Application settings")
        };
        AppStrings.SetToolTip(_toolTip, _settingsButton, AppStrings.T("Настройки приложения", "Application settings"));
        _settingsButton.Click += (_, _) => ShowApplicationPreferences();
        panel.Controls.Add(_settingsButton, 3, 0);
        return panel;
    }

    private Control BuildModeStrip()
    {
        var strip = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            BackColor = Theme.Window,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334F));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 8));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
        strip.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _modeButtons =
        [
            CreateModeButton(AppStrings.T("Скорость"), ClickMode.Speed),
            CreateModeButton(AppStrings.T("Случайно"), ClickMode.RandomInterval),
            CreateModeButton(AppStrings.T("Серии"), ClickMode.Burst)
        ];
        for (var index = 0; index < _modeButtons.Length; index++)
        {
            _modeButtons[index].Dock = DockStyle.Fill;
            _modeButtons[index].Margin = Padding.Empty;
            strip.Controls.Add(_modeButtons[index], index * 2, 0);
        }
        return strip;
    }

    private Control BuildModeCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(12, 10, 12, 10)
        };
        var layout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Theme.Card,
            ColumnCount = 1,
            RowCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        card.Controls.Add(layout);

        _modeTitle = CreateLabel(string.Empty, 10.25F, FontStyle.Bold, Theme.Text);
        _modeTitle.Dock = DockStyle.Fill;
        _modeTitle.Margin = Padding.Empty;
        _modeSubtitle = CreateLabel(string.Empty, 8.25F, FontStyle.Regular, Theme.Muted);
        _modeSubtitle.Dock = DockStyle.Fill;
        _modeSubtitle.Margin = Padding.Empty;

        _modeHost = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Theme.Card };
        layout.Controls.Add(_modeTitle, 0, 0);
        layout.Controls.Add(_modeSubtitle, 0, 1);
        layout.Controls.Add(_modeHost, 0, 2);
        layout.Controls.Add(BuildPresets(), 0, 3);
        _modeViews = [BuildSpeedView(), BuildRandomView(), BuildBurstView()];
        foreach (var view in _modeViews)
        {
            view.Dock = DockStyle.Fill;
            view.Visible = false;
            _modeHost.Controls.Add(view);
        }
        return card;
    }

    private FieldGrid BuildSpeedView()
    {
        return CreateFieldGrid(
            2,
            CreateField(AppStrings.T("Скорость, CPS"), _speedCps),
            CreateField(AppStrings.T("Интервал, мс"), _speedIntervalMs));
    }

    private Control BuildPresets()
    {
        var strip = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Card,
            Margin = Padding.Empty,
            Padding = new Padding(0, 4, 0, 0),
            ColumnCount = 3,
            RowCount = 1
        };
        strip.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _presetButtons = new RoundedButton[3];
        for (var index = 0; index < _presetButtons.Length; index++)
        {
            strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 3));
            var presetIndex = index;
            var button = new RoundedButton
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, index < 2 ? 6 : 0, 0),
                FillColor = Theme.Input,
                HoverColor = Theme.CardHover,
                Font = new Font("Segoe UI", 7.8F),
                Text = AppStrings.T("Пресет")
            };
            button.Click += (_, _) => ApplyPreset(presetIndex);
            _presetButtons[index] = button;
            strip.Controls.Add(button, index, 0);
        }
        return strip;
    }

    private void ApplyPreset(int preset)
    {
        if (IsBusy)
        {
            return;
        }
        switch (_selectedMode)
        {
            case ClickMode.Speed:
                _speedCps.Value = new decimal[] { 1, 5, 20 }[preset];
                break;
            case ClickMode.RandomInterval:
                _randomMinimum.Value = new decimal[] { 1, 3, 0.5m }[preset];
                _randomMaximum.Value = new decimal[] { 15, 8, 2 }[preset];
                break;
            case ClickMode.Burst:
                _burstCount.Value = new decimal[] { 3, 5, 10 }[preset];
                _burstCps.Value = new decimal[] { 5, 10, 20 }[preset];
                _burstPause.Value = new decimal[] { 5, 3, 1 }[preset];
                break;
        }
        RefreshPresets();
    }

    private void RefreshPresets()
    {
        var labels = _selectedMode switch
        {
            ClickMode.RandomInterval => new[] { AppStrings.T("1–15 с"), AppStrings.T("3–8 с"), AppStrings.T("0,5–2 с") },
            ClickMode.Burst => new[] { "3 × 5 CPS", "5 × 10 CPS", "10 × 20 CPS" },
            _ => new[] { AppStrings.T("1 CPS · Тихо"), AppStrings.T("5 CPS · Баланс"), AppStrings.T("20 CPS · Быстро") }
        };
        for (var index = 0; index < _presetButtons.Length; index++)
        {
            _presetButtons[index].Text = labels[index];
            _presetButtons[index].Selected = _selectedMode switch
            {
                ClickMode.Speed => Math.Abs(_speedIntervalMs.Value - new decimal[] { 1000, 200, 50 }[index]) < 0.01m,
                ClickMode.RandomInterval => _randomMinimum.Value == new decimal[] { 1, 3, 0.5m }[index] &&
                                            _randomMaximum.Value == new decimal[] { 15, 8, 2 }[index],
                _ => _burstCount.Value == new decimal[] { 3, 5, 10 }[index] &&
                     _burstCps.Value == new decimal[] { 5, 10, 20 }[index] &&
                     _burstPause.Value == new decimal[] { 5, 3, 1 }[index]
            };
        }
    }

    private void ConfigureExperience()
    {
        _speedCps.ShowProgressTrack = true;
        _speedIntervalMs.ShowProgressTrack = true;
        _speedIntervalMs.LogarithmicTrack = true;
        _jitterRadius.ShowProgressTrack = true;
        _startDelay.ShowProgressTrack = true;
        _maxClicks.ZeroText = "∞";
        _maxDuration.ZeroText = "∞";
        AppStrings.SetToolTip(_toolTip, _maxClicks, AppStrings.T("0 — без лимита. Двойной клик учитывается как два физических клика."));
        AppStrings.SetToolTip(_toolTip, _maxDuration, AppStrings.T("0 — без лимита. Время отсчитывается после задержки запуска."));
        AppStrings.SetToolTip(_toolTip, _speedIntervalMs, AppStrings.T("Точный интервал между кликами. Ввод с клавиатуры или перетаскивание дорожки."));
        AppStrings.SetToolTip(_toolTip, _captureButton, AppStrings.T("F7 сохраняет позицию курсора. Без точки используется центр экрана."));
        var glyphs = new[] { "\uE945", "\uE8B1", "\uE8FD" };
        for (var index = 0; index < _modeButtons.Length; index++)
        {
            _modeButtons[index].IconGlyph = glyphs[index];
        }
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            if (!IsBusy && !_closing)
            {
                SavePreferences();
            }
        };
        foreach (var number in new[] { _speedCps, _speedIntervalMs, _randomMinimum, _randomMaximum,
                     _burstCount, _burstCps, _burstPause, _jitterRadius, _startDelay, _maxClicks, _maxDuration })
        {
            number.ValueChanged += (_, _) => SettingsChanged();
        }
        _mouseAction.SelectedIndexChanged += (_, _) => SettingsChanged();
        _targetMode.SelectedIndexChanged += (_, _) => SettingsChanged();
        _alwaysOnTop.CheckedChanged += (_, _) => SettingsChanged();
        RefreshPresets();
    }

    private void SettingsChanged()
    {
        if (_closing)
        {
            return;
        }
        RefreshPresets();
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private FieldGrid BuildRandomView()
    {
        return CreateFieldGrid(
            2,
            CreateField(AppStrings.T("Минимум, сек"), _randomMinimum),
            CreateField(AppStrings.T("Максимум, сек"), _randomMaximum));
    }

    private FieldGrid BuildBurstView()
    {
        return CreateFieldGrid(
            3,
            CreateField(AppStrings.T("Кликов"), _burstCount),
            CreateField(AppStrings.T("Скорость, CPS"), _burstCps),
            CreateField(AppStrings.T("Пауза, сек"), _burstPause));
    }

    private Control BuildCommonCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(12, 10, 12, 10)
        };
        var layout = new BufferedPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Theme.Card
        };
        card.Controls.Add(layout);

        var title = CreateLabel(AppStrings.T("Настройки сессии"), 10.25F, FontStyle.Bold, Theme.Text);
        title.Dock = DockStyle.Top;
        title.Height = 26;

        _captureButton = new RoundedButton
        {
            Text = AppStrings.T("Центр экрана · F7"),
            Dock = DockStyle.Fill,
            FillColor = Theme.Input,
            HoverColor = Theme.CardHover
        };
        _resetPointButton = new RoundedButton
        {
            Text = "↺",
            Dock = DockStyle.Fill,
            Margin = new Padding(5, 0, 0, 0),
            FillColor = Theme.Input,
            HoverColor = Theme.CardHover,
            Font = new Font("Segoe UI Symbol", 11F, FontStyle.Bold),
            AccessibleName = AppStrings.T("Сбросить точку и использовать центр экрана")
        };
        AppStrings.SetToolTip(_toolTip, _resetPointButton, AppStrings.T("Сбросить точку — использовать центр экрана под курсором"));
        var pointActions = new PointActionsPanel(_captureButton, _resetPointButton);

        _commonFieldGrid = CreateFieldGrid(
            3,
            CreateField(AppStrings.T("Кнопка мыши"), _mouseAction),
            CreateField(AppStrings.T("Позиция клика"), _targetMode),
            CreateField(AppStrings.T("Цель · F7"), pointActions),
            CreateField(AppStrings.T("Разброс, px"), _jitterRadius),
            CreateField(AppStrings.T("Перед стартом, с"), _startDelay),
            CreateField(AppStrings.T("Лимит кликов"), _maxClicks),
            CreateField(AppStrings.T("Лимит времени, с"), _maxDuration),
            CreateField(AppStrings.T("Всегда на виду"), _alwaysOnTop));
        layout.Controls.Add(_commonFieldGrid);
        layout.Controls.Add(title);
        return card;
    }

    private Control BuildFooterCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(10)
        };
        _footerLayout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Theme.Card
        };
        _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
        _footerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        card.Controls.Add(_footerLayout);
        _sessionProgress = new SessionProgressBar
        {
            Dock = DockStyle.Bottom,
            Height = 3,
            Margin = Padding.Empty
        };
        card.Controls.Add(_sessionProgress);

        _footerMetric = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Theme.Card };
        var metricCaption = CreateLabel(AppStrings.T("КЛИКОВ"), 7F, FontStyle.Bold, Theme.Muted);
        metricCaption.Dock = DockStyle.Top;
        metricCaption.Height = 14;
        _sessionCounter = CreateLabel("0", 17F, FontStyle.Bold, Theme.Text);
        _sessionCounter.Dock = DockStyle.Top;
        _sessionCounter.Height = 27;
        _sessionTime = CreateLabel("00:00 · 0 CPS", 7F, FontStyle.Regular, Theme.Muted);
        _sessionTime.Dock = DockStyle.Fill;
        _footerMetric.Controls.AddRange([_sessionTime, _sessionCounter, metricCaption]);
        _footerLayout.Controls.Add(_footerMetric, 0, 0);

        _footerHelper = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(8, 0, 6, 0) };
        _nextEventLabel = CreateLabel(AppStrings.T("Всё готово"), 9F, FontStyle.Bold, Theme.Text);
        _nextEventLabel.Dock = DockStyle.Top;
        _nextEventLabel.Height = 20;
        _helperLabel = CreateLabel(AppStrings.T($"{_hotkeys.Toggle} — запуск · F7 — точка", $"{_hotkeys.Toggle} — start · F7 — point"), 7.8F, FontStyle.Regular, Theme.Muted);
        _helperLabel.Dock = DockStyle.Fill;
        _footerHelper.Controls.AddRange([_helperLabel, _nextEventLabel]);
        _footerLayout.Controls.Add(_footerHelper, 1, 0);

        _startButton = new PrimaryButton { Dock = DockStyle.Fill, Margin = new Padding(0) };
        _startButton.ApplyState(false);
        _footerLayout.Controls.Add(_startButton, 2, 0);
        return card;
    }

    private void WireEvents()
    {
        foreach (var modeButton in _modeButtons)
        {
            modeButton.Click += ModeButtonClicked;
        }

        _speedCps.ValueChanged += (_, _) => UpdateIntervalFromCps();
        _speedIntervalMs.ValueChanged += (_, _) => UpdateCpsFromInterval();
        _targetMode.SelectedIndexChanged += (_, _) => UpdateTargetUi();
        _alwaysOnTop.CheckedChanged += (_, _) => TopMost = _alwaysOnTop.Checked;
        _captureButton.Click += (_, _) => CapturePoint();
        _resetPointButton.Click += (_, _) => ResetPoint();
        _startButton.Click += async (_, _) => await ToggleRunningAsync();
        KeyDown += MainFormKeyDown;
        KeyUp += MainFormKeyUp;

        _engine.ClickCompleted += EngineClickCompleted;
        _engine.WaitScheduled += EngineWaitScheduled;
        _engine.Faulted += EngineFaulted;
        _engine.SessionCompleted += reason => PostRunToUi(() =>
        {
            _limitCompleted = true;
            _completionMessage = reason == SessionEndReason.ClickLimit
                ? AppStrings.T("Лимит кликов выполнен")
                : AppStrings.T("Лимит времени выполнен");
        });
        _engine.Stopped += EngineStopped;

        _displayTimer.Tick += (_, _) => RefreshSession();
        _displayTimer.Start();
        SystemEvents.PowerModeChanged += SystemPowerModeChanged;
        SystemEvents.SessionSwitch += SystemSessionSwitch;
    }

    private void ApplyPreferences()
    {
        _selectedMode = Enum.IsDefined(_preferences.Mode) ? _preferences.Mode : ClickMode.Speed;
        var scale = DeviceDpi / 96F;
        ClientSize = new Size(
            (int)Math.Ceiling(Math.Clamp(_preferences.WindowWidth, 340, 1200) * scale),
            (int)Math.Ceiling(Math.Clamp(_preferences.WindowHeight, 420, 1400) * scale));
        _fixedPoint = _preferences.HasFixedPoint ? new Point(_preferences.FixedX, _preferences.FixedY) : null;
        SetNumber(_speedCps, _preferences.ClicksPerSecond);
        if (_preferences.SpeedIntervalMilliseconds is { } savedInterval)
        {
            SetNumber(_speedIntervalMs, savedInterval);
            UpdateCpsFromInterval();
        }
        else
        {
            UpdateIntervalFromCps();
        }
        SetNumber(_randomMinimum, _preferences.RandomMinimumSeconds);
        SetNumber(_randomMaximum, _preferences.RandomMaximumSeconds);
        SetNumber(_burstCount, _preferences.BurstClickCount);
        SetNumber(_burstCps, _preferences.BurstClicksPerSecond);
        SetNumber(_burstPause, _preferences.BurstPauseSeconds);
        SetNumber(_jitterRadius, _preferences.JitterRadius);
        SetNumber(_startDelay, _preferences.StartDelaySeconds);
        SetNumber(_maxClicks, _preferences.MaxClicks);
        SetNumber(_maxDuration, double.IsFinite(_preferences.MaxDurationSeconds)
            ? (decimal)Math.Clamp(_preferences.MaxDurationSeconds, 0, 86400)
            : 0);
        _mouseAction.SelectedIndex = Math.Clamp((int)_preferences.MouseAction, 0, _mouseAction.Items.Count - 1);
        _targetMode.SelectedIndex = Math.Clamp((int)_preferences.TargetMode, 0, _targetMode.Items.Count - 1);
        _alwaysOnTop.Checked = _preferences.AlwaysOnTop;
        TopMost = _preferences.AlwaysOnTop;
        SelectMode(_selectedMode);
        UpdateTargetUi();
        SetRunState(AppRunState.Stopped);
    }

    private void SelectMode(ClickMode mode)
    {
        _selectedMode = mode;
        for (var index = 0; index < _modeButtons.Length; index++)
        {
            _modeButtons[index].Selected = index == (int)mode;
            _modeButtons[index].Invalidate();
        }

        (_modeTitle.Text, _modeSubtitle.Text) = mode switch
        {
            ClickMode.RandomInterval => (
                AppStrings.T("Случайный интервал"),
                AppStrings.T("Один клик после случайной паузы")),
            ClickMode.Burst => (
                AppStrings.T("Клики сериями"),
                AppStrings.T("Короткая серия → пауза → повтор")),
            _ => (
                AppStrings.T("Постоянная скорость"),
                AppStrings.T("Ровный темп от 0,2 до 50 кликов в секунду"))
        };

        _modeHost.SuspendLayout();
        try
        {
            for (var index = 0; index < _modeViews.Length; index++)
            {
                _modeViews[index].Visible = index == (int)mode;
            }
            _modeViews[(int)mode].BringToFront();
        }
        finally
        {
            _modeHost.ResumeLayout(true);
        }
        RefreshPresets();
        QueueLayoutRefresh();
    }

    private void ModeButtonClicked(object? sender, EventArgs eventArgs)
    {
        if (sender is RoundedButton button && button.Tag is ClickMode mode)
        {
            TrySelectMode(mode);
        }
    }

    private async Task ToggleRunningAsync()
    {
        if (_closing || _preferencesOpen || _hotkeysSuspended)
        {
            return;
        }

        var countdown = _countdownCancellation;
        if (countdown is not null)
        {
            countdown.Cancel();
            return;
        }

        await _transition.WaitAsync();
        try
        {
            if (_closing || _preferencesOpen || _hotkeysSuspended)
            {
                return;
            }

            if (IsBusy)
            {
                await StopAllAsync(AppStrings.T("Остановлено пользователем"));
            }
            else
            {
                await StartAsync();
            }
        }
        finally
        {
            _transition.Release();
        }
    }

    private async Task StartAsync()
    {
        var configuration = CreateConfiguration();
        if (configuration is null)
        {
            return;
        }

        SavePreferences();
        Interlocked.Increment(ref _runGeneration);
        _sessionConfiguration = configuration;
        _engineStartedForSession = false;
        _limitCompleted = false;
        _sessionClicks = 0;
        _blockedClicks = 0;
        _completionMessage = null;
        _sessionActive = false;
        _sessionProgress.Progress = 0;
        _sessionCounter.Text = "0";
        _sessionTime.Text = "00:00 · 0 CPS";
        _helperLabel.ForeColor = Theme.Muted;
        ToggleSettings(false);
        _startButton.ApplyState(true);

        var delaySeconds = (int)_startDelay.Value;
        _countdownCancellation = new CancellationTokenSource();
        var countdownToken = _countdownCancellation.Token;
        try
        {
            for (var remaining = delaySeconds; remaining > 0; remaining--)
            {
                _countdownRemaining = remaining;
                SetRunState(AppRunState.Countdown);
                _nextEventLabel.Text = AppStrings.Format("Старт через {0} с", "Starting in {0} s", remaining);
                _helperLabel.Text = AppStrings.T("Переключитесь в нужное окно");
                await Task.Delay(TimeSpan.FromSeconds(1), countdownToken);
            }

            lock (_startGate)
            {
                countdownToken.ThrowIfCancellationRequested();
                if (_closing)
                {
                    throw new OperationCanceledException();
                }
                if (!_engine.Start(configuration))
                {
                    throw new InvalidOperationException(AppStrings.T("Кликер уже работает."));
                }
                _countdownCancellation.Dispose();
                _countdownCancellation = null;
            }
            _nextEventAt = null;
            _nextEventCaption = string.Empty;

            _sessionActive = true;
            _engineStartedForSession = true;
            SetRunState(AppRunState.Running);
            if (_selectedMode == ClickMode.Speed)
            {
                _nextEventLabel.Text = AppStrings.Format("Интервал: {0:0} мс", "Interval: {0:0} ms", _speedIntervalMs.Value);
            }
            else
            {
                _nextEventLabel.Text = AppStrings.T("Планируется первый клик…");
            }
            _helperLabel.Text = BuildSessionHint();
        }
        catch (OperationCanceledException)
        {
            _countdownCancellation?.Dispose();
            _countdownCancellation = null;
            if (_closing)
            {
                return;
            }

            SetRunState(AppRunState.Stopped);
            ToggleSettings(true);
            _startButton.ApplyState(false);
            _nextEventLabel.Text = AppStrings.T("Запуск отменён");
            _helperLabel.Text = AppStrings.T("Защита от кликов по Bench Auto Clicker включена");
        }
        catch (Exception exception)
        {
            _countdownCancellation?.Dispose();
            _countdownCancellation = null;
            if (_closing)
            {
                return;
            }
            SetRunState(AppRunState.Stopped);
            ToggleSettings(true);
            _startButton.ApplyState(false);
            MessageBox.Show(exception.Message, "Bench Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task StopAllAsync(string message)
    {
        _completionMessage = message;
        var countdown = _countdownCancellation;
        _countdownCancellation = null;
        countdown?.Cancel();
        countdown?.Dispose();
        await _engine.StopAsync();
        if (_closing)
        {
            return;
        }

        _nextEventAt = null;
        _nextEventCaption = string.Empty;
        _sessionActive = false;
        RefreshSession();
        SetRunState(AppRunState.Stopped);
        ToggleSettings(true);
        _startButton.ApplyState(false);
        _nextEventLabel.Text = message;
        _helperLabel.Text = _blockedClicks > 0
            ? AppStrings.Format("Заблокировано кликов по Bench Auto Clicker: {0}", "Blocked clicks on Bench Auto Clicker: {0}", _blockedClicks)
            : AppStrings.T("Защита от кликов по Bench Auto Clicker включена");
    }

    private ClickConfiguration? CreateConfiguration()
    {
        CommitNumberInputs();
        if (!_emergencyHotKeyRegistered)
        {
            ShowValidation(AppStrings.T("Не удалось зарегистрировать глобальную клавишу аварийной остановки. Закройте программу, которая перехватывает Ctrl+Shift+F6 и Ctrl+Shift+F12, затем перезапустите Bench Auto Clicker."));
            return null;
        }

        if (_selectedMode == ClickMode.RandomInterval && _randomMinimum.Value > _randomMaximum.Value)
        {
            ShowValidation(AppStrings.T("Минимальная пауза не может быть больше максимальной."));
            return null;
        }

        var targetMode = (TargetMode)_targetMode.SelectedIndex;
        var resolvedPoint = _fixedPoint ?? Point.Empty;
        if (targetMode == TargetMode.FixedPoint && _fixedPoint is null)
        {
            try
            {
                var cursor = NativeInterop.GetCursorPosition();
                var screenBounds = Screen.FromPoint(cursor).Bounds;
                resolvedPoint = TargetMath.CenterOf(screenBounds);
            }
            catch (Exception exception)
            {
                ShowValidation(exception.Message);
                return null;
            }
        }

        if (targetMode == TargetMode.FixedPoint &&
            !Screen.AllScreens.Any(screen => screen.Bounds.Contains(resolvedPoint)))
        {
            ShowValidation(AppStrings.T("Сохранённая точка больше не находится на подключённом мониторе. Сохраните её заново клавишей F7."));
            return null;
        }

        if (targetMode == TargetMode.FixedPoint && Visible && Bounds.Contains(resolvedPoint))
        {
            ShowValidation(_fixedPoint is null
                ? AppStrings.T("Центр выбранного экрана находится под окном Bench Auto Clicker. Переместите окно в сторону или сохраните точку клавишей F7.")
                : AppStrings.T("Сохранённая точка находится внутри Bench Auto Clicker. Выберите точку в другом окне."));
            return null;
        }

        return new ClickConfiguration(
            _selectedMode,
            (MouseAction)_mouseAction.SelectedIndex,
            targetMode,
            resolvedPoint,
            (int)_jitterRadius.Value,
            (double)_speedIntervalMs.Value,
            (double)_randomMinimum.Value,
            (double)_randomMaximum.Value,
            (int)_burstCount.Value,
            (double)_burstCps.Value,
            (double)_burstPause.Value,
            Handle,
            (long)_maxClicks.Value,
            (double)_maxDuration.Value);
    }

    private void CapturePoint()
    {
        if (IsBusy || _closing)
        {
            return;
        }

        try
        {
            _fixedPoint = NativeInterop.GetCursorPosition();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Bench Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _targetMode.SelectedIndex = (int)TargetMode.FixedPoint;
        UpdateTargetUi();
        SettingsChanged();
        if (Visible && Bounds.Contains(_fixedPoint.Value))
        {
            _captureButton.Text = AppStrings.T("Выберите вне окна");
            _captureButton.ForeColor = Theme.Amber;
        }
    }

    private void UpdateTargetUi()
    {
        var fixedTarget = _targetMode.SelectedIndex == (int)TargetMode.FixedPoint;
        _captureButton.Selected = fixedTarget;
        _captureButton.Text = _fixedPoint is { } point
            ? $"{point.X}; {point.Y}"
            : AppStrings.T("Центр");
        AppStrings.SetToolTip(_toolTip, _captureButton, _fixedPoint is { } saved
            ? AppStrings.Format("Точка X {0}, Y {1}. F7 — выбрать заново.", "Point X {0}, Y {1}. F7 saves a new point.", saved.X, saved.Y)
            : AppStrings.T("Точка не задана: центр экрана под курсором. F7 — сохранить точку."));
        _captureButton.ForeColor = _fixedPoint is null ? Theme.AccentText : Theme.Green;
        _resetPointButton.Enabled = !IsBusy && _fixedPoint is not null;
    }

    private void ResetPoint()
    {
        if (IsBusy || _closing)
        {
            return;
        }

        _fixedPoint = null;
        _targetMode.SelectedIndex = (int)TargetMode.FixedPoint;
        UpdateTargetUi();
        _helperLabel.Text = AppStrings.T("Цель: центр экрана под курсором");
        _helperLabel.ForeColor = Theme.Muted;
        SettingsChanged();
    }

    private void UpdateIntervalFromCps()
    {
        if (_syncingSpeedInputs)
        {
            return;
        }

        _syncingSpeedInputs = true;
        var milliseconds = ScheduleMath.IntervalFromClicksPerSecond((double)_speedCps.Value).TotalMilliseconds;
        SetNumber(_speedIntervalMs, (decimal)Math.Round(milliseconds));
        _syncingSpeedInputs = false;
    }

    private void UpdateCpsFromInterval()
    {
        if (_syncingSpeedInputs)
        {
            return;
        }

        _syncingSpeedInputs = true;
        var cps = ScheduleMath.ClicksPerSecondFromInterval((double)_speedIntervalMs.Value);
        SetNumber(_speedCps, (decimal)Math.Round(cps, _speedCps.DecimalPlaces));
        _syncingSpeedInputs = false;
    }

    private void EngineClickCompleted(ClickResult result)
    {
        if (result.WasBlocked)
        {
            Interlocked.Increment(ref _blockedClicks);
        }
    }

    private void EngineWaitScheduled(DateTimeOffset at, string caption)
    {
        if (_closing || !IsHandleCreated)
        {
            return;
        }

        PostRunToUi(() =>
        {
            if (!_sessionActive)
            {
                return;
            }
            _nextEventAt = at;
            _nextEventCaption = caption;
            RefreshNextEvent();
        });
    }

    private void EngineFaulted(Exception exception)
    {
        if (_closing || !IsHandleCreated)
        {
            return;
        }

        PostRunToUi(() =>
        {
            _completionMessage = AppStrings.T("Остановлено: ошибка ввода");
            _nextEventLabel.Text = AppStrings.T("Работа остановлена из-за ошибки");
            MessageBox.Show(exception.Message, "Bench Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        });
    }

    private void EngineStopped()
    {
        if (_closing || !IsHandleCreated)
        {
            return;
        }

        PostRunToUi(() =>
        {
            _sessionActive = false;
            _nextEventAt = null;
            RefreshSession();
            SetRunState(AppRunState.Stopped);
            ToggleSettings(true);
            _startButton.ApplyState(false);
            _nextEventLabel.Text = _completionMessage ?? AppStrings.T("Сессия завершена");
            _helperLabel.Text = AppStrings.Format("{0:N0} кликов · {1:mm\\:ss}", "{0:N0} clicks · {1:mm\\:ss}", _sessionClicks, _engine.Elapsed);
        });
    }

    private string BuildSessionHint()
    {
        var clicks = _maxClicks.Value > 0
            ? AppStrings.Format("{0:N0} кликов", "{0:N0} clicks", _maxClicks.Value)
            : AppStrings.T("∞ кликов");
        var time = _maxDuration.Value > 0 ? AppStrings.Format(" · {0:0} с", " · {0:0} s", _maxDuration.Value) : string.Empty;
        return $"{clicks}{time} · {_hotkeys.Toggle} {AppStrings.T("стоп", "stop")}";
    }

    private void PostRunToUi(Action action)
    {
        var generation = Volatile.Read(ref _runGeneration);
        PostToUi(() =>
        {
            if (generation == Volatile.Read(ref _runGeneration))
            {
                action();
            }
        });
    }

    private void RefreshSession()
    {
        if (_statusPill.State == AppRunState.Countdown)
        {
            return;
        }
        if (_engineStartedForSession)
        {
            _sessionClicks = _engine.TotalClicks;
            var elapsed = _engine.Elapsed;
            var rate = elapsed.TotalSeconds > 0.5 ? _sessionClicks / elapsed.TotalSeconds : 0;
            _sessionCounter.Text = _sessionClicks.ToString("N0");
            _sessionTime.Text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00} · {rate:0.#} CPS";
            var maxClicks = _sessionConfiguration?.MaxClicks ?? 0;
            var maxDuration = _sessionConfiguration?.MaxDurationSeconds ?? 0;
            var clickProgress = maxClicks > 0 ? _sessionClicks / (double)maxClicks : 0;
            var timeProgress = maxDuration > 0 ? elapsed.TotalSeconds / maxDuration : 0;
            _sessionProgress.Progress = _limitCompleted ? 1 : Math.Clamp(Math.Max(clickProgress, timeProgress), 0, 1);
            if (_sessionActive && Interlocked.Read(ref _blockedClicks) > 0)
            {
                _helperLabel.Text = AppStrings.Format("Пропущено над окном: {0:N0} · {1} стоп", "Skipped over app: {0:N0} · {1} stop", Interlocked.Read(ref _blockedClicks), _hotkeys.Toggle);
            }
        }
        _sessionProgress.Active = _sessionActive;
        RefreshNextEvent();
        RefreshShell();
    }

    private void PostToUi(Action action)
    {
        if (_closing || IsDisposed || Disposing || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke((Action)(() =>
            {
                if (_closing || IsDisposed || Disposing)
                {
                    return;
                }

                action();
            }));
        }
        catch (InvalidOperationException)
        {
            // The native window can disappear between the state check and BeginInvoke.
        }
    }

    private void RefreshNextEvent()
    {
        if (_statusPill.State != AppRunState.Running || _nextEventAt is null)
        {
            return;
        }

        var remaining = _nextEventAt.Value - DateTimeOffset.Now;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }
        _nextEventLabel.Text = AppStrings.Format("{0}: {1:0.0} с", "{0}: {1:0.0} s", AppStrings.T(_nextEventCaption), remaining.TotalSeconds);
    }

    private void SetRunState(AppRunState state)
    {
        _statusPill.State = state;
        _startButton.ApplyState(state != AppRunState.Stopped);
        RefreshShell();
    }

    private void ToggleSettings(bool enabled)
    {
        foreach (var modeButton in _modeButtons)
        {
            modeButton.Enabled = enabled;
        }
        foreach (var view in _modeViews)
        {
            view.Enabled = enabled;
        }
        foreach (var preset in _presetButtons)
        {
            preset.Enabled = enabled;
        }
        _mouseAction.Enabled = enabled;
        _targetMode.Enabled = enabled;
        _jitterRadius.Enabled = enabled;
        _startDelay.Enabled = enabled;
        _maxClicks.Enabled = enabled;
        _maxDuration.Enabled = enabled;
        _alwaysOnTop.Enabled = enabled;
        _captureButton.Enabled = enabled;
        _resetPointButton.Enabled = enabled && _fixedPoint is not null;
    }

    private void SavePreferences()
    {
        if (!_persistSettings)
        {
            return;
        }
        CommitNumberInputs();
        _preferences.Mode = _selectedMode;
        _preferences.MouseAction = (MouseAction)_mouseAction.SelectedIndex;
        _preferences.TargetMode = (TargetMode)_targetMode.SelectedIndex;
        _preferences.FixedX = _fixedPoint?.X ?? 0;
        _preferences.FixedY = _fixedPoint?.Y ?? 0;
        _preferences.HasFixedPoint = _fixedPoint.HasValue;
        _preferences.JitterRadius = (int)_jitterRadius.Value;
        _preferences.ClicksPerSecond = _speedCps.Value;
        _preferences.SpeedIntervalMilliseconds = _speedIntervalMs.Value;
        _preferences.RandomMinimumSeconds = _randomMinimum.Value;
        _preferences.RandomMaximumSeconds = _randomMaximum.Value;
        _preferences.BurstClickCount = (int)_burstCount.Value;
        _preferences.BurstClicksPerSecond = _burstCps.Value;
        _preferences.BurstPauseSeconds = _burstPause.Value;
        _preferences.StartDelaySeconds = (int)_startDelay.Value;
        _preferences.MaxClicks = (long)_maxClicks.Value;
        _preferences.MaxDurationSeconds = (double)_maxDuration.Value;
        _preferences.AlwaysOnTop = _alwaysOnTop.Checked;
        _preferences.WindowWidth = (int)Math.Round(ClientSize.Width * (96D / DeviceDpi));
        _preferences.WindowHeight = (int)Math.Round(ClientSize.Height * (96D / DeviceDpi));
        _preferences.Language = AppStrings.Language;
        StoreHotkeyPreferences();
        SettingsStore.Save(_preferences);
    }

    private void CommitNumberInputs()
    {
        if (_speedCps.ContainsFocus)
        {
            _speedIntervalMs.Commit();
            _speedCps.Commit();
        }
        else
        {
            _speedCps.Commit();
            _speedIntervalMs.Commit();
        }

        _randomMinimum.Commit();
        _randomMaximum.Commit();
        _burstCount.Commit();
        _burstCps.Commit();
        _burstPause.Commit();
        _jitterRadius.Commit();
        _startDelay.Commit();
        _maxClicks.Commit();
        _maxDuration.Commit();
    }

    private void MainFormKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        HandleLocalHotkey(eventArgs);
    }

    private void MainFormKeyUp(object? sender, KeyEventArgs eventArgs)
    {
        _localKeysDown.Remove(eventArgs.KeyCode);
    }

    protected override void OnDeactivate(EventArgs eventArgs)
    {
        _localKeysDown.Clear();
        base.OnDeactivate(eventArgs);
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        NativeInterop.ApplyWindowFrame(Handle, Theme.IsDark && !Theme.HighContrast);
        ApplyMinimumWindowSize();
        QueueLayoutRefresh();
        if (!_hotkeysSuspended) RegisterAllHotkeys();
    }

    protected override void OnHandleDestroyed(EventArgs eventArgs)
    {
        UnregisterAllHotkeys();
        base.OnHandleDestroyed(eventArgs);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs eventArgs)
    {
        base.OnDpiChanged(eventArgs);
        ApplyMinimumWindowSize();
        QueueLayoutRefresh();
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        QueueLayoutRefresh();
        if (_shellConfigured && _preferences.TrayMode && WindowState == FormWindowState.Minimized)
        {
            HideToTray();
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg is 0x001A or 0x031A or 0x0320)
        {
            QueueWindowsThemeRefresh();
        }
        if (message.Msg == NativeInterop.WmHotKey)
        {
            DispatchHotkey(message.WParam.ToInt32(), message.LParam);
        }
        base.WndProc(ref message);
    }

    private async Task EmergencyStopAsync()
    {
        if (_closing)
        {
            return;
        }

        _engine.RequestStop();

        var countdown = _countdownCancellation;
        if (countdown is not null)
        {
            countdown.Cancel();
            return;
        }

        await _transition.WaitAsync();
        try
        {
            if (_closing)
            {
                return;
            }

            if (IsBusy)
            {
                await StopAllAsync(AppStrings.T("Аварийная остановка"));
            }
        }
        finally
        {
            _transition.Release();
        }
    }

    private void SystemPowerModeChanged(object sender, PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode == PowerModes.Suspend)
        {
            RequestImmediateStop();
            PostToUi(() => ShowSystemStop(AppStrings.T("Остановлено перед переходом в сон")));
        }
    }

    private void SystemSessionSwitch(object sender, SessionSwitchEventArgs eventArgs)
    {
        if (eventArgs.Reason == SessionSwitchReason.SessionLock)
        {
            RequestImmediateStop();
            PostToUi(() => ShowSystemStop(AppStrings.T("Остановлено при блокировке Windows")));
        }
    }

    private void RequestImmediateStop()
    {
        lock (_startGate)
        {
            try
            {
                _countdownCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Countdown has already finished on the UI thread.
            }
            _engine.RequestStop();
        }
    }

    private void ShowSystemStop(string message)
    {
        _nextEventAt = null;
        _nextEventCaption = string.Empty;
        SetRunState(AppRunState.Stopped);
        ToggleSettings(true);
        _startButton.ApplyState(false);
        _nextEventLabel.Text = message;
        _helperLabel.Text = AppStrings.T("Клики не возобновятся автоматически");
    }

    private void UpdateHotKeyUi()
    {
        if (_hotKeyHelpLabel is null || _startButton is null || _captureButton is null)
        {
            return;
        }

        var toggleText = $"{_hotkeys.Toggle} {(_toggleHotKeyRegistered ? AppStrings.T("Старт", "Start") : AppStrings.T("локально", "in app"))}";
        var captureText = _captureHotKeyRegistered ? AppStrings.T("F7 Точка") : AppStrings.T("F7 локально");
        var emergencyText = _emergencyHotKeyRegistered
            ? $"{_emergencyHotKeyText.Replace(" + ", "+")} {AppStrings.T("Стоп", "Stop")}"
            : AppStrings.T("Аварийная клавиша недоступна");
        _hotKeyHelpLabel.Text = _layoutProfile == LayoutProfile.Phone
            ? $"{_emergencyHotKeyText.Replace(" + ", "+")} · {AppStrings.T("стоп", "stop")}"
            : $"{toggleText}  ·  {captureText}  ·  {emergencyText}";
        AppStrings.SetToolTip(_toolTip, _hotKeyHelpLabel, $"{toggleText} · {captureText} · {emergencyText}");
        _hotKeyHelpLabel.ForeColor = _emergencyHotKeyRegistered ? Theme.Muted : Theme.Red;

        _startButton.HotKeyHint = _hotkeys.Toggle;
        _toolTip.SetToolTip(_startButton, $"{_hotkeys.Toggle} · {AppStrings.T("запуск / остановка", "start / stop")}");
        _startButton.ApplyState(IsBusy);
        UpdateTargetUi();

        for (var index = 0; index < _modeButtons.Length; index++)
        {
            var shortcut = index switch { 1 => _hotkeys.Random, 2 => _hotkeys.Burst, _ => _hotkeys.Speed };
            var local = _registeredHotkeys.ContainsKey(HotKeySpeed + index) ? string.Empty : AppStrings.T(" · только в окне", " · in app only");
            _toolTip.SetToolTip(_modeButtons[index], $"{TrayController.ModeCaption((ClickMode)index)} · {shortcut}{local}");
        }
        if (!_toggleHotKeyRegistered || !_captureHotKeyRegistered ||
            UserHotkeys(_hotkeys).Any(binding => !_registeredHotkeys.ContainsKey(binding.Id)))
        {
            _showingHotkeyWarning = true;
            _helperLabel.Text = AppStrings.T("Некоторые горячие клавиши доступны только в окне");
            _helperLabel.ForeColor = Theme.Amber;
        }
        else if (_showingHotkeyWarning)
        {
            _showingHotkeyWarning = false;
            _helperLabel.Text = IsBusy ? BuildSessionHint() :
                AppStrings.T($"{_hotkeys.Toggle} — запуск · F7 — точка", $"{_hotkeys.Toggle} — start · F7 — point");
            _helperLabel.ForeColor = Theme.Muted;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        if (_shellConfigured && _preferences.TrayMode && !_exitRequested &&
            eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            SavePreferences();
            HideToTray();
            base.OnFormClosing(eventArgs);
            return;
        }
        _closing = true;
        _displayTimer.Stop();
        _saveTimer.Stop();
        SystemEvents.PowerModeChanged -= SystemPowerModeChanged;
        SystemEvents.SessionSwitch -= SystemSessionSwitch;
        SavePreferences();
        _countdownCancellation?.Cancel();
        _engine.StopAsync().GetAwaiter().GetResult();
        _engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _displayTimer.Dispose();
        _saveTimer.Dispose();
        _toolTip.Dispose();
        base.OnFormClosing(eventArgs);
    }

    private bool IsBusy => _countdownCancellation is not null || _engine.IsRunning;

    private void ApplyMinimumWindowSize()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var scale = DeviceDpi / 96F;
        var profile = GetLayoutProfile(ClientSize.Width / scale);
        ApplyMinimumWindowSize(profile, scale);
    }

    private void ApplyMinimumWindowSize(LayoutProfile profile, float scale)
    {
        var minimumHeight = profile switch
        {
            LayoutProfile.Wide => 620,
            LayoutProfile.Compact => 680,
            _ => 800
        };
        var minimumClientSize = new Size(
            (int)Math.Ceiling(340 * scale),
            (int)Math.Ceiling(minimumHeight * scale));
        MinimumSize = SizeFromClientSize(minimumClientSize);
    }

    private static LayoutProfile GetLayoutProfile(float logicalWidth) => logicalWidth switch
    {
        >= 700 => LayoutProfile.Wide,
        >= 500 => LayoutProfile.Compact,
        _ => LayoutProfile.Phone
    };

    private void QueueLayoutRefresh()
    {
        if (_layoutRefreshQueued || _applyingResponsiveLayout || !IsHandleCreated || _closing || IsDisposed)
        {
            return;
        }

        _layoutRefreshQueued = true;
        BeginInvoke((MethodInvoker)(() =>
        {
            try
            {
                if (_closing || IsDisposed)
                {
                    return;
                }
                ApplyResponsiveLayout();
            }
            finally
            {
                _layoutRefreshQueued = false;
            }
        }));
    }

    protected override void OnLoad(EventArgs eventArgs)
    {
        // Complete the first layout before Windows is allowed to publish the first frame.
        ApplyResponsiveLayout();
        PerformLayoutTree(this);
        base.OnLoad(eventArgs);
    }

    private static void PerformLayoutTree(Control parent)
    {
        parent.PerformLayout();
        foreach (Control child in parent.Controls)
        {
            PerformLayoutTree(child);
        }
    }

    private void ApplyResponsiveLayout()
    {
        if (_applyingResponsiveLayout || _rootLayout is null)
        {
            return;
        }
        _applyingResponsiveLayout = true;
        _rootLayout.SuspendLayout();
        try
        {
            var scale = DeviceDpi / 96F;
            var logicalWidth = ClientSize.Width / scale;
            var profile = GetLayoutProfile(logicalWidth);
            ApplyMinimumWindowSize(profile, scale);

            if (_layoutProfile != profile || _appliedLayoutDpi != DeviceDpi)
            {
                _layoutProfile = profile;
                _appliedLayoutDpi = DeviceDpi;
                foreach (var modeView in _modeViews)
                {
                    var columns = profile switch
                    {
                        LayoutProfile.Wide => modeView.FieldCount,
                        LayoutProfile.Compact => modeView.FieldCount,
                        _ => Math.Min(2, modeView.FieldCount)
                    };
                    ConfigureFieldGrid(modeView, columns);
                }
                ConfigureFieldGrid(_commonFieldGrid, profile switch
                {
                    LayoutProfile.Wide => 4,
                    LayoutProfile.Compact => 3,
                    _ => 2
                });
                ConfigureFooterLayout(profile != LayoutProfile.Wide);
                _brandLayout.RowStyles[0].Height = ScaleLogical(32);
                UpdateHotKeyUi();
            }
            _brandTitle.Text = profile == LayoutProfile.Phone ? "Bench" : "Bench Auto Clicker";

            _contentPanel.AutoScroll = false;
            _contentPanel.AutoScrollMinSize = Size.Empty;
            if (_rootLayout.Dock != DockStyle.Fill)
            {
                _rootLayout.Dock = DockStyle.Fill;
            }
            _contentPanel.PerformLayout();
            LayoutRootSections(profile);
        }
        finally
        {
            _rootLayout.ResumeLayout(true);
            _applyingResponsiveLayout = false;
        }
    }

    private void ConfigureFooterLayout(bool portrait)
    {
        _footerLayout.SuspendLayout();
        _footerLayout.Controls.Clear();
        _footerLayout.ColumnStyles.Clear();
        _footerLayout.RowStyles.Clear();
        if (portrait)
        {
            _footerLayout.ColumnCount = 2;
            _footerLayout.RowCount = 2;
            _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(116)));
            _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _footerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(58)));
            _footerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _footerLayout.Controls.Add(_footerMetric, 0, 0);
            _footerLayout.Controls.Add(_footerHelper, 1, 0);
            _footerLayout.Controls.Add(_startButton, 0, 1);
            _footerLayout.SetColumnSpan(_startButton, 2);
        }
        else
        {
            _footerLayout.ColumnCount = 3;
            _footerLayout.RowCount = 1;
            _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(116)));
            _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(188)));
            _footerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _footerLayout.Controls.Add(_footerMetric, 0, 0);
            _footerLayout.Controls.Add(_footerHelper, 1, 0);
            _footerLayout.Controls.Add(_startButton, 2, 0);
            _footerLayout.SetColumnSpan(_startButton, 1);
            _footerLayout.SetRowSpan(_startButton, 1);
        }
        _footerLayout.ResumeLayout(true);
    }

    private void LayoutRootSections(LayoutProfile profile)
    {
        var width = Math.Max(1, _rootLayout.ClientSize.Width);
        var stacked = profile != LayoutProfile.Wide;
        var headerHeight = ScaleLogical(profile == LayoutProfile.Phone ? 74 : 68);
        var stripHeight = ScaleLogical(46);
        var gap = ScaleLogical(10);
        var footerHeight = ScaleLogical(stacked ? 120 : 88);
        int modeHeight;
        int commonHeight;
        if (profile == LayoutProfile.Wide)
        {
            var available = Math.Max(
                1,
                _rootLayout.ClientSize.Height - headerHeight - stripHeight - footerHeight - (gap * 3));
            modeHeight = Math.Max(ScaleLogical(160), (int)Math.Round(available * 0.44));
            commonHeight = Math.Max(1, available - modeHeight);
        }
        else
        {
            modeHeight = ScaleLogical(profile == LayoutProfile.Compact ? 160 : 214);
            var baseCommonHeight = ScaleLogical(profile == LayoutProfile.Compact ? 226 : 282);
            var usedHeight = headerHeight + stripHeight + footerHeight + modeHeight + (gap * 3);
            commonHeight = Math.Max(baseCommonHeight, _rootLayout.ClientSize.Height - usedHeight);
        }

        var top = 0;
        _headerSection.SetBounds(0, top, width, headerHeight);
        top += headerHeight;
        _modeStripSection.SetBounds(0, top, width, stripHeight);
        top += stripHeight + gap;
        _modeCardSection.SetBounds(0, top, width, modeHeight);
        top += modeHeight + gap;
        _commonCardSection.SetBounds(0, top, width, commonHeight);
        top += commonHeight + gap;
        _footerCardSection.SetBounds(0, top, width, footerHeight);
    }

    private int ScaleLogical(int logicalValue) =>
        (int)Math.Ceiling(logicalValue * (DeviceDpi / 96F));

    private void ShowValidation(string text)
    {
        MessageBox.Show(text, AppStrings.T("Проверьте настройки"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private RoundedButton CreateModeButton(string text, ClickMode mode)
    {
        var button = new RoundedButton
        {
            Text = text,
            Tag = mode,
            TextAlign = ContentAlignment.MiddleCenter,
            FillColor = Theme.Card,
            HoverColor = Theme.CardHover,
            SelectedColor = Theme.AccentSoft,
            BorderColor = Theme.Border,
            AccentStripWhenSelected = false,
            BottomAccentWhenSelected = false,
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            Padding = Padding.Empty
        };
        AppStrings.Bind(button, text);
        return button;
    }

    private static FieldGrid CreateFieldGrid(int columns, params Control[] fields) =>
        new(columns, fields) { Dock = DockStyle.Fill };

    private static void ConfigureFieldGrid(FieldGrid grid, int columns)
    {
        grid.Columns = columns;
    }

    private static Control CreateField(string caption, Control input)
    {
        var field = new BufferedPanel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(0, 1, 8, 1) };
        var label = CreateLabel(caption, 8F, FontStyle.Regular, Theme.Muted);
        label.Dock = DockStyle.Top;
        label.Height = 19;
        Control content = input is SelectBox or CheckBox
            ? new InputSurface(input)
            : input;
        content.Dock = DockStyle.Fill;
        field.Controls.Add(content);
        field.Controls.Add(label);
        return field;
    }

    private static Label CreateLabel(string text, float size, FontStyle style, Color color)
    {
        var label = new Label
        {
            Text = text,
            Font = new Font("Segoe UI", size, style),
            ForeColor = color,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
            UseMnemonic = false
        };
        AppStrings.Bind(label, text);
        return label;
    }

    private static NumberBox CreateNumber(
        decimal minimum,
        decimal maximum,
        decimal value,
        decimal increment,
        int decimalPlaces,
        string suffix)
    {
        return new NumberBox
        {
            Minimum = minimum,
            Maximum = maximum,
            Increment = increment,
            DecimalPlaces = decimalPlaces,
            Value = value,
            BackColor = Theme.Input,
            ForeColor = Theme.Text,
            Padding = Padding.Empty,
            Tag = suffix
        };
    }

    private static SelectBox CreateComboBox(string[] items)
    {
        return new SelectBox(items);
    }

    private static void SetNumber(NumberBox control, decimal value)
    {
        control.Value = Math.Clamp(value, control.Minimum, control.Maximum);
    }
}
