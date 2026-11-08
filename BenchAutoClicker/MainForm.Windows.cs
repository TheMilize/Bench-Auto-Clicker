using Microsoft.Win32;

namespace BenchAutoClicker;

internal sealed partial class MainForm
{
    private RoundedButton _settingsButton = null!;
    private TrayController? _trayController;
    private bool _shellConfigured;
    private bool _allowMainWindow = true;
    private bool _exitRequested;
    private bool _themeRefreshQueued;

    internal void ConfigureShell(bool launchedAtLogin)
    {
        if (!_shellConfigured)
        {
            _shellConfigured = true;
            _trayController = new TrayController(CreateTraySnapshot,
                () => _ = ToggleRunningAsync(),
                mode => TrySelectMode(mode), ShowMainSettings, ExitApplication);
            SystemEvents.UserPreferenceChanged += WindowsUserPreferenceChanged;
        }
        _trayController!.SetVisible(_preferences.TrayMode);
        // Startup only launches the app. It never changes the clicker's stopped state.
        if (_preferences.TrayMode)
        {
            _allowMainWindow = false;
            ShowInTaskbar = false;
            if (Visible)
            {
                Hide();
            }
        }
        Theme.ApplyTo(this);
    }

    internal void ShowMainSettings()
    {
        if (_closing || IsDisposed)
        {
            return;
        }
        _allowMainWindow = true;
        ShowInTaskbar = true;
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        Show();
        Activate();
        BringToFront();
    }

    internal void HideToTray()
    {
        if (!_shellConfigured || !_preferences.TrayMode || _closing)
        {
            return;
        }
        _allowMainWindow = false;
        _trayController!.SetVisible(true);
        Hide();
        ShowInTaskbar = false;
    }

    protected override void SetVisibleCore(bool value)
    {
        if (value && _shellConfigured && !_allowMainWindow)
        {
            if (!IsHandleCreated)
            {
                CreateHandle();
            }
            base.SetVisibleCore(false);
            return;
        }
        base.SetVisibleCore(value);
    }

    private TraySnapshot CreateTraySnapshot() => new(_selectedMode,
        _statusPill.State == AppRunState.Running,
        _statusPill.State == AppRunState.Countdown,
        _engineStartedForSession ? _engine.TotalClicks : 0,
        _nextEventLabel.Text, _toggleHotKeyRegistered, _emergencyHotKeyText.Replace(" + ", "+"), _hotkeys);

    private void RefreshShell()
    {
        _trayController?.Refresh();
        if (_settingsButton is not null)
        {
            _settingsButton.AccessibleName = AppStrings.T("Настройки приложения", "Application settings");
            _toolTip.SetToolTip(_settingsButton, _settingsButton.AccessibleName);
        }
    }

    private async void ShowApplicationPreferences()
    {
        if (_preferencesOpen || _closing) return;
        _preferencesOpen = true;
        RequestImmediateStop();
        await _transition.WaitAsync();
        try
        {
            if (_closing) return;
            if (IsBusy || _sessionActive)
                await StopAllAsync(AppStrings.T("Остановлено для настройки клавиш", "Stopped to configure shortcuts"));
            SuspendHotkeys();
            var launchAtLogin = _preferences.LaunchAtLogin;
            if (_persistSettings)
            {
                try
                {
                    launchAtLogin = WindowsStartup.ReadEnabled();
                    _preferences.LaunchAtLogin = launchAtLogin;
                }
                catch (InvalidOperationException exception)
                {
                    MessageBox.Show(this, exception.Message, "Bench Auto Clicker",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            using var dialog = new PreferencesForm(new AppPreferences(_preferences.Language,
                launchAtLogin, _preferences.TrayMode, _hotkeys), CheckHotkeyAvailability);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                ApplyAppPreferences(dialog.Result);
            }
            catch (InvalidOperationException exception)
            {
                MessageBox.Show(this, exception.Message, "Bench Auto Clicker",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        finally
        {
            _preferencesOpen = false;
            ResumeHotkeys();
            _transition.Release();
        }
    }

    internal void ApplyAppPreferences(AppPreferences preferences)
    {
        var previousHotkeys = _hotkeys;
        if (!TryApplyHotkeySettings(preferences.Hotkeys ?? _hotkeys, out var error))
            throw new InvalidOperationException(error);
        // Tests use persistSettings:false and can exercise the complete UI without touching HKCU.
        try
        {
            if (_persistSettings && (preferences.LaunchAtLogin || _preferences.LaunchAtLogin != preferences.LaunchAtLogin))
            {
                WindowsStartup.SetEnabled(preferences.LaunchAtLogin);
            }
        }
        catch (InvalidOperationException exception)
        {
            TryApplyHotkeySettings(previousHotkeys, out var rollbackError);
            throw new InvalidOperationException(exception.Message + (string.IsNullOrEmpty(rollbackError) ? string.Empty : " " + rollbackError), exception);
        }
        _preferences.Language = AppStrings.NormalizeLanguage(preferences.Language);
        _preferences.LaunchAtLogin = preferences.LaunchAtLogin;
        _preferences.TrayMode = preferences.TrayMode;
        SetLanguage(_preferences.Language);
        if (!_shellConfigured)
        {
            ConfigureShell(false);
        }
        _trayController!.RefreshAppearance();
        _trayController.SetVisible(_preferences.TrayMode);
        if (_preferences.TrayMode)
        {
            HideToTray();
        }
        else
        {
            ShowMainSettings();
        }
        SavePreferences();
    }

    internal void ExitApplication()
    {
        _exitRequested = true;
        RequestImmediateStop();
        Close();
    }

    private void WindowsUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        QueueWindowsThemeRefresh();

    private void QueueWindowsThemeRefresh()
    {
        if (!_shellConfigured || _closing || !IsHandleCreated)
        {
            return;
        }
        PostToUi(() =>
        {
            if (_themeRefreshQueued)
            {
                return;
            }
            _themeRefreshQueued = true;
            try
            {
                if (Theme.RefreshFromWindows())
                {
                    foreach (Form window in Application.OpenForms.Cast<Form>().ToArray())
                    {
                        Theme.ApplyTo(window);
                        if (window is PreferencesForm preferences)
                        {
                            preferences.RefreshAppearance();
                        }
                    }
                    foreach (var view in _modeViews)
                    {
                        Theme.ApplyTo(view);
                    }
                    _trayController?.RefreshAppearance();
                }
            }
            finally
            {
                _themeRefreshQueued = false;
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_shellConfigured)
            {
                SystemEvents.UserPreferenceChanged -= WindowsUserPreferenceChanged;
                _shellConfigured = false;
            }
            _trayController?.Dispose();
            _trayController = null;
            if (!_closing)
            {
                _closing = true;
                SystemEvents.PowerModeChanged -= SystemPowerModeChanged;
                SystemEvents.SessionSwitch -= SystemSessionSwitch;
                RequestImmediateStop();
                _engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _displayTimer.Dispose();
                _saveTimer.Dispose();
                _toolTip.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}
