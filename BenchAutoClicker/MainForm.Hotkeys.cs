namespace BenchAutoClicker;

internal sealed partial class MainForm
{
    private const int HotKeySpeed = 10;
    private const int HotKeyRandom = 11;
    private const int HotKeyBurst = 12;
    private HotkeySettings _hotkeys = HotkeySettings.Defaults;
    private readonly Dictionary<int, HotkeyGesture> _registeredHotkeys = [];
    private readonly HashSet<Keys> _localKeysDown = [];
    private bool _hotkeysSuspended;
    private bool _preferencesOpen;
    private bool _showingHotkeyWarning;

    private IEnumerable<(int Id, HotkeyGesture Gesture)> UserHotkeys(HotkeySettings settings)
    {
        var values = new[] { settings.Toggle, settings.Speed, settings.Random, settings.Burst };
        var ids = new[] { HotKeyToggle, HotKeySpeed, HotKeyRandom, HotKeyBurst };
        for (var index = 0; index < ids.Length; index++)
        {
            if (HotkeyGesture.TryParse(values[index], out var gesture)) yield return (ids[index], gesture);
        }
    }

    private bool RegisterGesture(int id, HotkeyGesture gesture)
    {
        if (_registeredHotkeys.TryGetValue(id, out var registered)) return registered == gesture;
        if (!IsHandleCreated || !NativeInterop.RegisterHotKey(Handle, id,
                gesture.Modifiers | ModNoRepeat, (uint)gesture.Key)) return false;
        _registeredHotkeys.Add(id, gesture);
        return true;
    }

    private void UnregisterGesture(int id)
    {
        if (_registeredHotkeys.Remove(id) && IsHandleCreated) NativeInterop.UnregisterHotKey(Handle, id);
    }

    private void UpdateRegistrationState()
    {
        _toggleHotKeyRegistered = _registeredHotkeys.ContainsKey(HotKeyToggle);
        _captureHotKeyRegistered = _registeredHotkeys.ContainsKey(HotKeyCapture);
        _emergencyHotKeyRegistered = _registeredHotkeys.ContainsKey(HotKeyEmergency);
        if (_hotkeysSuspended || _closing) return;
        UpdateHotKeyUi();
        RefreshShell();
    }

    private void RegisterAllHotkeys()
    {
        var emergency = new HotkeyGesture(HotkeyGesture.Control | HotkeyGesture.Shift, Keys.F6);
        if (!RegisterGesture(HotKeyEmergency, emergency))
        {
            emergency = emergency with { Key = Keys.F12 };
            RegisterGesture(HotKeyEmergency, emergency);
        }
        _emergencyHotKeyText = _registeredHotkeys.TryGetValue(HotKeyEmergency, out var actual)
            ? actual.ToString() : "Ctrl+Shift+F6";
        RegisterGesture(HotKeyCapture, new HotkeyGesture(0, Keys.F7));
        foreach (var (id, gesture) in UserHotkeys(_hotkeys)) RegisterGesture(id, gesture);
        UpdateRegistrationState();
    }

    private void UnregisterAllHotkeys()
    {
        foreach (var id in _registeredHotkeys.Keys.ToArray()) UnregisterGesture(id);
        _localKeysDown.Clear();
        UpdateRegistrationState();
    }

    private void SuspendHotkeys()
    {
        _hotkeysSuspended = true;
        UnregisterAllHotkeys();
    }

    private void ResumeHotkeys()
    {
        if (_closing || IsDisposed) return;
        _localKeysDown.Clear();
        RegisterAllHotkeys();
        _hotkeysSuspended = false;
        UpdateRegistrationState();
    }

    // The dialog probes availability without changing preferences or retaining registrations.
    private string? CheckHotkeyAvailability(HotkeySettings settings)
    {
        if (!settings.TryValidate(out var normalized, out var error)) return error;
        var probes = new List<int>();
        try
        {
            foreach (var (id, gesture) in UserHotkeys(normalized))
            {
                if (_registeredHotkeys.Values.Contains(gesture)) continue;
                var probeId = 100 + id;
                if (!NativeInterop.RegisterHotKey(Handle, probeId, gesture.Modifiers | ModNoRepeat, (uint)gesture.Key))
                    return UnavailableShortcut(gesture);
                probes.Add(probeId);
            }
            return null;
        }
        finally
        {
            foreach (var id in probes) NativeInterop.UnregisterHotKey(Handle, id);
        }
    }

    private bool TryApplyHotkeySettings(HotkeySettings settings, out string error)
    {
        if (!settings.TryValidate(out var normalized, out error)) return false;
        if (!IsHandleCreated)
        {
            _hotkeys = normalized;
            StoreHotkeyPreferences();
            return true;
        }

        var previous = _hotkeys;
        foreach (var (id, _) in UserHotkeys(previous)) UnregisterGesture(id);
        foreach (var (id, gesture) in UserHotkeys(normalized))
        {
            if (RegisterGesture(id, gesture)) continue;
            error = UnavailableShortcut(gesture);
            foreach (var (newId, _) in UserHotkeys(normalized)) UnregisterGesture(newId);
            var rollbackFailed = false;
            foreach (var (oldId, oldGesture) in UserHotkeys(previous))
                rollbackFailed |= !RegisterGesture(oldId, oldGesture);
            if (rollbackFailed)
                error += AppStrings.T(" Часть прежних сочетаний теперь доступна только в окне.",
                    " Some previous shortcuts are now available only in the app window.");
            UpdateRegistrationState();
            return false;
        }
        _hotkeys = normalized;
        StoreHotkeyPreferences();
        _localKeysDown.Clear();
        UpdateRegistrationState();
        return true;
    }

    private static string UnavailableShortcut(HotkeyGesture gesture) => AppStrings.T(
        $"{gesture} занято Windows или другой программой. Выберите другое сочетание.",
        $"{gesture} is in use by Windows or another app. Choose a different shortcut.");

    private void StoreHotkeyPreferences()
    {
        _preferences.ToggleHotkey = _hotkeys.Toggle;
        _preferences.SpeedHotkey = _hotkeys.Speed;
        _preferences.RandomHotkey = _hotkeys.Random;
        _preferences.BurstHotkey = _hotkeys.Burst;
    }

    private bool TrySelectMode(ClickMode mode)
    {
        if (_closing || _preferencesOpen || _hotkeysSuspended || IsBusy || !Enum.IsDefined(mode)) return false;
        SelectMode(mode);
        SettingsChanged();
        RefreshShell();
        return true;
    }

    private bool DispatchHotkey(int id, IntPtr lParam)
    {
        if (_closing || _hotkeysSuspended || _preferencesOpen || !_registeredHotkeys.TryGetValue(id, out var gesture))
            return false;
        // A queued WM_HOTKEY from the old binding must not activate its replacement.
        var packed = (long)lParam;
        if ((uint)(packed & 0xFFFF) != gesture.Modifiers || (Keys)((packed >> 16) & 0xFFFF) != gesture.Key)
            return false;
        return InvokeHotkey(id);
    }

    private bool InvokeHotkey(int id)
    {
        switch (id)
        {
            case HotKeyToggle: _ = ToggleRunningAsync(); return true;
            case HotKeyCapture: CapturePoint(); return true;
            case HotKeyEmergency: _ = EmergencyStopAsync(); return true;
            case HotKeySpeed: return TrySelectMode(ClickMode.Speed);
            case HotKeyRandom: return TrySelectMode(ClickMode.RandomInterval);
            case HotKeyBurst: return TrySelectMode(ClickMode.Burst);
            default: return false;
        }
    }

    private void HandleLocalHotkey(KeyEventArgs e)
    {
        if (_closing || _hotkeysSuspended || _preferencesOpen) return;
        var candidates = UserHotkeys(_hotkeys).Concat(new[]
        {
            (HotKeyCapture, new HotkeyGesture(0, Keys.F7)),
            (HotKeyEmergency, new HotkeyGesture(HotkeyGesture.Control | HotkeyGesture.Shift, Keys.F6))
        });
        foreach (var (id, gesture) in candidates)
        {
            if (_registeredHotkeys.ContainsKey(id) || !gesture.Matches(e.KeyData)) continue;
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (_localKeysDown.Add(e.KeyCode)) InvokeHotkey(id);
            return;
        }
    }
}
