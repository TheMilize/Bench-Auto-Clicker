using Microsoft.Win32;
using System.Drawing;

namespace BenchAutoClicker;

internal sealed class MainForm : Form
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

    private readonly ClickEngine _engine = new();
    private readonly UserPreferences _preferences;
    private readonly SemaphoreSlim _transition = new(1, 1);
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
    private bool _hotKeysRegistered;
    private bool _toggleHotKeyRegistered;
    private bool _captureHotKeyRegistered;
    private bool _emergencyHotKeyRegistered;
    private bool _localF6Down;
    private bool _localF7Down;
    private string _emergencyHotKeyText = "Ctrl + Shift + F6";
    private bool _syncingSpeedInputs;
    private bool _layoutRefreshQueued;
    private LayoutProfile? _layoutProfile;
    private int _appliedLayoutDpi;

    public MainForm()
    {
        _preferences = SettingsStore.Load();
        _speedCps = CreateNumber(0.2m, 50, 5, 0.2m, 1, " CPS");
        _speedIntervalMs = CreateNumber(20, 5000, 200, 10, 0, " мс");
        _randomMinimum = CreateNumber(0.1m, 3600, 1, 0.5m, 1, " с");
        _randomMaximum = CreateNumber(0.1m, 3600, 15, 0.5m, 1, " с");
        _burstCount = CreateNumber(1, 500, 5, 1, 0, " кликов");
        _burstCps = CreateNumber(0.2m, 50, 10, 0.5m, 1, " CPS");
        _burstPause = CreateNumber(0.1m, 3600, 3, 0.5m, 1, " с");
        _jitterRadius = CreateNumber(0, 100, 0, 1, 0, " px");
        _startDelay = CreateNumber(0, 10, 3, 1, 0, " с");
        _mouseAction = CreateComboBox(["Левая кнопка", "Правая кнопка", "Средняя кнопка", "Двойной левый"]);
        _targetMode = CreateComboBox(["Под курсором", "В сохранённой точке"]);
        _alwaysOnTop = new ToggleSwitch
        {
            Text = "Поверх окон",
            ForeColor = Theme.Text,
            Padding = Padding.Empty
        };

        BuildWindow();
        ApplyPreferences();
        WireEvents();
    }

    private void BuildWindow()
    {
        SuspendLayout();
        Text = "Bench Auto Clicker 1.6 — многорежимный кликер";
        Icon = BrandAssets.LoadIcon();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(640, 500);
        BackColor = Theme.Window;
        ForeColor = Theme.Text;
        Font = new Font("Segoe UI", 9F);
        KeyPreview = true;

        Controls.Add(BuildContent());
        ResumeLayout(true);
    }

    private Control BuildContent()
    {
        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Window,
            Padding = new Padding(16, 12, 16, 8)
        };
        _rootLayout = new Panel
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
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Window,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var logo = new LogoBadge(BrandAssets.LoadBitmap())
        {
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            Size = new Size(30, 30)
        };
        panel.Controls.Add(logo, 0, 0);

        var brand = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Window,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(6, 0, 4, 0)
        };
        brand.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = CreateLabel("Bench Auto Clicker 1.6", 12.5F, FontStyle.Bold, Theme.Text);
        title.Dock = DockStyle.Fill;
        _hotKeyHelpLabel = CreateLabel("F6  Старт  •  F7  Точка  •  Ctrl+Shift+F6  Стоп", 7.3F, FontStyle.Regular, Theme.Muted);
        _hotKeyHelpLabel.Dock = DockStyle.Fill;
        brand.Controls.Add(title, 0, 0);
        brand.Controls.Add(_hotKeyHelpLabel, 0, 1);
        panel.Controls.Add(brand, 1, 0);

        _statusPill = new StatusPill
        {
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty
        };
        panel.Controls.Add(_statusPill, 2, 0);
        return panel;
    }

    private Control BuildModeStrip()
    {
        var strip = new TableLayoutPanel
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
            CreateModeButton("Скорость", ClickMode.Speed),
            CreateModeButton("Случайно", ClickMode.RandomInterval),
            CreateModeButton("Серии", ClickMode.Burst)
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
        var layout = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Theme.Card
        };
        card.Controls.Add(layout);

        _modeTitle = CreateLabel(string.Empty, 9.4F, FontStyle.Bold, Theme.Text);
        _modeTitle.Dock = DockStyle.Top;
        _modeTitle.Height = 21;
        _modeSubtitle = CreateLabel(string.Empty, 8.3F, FontStyle.Regular, Theme.Muted);

        _modeHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
        layout.Controls.Add(_modeHost);
        layout.Controls.Add(_modeTitle);
        _modeViews = [BuildSpeedView(), BuildRandomView(), BuildBurstView()];
        return card;
    }

    private FieldGrid BuildSpeedView()
    {
        return CreateFieldGrid(
            2,
            CreateField("Скорость, CPS", _speedCps),
            CreateField("Интервал, мс", _speedIntervalMs));
    }

    private FieldGrid BuildRandomView()
    {
        return CreateFieldGrid(
            2,
            CreateField("Минимум, сек", _randomMinimum),
            CreateField("Максимум, сек", _randomMaximum));
    }

    private FieldGrid BuildBurstView()
    {
        return CreateFieldGrid(
            3,
            CreateField("Кликов", _burstCount),
            CreateField("Скорость, CPS", _burstCps),
            CreateField("Пауза, сек", _burstPause));
    }

    private Control BuildCommonCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(12, 10, 12, 10)
        };
        var layout = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Theme.Card
        };
        card.Controls.Add(layout);

        var title = CreateLabel("Клик и позиция", 9.4F, FontStyle.Bold, Theme.Text);
        title.Dock = DockStyle.Top;
        title.Height = 22;

        _captureButton = new RoundedButton
        {
            Text = "Центр экрана · F7",
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
            AccessibleName = "Сбросить точку и использовать центр экрана"
        };
        _toolTip.SetToolTip(_resetPointButton, "Сбросить точку — использовать центр экрана под курсором");
        var pointActions = new PointActionsPanel(_captureButton, _resetPointButton);

        _commonFieldGrid = CreateFieldGrid(
            3,
            CreateField("Кнопка мыши", _mouseAction),
            CreateField("Позиция", _targetMode),
            CreateField("Разброс, px", _jitterRadius),
            CreateField("Фиксированная точка", pointActions),
            CreateField("Задержка, сек", _startDelay),
            CreateField("Окно", _alwaysOnTop));
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
        _footerLayout = new TableLayoutPanel
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

        _footerMetric = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card };
        var metricCaption = CreateLabel("КЛИКОВ", 7F, FontStyle.Bold, Theme.Muted);
        metricCaption.Dock = DockStyle.Top;
        metricCaption.Height = 15;
        _sessionCounter = CreateLabel("0", 13F, FontStyle.Bold, Theme.Text);
        _sessionCounter.Dock = DockStyle.Fill;
        _footerMetric.Controls.AddRange([_sessionCounter, metricCaption]);
        _footerLayout.Controls.Add(_footerMetric, 0, 0);

        _footerHelper = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(8, 0, 6, 0) };
        _nextEventLabel = CreateLabel("Готов к запуску", 8.3F, FontStyle.Bold, Theme.Text);
        _nextEventLabel.Dock = DockStyle.Top;
        _nextEventLabel.Height = 20;
        _helperLabel = CreateLabel("Защита от кликов по себе", 7.4F, FontStyle.Regular, Theme.Muted);
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
        _engine.Stopped += EngineStopped;

        _displayTimer.Tick += (_, _) => RefreshNextEvent();
        _displayTimer.Start();
        SystemEvents.PowerModeChanged += SystemPowerModeChanged;
        SystemEvents.SessionSwitch += SystemSessionSwitch;
    }

    private void ApplyPreferences()
    {
        _selectedMode = Enum.IsDefined(_preferences.Mode) ? _preferences.Mode : ClickMode.Speed;
        ClientSize = new Size(
            Math.Clamp(_preferences.WindowWidth, 340, 1200),
            Math.Clamp(_preferences.WindowHeight, 420, 1400));
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
                "Случайный интервал",
                "Один клик после случайной паузы в заданном диапазоне"),
            ClickMode.Burst => (
                "Клики сериями",
                "Серия быстрых кликов, пауза и автоматический повтор"),
            _ => (
                "Постоянная скорость",
                "Ровный темп от 0,2 до 50 кликов в секунду")
        };

        _modeHost.SuspendLayout();
        _modeHost.Controls.Clear();
        var view = _modeViews[(int)mode];
        view.Dock = DockStyle.Fill;
        _modeHost.Controls.Add(view);
        _modeHost.ResumeLayout();
        QueueLayoutRefresh();
    }

    private void ModeButtonClicked(object? sender, EventArgs eventArgs)
    {
        if (sender is RoundedButton button && button.Tag is ClickMode mode && !IsBusy)
        {
            SelectMode(mode);
        }
    }

    private async Task ToggleRunningAsync()
    {
        if (_closing)
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
            if (_closing)
            {
                return;
            }

            if (IsBusy)
            {
                await StopAllAsync("Остановлено пользователем");
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
        _sessionClicks = 0;
        _blockedClicks = 0;
        _sessionCounter.Text = "0";
        ToggleSettings(false);
        _startButton.ApplyState(true);

        var delaySeconds = (int)_startDelay.Value;
        _countdownCancellation = new CancellationTokenSource();
        var countdownToken = _countdownCancellation.Token;
        try
        {
            for (var remaining = delaySeconds; remaining > 0; remaining--)
            {
                SetRunState(AppRunState.Countdown);
                _nextEventLabel.Text = $"Старт через {remaining} с";
                _helperLabel.Text = "Переключитесь в нужное окно";
                await Task.Delay(TimeSpan.FromSeconds(1), countdownToken);
            }

            countdownToken.ThrowIfCancellationRequested();
            _countdownCancellation.Dispose();
            _countdownCancellation = null;
            _nextEventAt = null;
            _nextEventCaption = string.Empty;
            if (!_engine.Start(configuration))
            {
                throw new InvalidOperationException("Кликер уже работает.");
            }

            SetRunState(AppRunState.Running);
            if (_selectedMode == ClickMode.Speed)
            {
                _nextEventLabel.Text = $"Интервал: {_speedIntervalMs.Value:0} мс";
            }
            else
            {
                _nextEventLabel.Text = "Планируется первый клик…";
            }
            _helperLabel.Text = "F6 — остановить в любой момент";
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
            _nextEventLabel.Text = "Запуск отменён";
            _helperLabel.Text = "Защита от кликов по Bench Auto Clicker включена";
        }
        catch (Exception exception)
        {
            _countdownCancellation?.Dispose();
            _countdownCancellation = null;
            SetRunState(AppRunState.Stopped);
            ToggleSettings(true);
            _startButton.ApplyState(false);
            MessageBox.Show(exception.Message, "Bench Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task StopAllAsync(string message)
    {
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
        SetRunState(AppRunState.Stopped);
        ToggleSettings(true);
        _startButton.ApplyState(false);
        _nextEventLabel.Text = message;
        _helperLabel.Text = _blockedClicks > 0
            ? $"Заблокировано кликов по Bench Auto Clicker: {_blockedClicks}"
            : "Защита от кликов по Bench Auto Clicker включена";
    }

    private ClickConfiguration? CreateConfiguration()
    {
        CommitNumberInputs();
        if (!_emergencyHotKeyRegistered)
        {
            ShowValidation("Не удалось зарегистрировать глобальную клавишу аварийной остановки. Закройте программу, которая перехватывает Ctrl+Shift+F6 и Ctrl+Shift+F12, затем перезапустите Bench Auto Clicker.");
            return null;
        }

        if (_selectedMode == ClickMode.RandomInterval && _randomMinimum.Value > _randomMaximum.Value)
        {
            ShowValidation("Минимальная пауза не может быть больше максимальной.");
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
            ShowValidation("Сохранённая точка больше не находится на подключённом мониторе. Сохраните её заново клавишей F7.");
            return null;
        }

        if (targetMode == TargetMode.FixedPoint && Bounds.Contains(resolvedPoint))
        {
            ShowValidation(_fixedPoint is null
                ? "Центр выбранного экрана находится под окном Bench Auto Clicker. Переместите окно в сторону или сохраните точку клавишей F7."
                : "Сохранённая точка находится внутри Bench Auto Clicker. Выберите точку в другом окне.");
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
            Handle);
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
        if (Bounds.Contains(_fixedPoint.Value))
        {
            _captureButton.Text = "Внутри Bench Auto Clicker · F7";
            _captureButton.ForeColor = Theme.Amber;
        }
    }

    private void UpdateTargetUi()
    {
        var fixedTarget = _targetMode.SelectedIndex == (int)TargetMode.FixedPoint;
        _captureButton.Selected = fixedTarget;
        var hotKeyHint = _captureHotKeyRegistered ? " · F7" : string.Empty;
        _captureButton.Text = _fixedPoint is { } point
            ? $"X {point.X}, Y {point.Y}{hotKeyHint}"
            : $"Центр экрана{hotKeyHint}";
        _captureButton.ForeColor = _fixedPoint is null ? Theme.AccentHover : Theme.Green;
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
        _helperLabel.Text = "Цель: центр экрана под курсором";
        _helperLabel.ForeColor = Theme.Muted;
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
        if (_closing || !IsHandleCreated)
        {
            return;
        }

        PostToUi(() =>
        {
            if (result.WasSent)
            {
                _sessionClicks += result.PhysicalClickCount;
                _sessionCounter.Text = _sessionClicks.ToString("N0");
            }
            else if (result.WasBlocked)
            {
                _blockedClicks++;
                _helperLabel.Text = "Клик пропущен: курсор над Bench Auto Clicker";
            }
        });
    }

    private void EngineWaitScheduled(DateTimeOffset at, string caption)
    {
        if (_closing || !IsHandleCreated)
        {
            return;
        }

        PostToUi(() =>
        {
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

        PostToUi(() =>
        {
            _nextEventLabel.Text = "Работа остановлена из-за ошибки";
            MessageBox.Show(exception.Message, "Bench Auto Clicker", MessageBoxButtons.OK, MessageBoxIcon.Error);
        });
    }

    private void EngineStopped()
    {
        if (_closing || !IsHandleCreated)
        {
            return;
        }

        PostToUi(() =>
        {
            SetRunState(AppRunState.Stopped);
            ToggleSettings(true);
            _startButton.ApplyState(false);
        });
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
        _nextEventLabel.Text = $"{_nextEventCaption}: {remaining.TotalSeconds:0.0} с";
    }

    private void SetRunState(AppRunState state)
    {
        _statusPill.State = state;
        _startButton.ApplyState(state != AppRunState.Stopped);
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
        _mouseAction.Enabled = enabled;
        _targetMode.Enabled = enabled;
        _jitterRadius.Enabled = enabled;
        _startDelay.Enabled = enabled;
        _alwaysOnTop.Enabled = enabled;
        _captureButton.Enabled = enabled;
        _resetPointButton.Enabled = enabled && _fixedPoint is not null;
    }

    private void SavePreferences()
    {
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
        _preferences.AlwaysOnTop = _alwaysOnTop.Checked;
        _preferences.WindowWidth = (int)Math.Round(ClientSize.Width * (96D / DeviceDpi));
        _preferences.WindowHeight = (int)Math.Round(ClientSize.Height * (96D / DeviceDpi));
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
    }

    private void MainFormKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.F6 && eventArgs.Control && eventArgs.Shift &&
            !_emergencyHotKeyRegistered && !_localF6Down)
        {
            _localF6Down = true;
            eventArgs.Handled = true;
            eventArgs.SuppressKeyPress = true;
            _ = EmergencyStopAsync();
        }
        else if (eventArgs.KeyCode == Keys.F6 &&
                 !eventArgs.Control && !eventArgs.Shift && !eventArgs.Alt &&
                 !_toggleHotKeyRegistered && !_localF6Down)
        {
            _localF6Down = true;
            eventArgs.Handled = true;
            eventArgs.SuppressKeyPress = true;
            _ = ToggleRunningAsync();
        }
        else if (eventArgs.KeyCode == Keys.F7 &&
                 !eventArgs.Control && !eventArgs.Shift && !eventArgs.Alt &&
                 !_captureHotKeyRegistered && !_localF7Down)
        {
            _localF7Down = true;
            eventArgs.Handled = true;
            eventArgs.SuppressKeyPress = true;
            CapturePoint();
        }
    }

    private void MainFormKeyUp(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.KeyCode == Keys.F6)
        {
            _localF6Down = false;
        }
        else if (eventArgs.KeyCode == Keys.F7)
        {
            _localF7Down = false;
        }
    }

    protected override void OnDeactivate(EventArgs eventArgs)
    {
        _localF6Down = false;
        _localF7Down = false;
        base.OnDeactivate(eventArgs);
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        ApplyMinimumWindowSize();
        QueueLayoutRefresh();
        _toggleHotKeyRegistered = NativeInterop.RegisterHotKey(Handle, HotKeyToggle, ModNoRepeat, (uint)Keys.F6);
        _captureHotKeyRegistered = NativeInterop.RegisterHotKey(Handle, HotKeyCapture, ModNoRepeat, (uint)Keys.F7);
        _emergencyHotKeyRegistered = NativeInterop.RegisterHotKey(
            Handle,
            HotKeyEmergency,
            NativeInterop.ModControl | NativeInterop.ModShift | ModNoRepeat,
            (uint)Keys.F6);
        _emergencyHotKeyText = "Ctrl + Shift + F6";
        if (!_emergencyHotKeyRegistered)
        {
            _emergencyHotKeyRegistered = NativeInterop.RegisterHotKey(
                Handle,
                HotKeyEmergency,
                NativeInterop.ModControl | NativeInterop.ModShift | ModNoRepeat,
                (uint)Keys.F12);
            if (_emergencyHotKeyRegistered)
            {
                _emergencyHotKeyText = "Ctrl + Shift + F12";
            }
        }
        _hotKeysRegistered = _toggleHotKeyRegistered || _captureHotKeyRegistered || _emergencyHotKeyRegistered;
        UpdateHotKeyUi();
        if (!_emergencyHotKeyRegistered)
        {
            Shown += (_, _) =>
            {
                MessageBox.Show(
                    "Bench Auto Clicker не смог зарегистрировать Ctrl+Shift+F6 или резервную Ctrl+Shift+F12. Запуск заблокирован, пока нет надёжной аварийной остановки.",
                    "Горячие клавиши заняты",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            };
        }
    }

    protected override void OnHandleDestroyed(EventArgs eventArgs)
    {
        if (_hotKeysRegistered)
        {
            NativeInterop.UnregisterHotKey(Handle, HotKeyToggle);
            NativeInterop.UnregisterHotKey(Handle, HotKeyCapture);
            NativeInterop.UnregisterHotKey(Handle, HotKeyEmergency);
            _hotKeysRegistered = false;
            _toggleHotKeyRegistered = false;
            _captureHotKeyRegistered = false;
            _emergencyHotKeyRegistered = false;
        }
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
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeInterop.WmHotKey)
        {
            switch (message.WParam.ToInt32())
            {
                case HotKeyToggle:
                    _ = ToggleRunningAsync();
                    break;
                case HotKeyCapture:
                    CapturePoint();
                    break;
                case HotKeyEmergency:
                    _ = EmergencyStopAsync();
                    break;
            }
        }
        base.WndProc(ref message);
    }

    private async Task EmergencyStopAsync()
    {
        if (_closing)
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
            if (_closing)
            {
                return;
            }

            if (IsBusy)
            {
                await StopAllAsync("Аварийная остановка");
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
            PostToUi(() => ShowSystemStop("Остановлено перед переходом в сон"));
        }
    }

    private void SystemSessionSwitch(object sender, SessionSwitchEventArgs eventArgs)
    {
        if (eventArgs.Reason == SessionSwitchReason.SessionLock)
        {
            RequestImmediateStop();
            PostToUi(() => ShowSystemStop("Остановлено при блокировке Windows"));
        }
    }

    private void RequestImmediateStop()
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

    private void ShowSystemStop(string message)
    {
        _nextEventAt = null;
        _nextEventCaption = string.Empty;
        SetRunState(AppRunState.Stopped);
        ToggleSettings(true);
        _startButton.ApplyState(false);
        _nextEventLabel.Text = message;
        _helperLabel.Text = "Клики не возобновятся автоматически";
    }

    private void UpdateHotKeyUi()
    {
        if (_hotKeyHelpLabel is null || _startButton is null || _captureButton is null)
        {
            return;
        }

        var toggleText = _toggleHotKeyRegistered ? "F6 Старт" : "F6 локально";
        var captureText = _captureHotKeyRegistered ? "F7 Точка" : "F7 локально";
        var emergencyText = _emergencyHotKeyRegistered
            ? $"{_emergencyHotKeyText.Replace(" + ", "+")} Стоп"
            : "Аварийная клавиша недоступна";
        _hotKeyHelpLabel.Text = _layoutProfile is LayoutProfile.Compact or LayoutProfile.Phone
            ? $"F6  •  F7  •  {_emergencyHotKeyText.Replace(" + ", "+")} Стоп"
            : $"{toggleText}  •  {captureText}  •  {emergencyText}";
        _hotKeyHelpLabel.ForeColor = _emergencyHotKeyRegistered ? Theme.Muted : Theme.Red;

        _startButton.HotKeyHint = _toggleHotKeyRegistered ? "F6" : string.Empty;
        _startButton.ApplyState(IsBusy);
        UpdateTargetUi();

        if (!_toggleHotKeyRegistered || !_captureHotKeyRegistered)
        {
            _helperLabel.Text = "Некоторые горячие клавиши доступны только в окне";
            _helperLabel.ForeColor = Theme.Amber;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs eventArgs)
    {
        _closing = true;
        _displayTimer.Stop();
        SystemEvents.PowerModeChanged -= SystemPowerModeChanged;
        SystemEvents.SessionSwitch -= SystemSessionSwitch;
        SavePreferences();
        _countdownCancellation?.Cancel();
        _engine.StopAsync().GetAwaiter().GetResult();
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
            LayoutProfile.Wide => 500,
            LayoutProfile.Compact => 625,
            _ => 829
        };
        var minimumClientSize = new Size(
            (int)Math.Ceiling(340 * scale),
            (int)Math.Ceiling(minimumHeight * scale));
        MinimumSize = SizeFromClientSize(minimumClientSize);
    }

    private static LayoutProfile GetLayoutProfile(float logicalWidth) => logicalWidth switch
    {
        >= 560 => LayoutProfile.Wide,
        >= 440 => LayoutProfile.Compact,
        _ => LayoutProfile.Phone
    };

    private void QueueLayoutRefresh()
    {
        if (_layoutRefreshQueued || !IsHandleCreated || _closing || IsDisposed)
        {
            return;
        }

        _layoutRefreshQueued = true;
        BeginInvoke((MethodInvoker)(() =>
        {
            _layoutRefreshQueued = false;
            if (_closing || IsDisposed)
            {
                return;
            }

            ApplyResponsiveLayout();
            PerformLayoutTree(this);
            Invalidate(true);
        }));
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
                    LayoutProfile.Compact => Math.Min(2, modeView.FieldCount),
                    _ => 1
                };
                ConfigureFieldGrid(modeView, columns);
            }
            ConfigureFieldGrid(_commonFieldGrid, profile switch
            {
                LayoutProfile.Wide => 3,
                LayoutProfile.Compact => 2,
                _ => 1
            });
            ConfigureFooterLayout(profile != LayoutProfile.Wide);
            UpdateHotKeyUi();
        }

        _contentPanel.AutoScroll = false;
        _contentPanel.AutoScrollMinSize = Size.Empty;
        _rootLayout.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _rootLayout.Dock = DockStyle.Fill;
        _contentPanel.PerformLayout();
        LayoutRootSections(profile);
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
            _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(72)));
            _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _footerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ScaleLogical(42)));
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
            _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(72)));
            _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _footerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScaleLogical(168)));
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
        var headerHeight = ScaleLogical(stacked ? 54 : 44);
        var stripHeight = ScaleLogical(stacked ? 44 : 42);
        var gap = ScaleLogical(8);
        var footerHeight = ScaleLogical(stacked ? 100 : 66);
        int modeHeight;
        int commonHeight;
        if (profile == LayoutProfile.Wide)
        {
            var available = Math.Max(
                1,
                _rootLayout.ClientSize.Height - headerHeight - stripHeight - footerHeight - (gap * 3));
            modeHeight = (int)Math.Round(available * 0.35);
            commonHeight = Math.Max(1, available - modeHeight);
        }
        else
        {
            modeHeight = ScaleLogical(profile == LayoutProfile.Compact ? 161 : 209);
            var baseCommonHeight = ScaleLogical(profile == LayoutProfile.Compact ? 222 : 378);
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
        MessageBox.Show(text, "Проверьте настройки", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private RoundedButton CreateModeButton(string text, ClickMode mode)
    {
        return new RoundedButton
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
    }

    private static FieldGrid CreateFieldGrid(int columns, params Control[] fields) =>
        new(columns, fields) { Dock = DockStyle.Fill };

    private static void ConfigureFieldGrid(FieldGrid grid, int columns)
    {
        grid.Columns = columns;
    }

    private static Control CreateField(string caption, Control input)
    {
        var field = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, Padding = new Padding(0, 1, 8, 1) };
        var label = CreateLabel(caption, 7.6F, FontStyle.Bold, Theme.Muted);
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
        return new Label
        {
            Text = text,
            Font = new Font("Segoe UI", size, style),
            ForeColor = color,
            BackColor = Color.Transparent,
            AutoEllipsis = true,
            UseMnemonic = false
        };
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
