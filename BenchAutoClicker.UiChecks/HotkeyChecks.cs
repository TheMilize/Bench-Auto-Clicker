using BenchAutoClicker;
using System.Reflection;
using System.Text.Json;
using System.Runtime.ExceptionServices;

internal static class HotkeyChecks
{
    public static async Task RunAsync(MainForm main, string folder, List<string> report)
    {
        CheckModel();
        CheckRecorder();
        await CheckLifecycleAsync();

        void CheckModel()
        {
            Check(HotkeySettings.Defaults.TryValidate(out var defaults, out _) &&
                defaults == HotkeySettings.Defaults, "Default shortcuts are valid and canonical");

            Check(HotkeyGesture.TryParse(" ctrl + shift + 1 ", out var mode) &&
                mode.Key == Keys.D1 && mode.ToString() == "Ctrl+Shift+1",
                "Shortcut parsing normalizes case, whitespace and number keys");
            Check(mode.Matches(Keys.Control | Keys.Shift | Keys.D1) &&
                !mode.Matches(Keys.Control | Keys.D1) &&
                !mode.Matches(Keys.Control | Keys.Shift | Keys.Alt | Keys.D1) &&
                !mode.Matches(Keys.Control | Keys.Shift | Keys.NumPad1),
                "Shortcut matching requires the exact key and modifier set");

            foreach (var text in new[] { "F6", "Ctrl+Shift+1", "Ctrl+Alt+F13", "Alt+F24" })
            {
                Check(HotkeyGesture.TryParse(text, out var gesture) &&
                    HotkeyGesture.TryParse(gesture.ToString(), out var restored) && restored == gesture,
                    $"Shortcut {text} survives its canonical serialization");
            }
            foreach (var text in new[] { "", "Ctrl+Shift", "Ctrl+F25", "Ctrl+A+B", "Win+F8" })
            {
                Check(!HotkeyGesture.TryParse(text, out _), $"Malformed shortcut '{text}' is rejected");
            }
            Check(!HotkeyGesture.TryFromKeys(Keys.A, out _, out _) &&
                !HotkeyGesture.TryFromKeys(Keys.D1, out _, out _) &&
                !HotkeyGesture.TryFromKeys(Keys.Shift | Keys.A, out _, out _) &&
                !HotkeyGesture.TryFromKeys(Keys.LWin, out _, out _) &&
                !HotkeyGesture.TryFromKeys(Keys.ControlKey, out _, out _) &&
                HotkeyGesture.TryFromKeys(Keys.Control | Keys.Shift | Keys.D1, out _, out _) &&
                HotkeyGesture.TryFromKeys(Keys.F24, out _, out _),
                "Capture accepts function keys or modified keys without binding ordinary typing");

            var duplicate = new HotkeySettings("f6", "F6", "Ctrl+Shift+2", "Ctrl+Shift+3");
            Check(!duplicate.TryValidate(out _, out var duplicateError) && !string.IsNullOrWhiteSpace(duplicateError),
                "Duplicate shortcuts are detected after normalization");
            foreach (var reserved in new[] { "F7", "Ctrl+Shift+F6", "Ctrl+Shift+F12" })
            {
                var invalid = HotkeySettings.Defaults with { Toggle = reserved };
                Check(!invalid.TryValidate(out _, out var error) && !string.IsNullOrWhiteSpace(error),
                    $"Reserved point/emergency shortcut {reserved} cannot be reassigned");
            }
            Check((HotkeySettings.Defaults with { Toggle = "Ctrl+Shift+F7" }).TryValidate(out _, out _),
                "Reserved shortcuts do not incorrectly exclude combinations with different modifiers");
            Check((HotkeySettings.Defaults with { Toggle = "broken" }).Normalize() == HotkeySettings.Defaults,
                "Damaged saved shortcut settings recover to a complete valid default set");
            var serialized = JsonSerializer.Serialize(defaults);
            Check(JsonSerializer.Deserialize<HotkeySettings>(serialized) == defaults,
                "Shortcut settings preserve every binding through JSON");
            var legacy = JsonSerializer.Deserialize<UserPreferences>("{}")!;
            Check(legacy.ToggleHotkey == defaults.Toggle && legacy.SpeedHotkey == defaults.Speed &&
                legacy.RandomHotkey == defaults.Random && legacy.BurstHotkey == defaults.Burst,
                "Settings files from older versions receive all four default shortcuts");
        }

        void CheckRecorder()
        {
            using var box = new HotkeyBox { Value = "F6" };
            var changes = 0;
            box.ValueChanged += (_, _) => changes++;
            box.BeginCapture();
            KeyDown(Keys.Control | Keys.ControlKey);
            Check(box.IsCapturing && box.Value == "F6" && changes == 0,
                "Holding a modifier previews recording without committing a shortcut");
            KeyDown(Keys.A);
            Check(box.IsCapturing && box.Value == "F6" && box.CaptureError.Length > 0 && changes == 0,
                "Invalid typing leaves recording active with an explanatory error");
            var escape = KeyDown(Keys.Escape);
            Check(!box.IsCapturing && box.Value == "F6" && changes == 0 && escape.SuppressKeyPress,
                "Escape cancels shortcut recording without changing the saved value");
            box.BeginCapture();
            var accepted = KeyDown(Keys.Control | Keys.Alt | Keys.F9);
            KeyDown(Keys.Control | Keys.Alt | Keys.F9);
            Check(!box.IsCapturing && box.Value == "Ctrl+Alt+F9" && changes == 1 && accepted.SuppressKeyPress,
                "Recording commits once and repeated keydown cannot change it again");
            box.BeginCapture();
            typeof(HotkeyBox).GetMethod("OnLostFocus", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(box, [EventArgs.Empty]);
            Check(!box.IsCapturing && box.Value == "Ctrl+Alt+F9",
                "Losing focus cancels incomplete shortcut recording");
            box.Enabled = false;
            box.BeginCapture();
            Check(!box.IsCapturing, "A disabled shortcut field cannot start recording");

            KeyEventArgs KeyDown(Keys keys)
            {
                var e = new KeyEventArgs(keys);
                typeof(HotkeyBox).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(box, [e]);
                return e;
            }
        }

        async Task CheckLifecycleAsync()
        {
            var original = (HotkeySettings)Field("_hotkeys");
            var originalMode = (ClickMode)Field("_selectedMode");
            var preferences = (UserPreferences)Field("_preferences");
            var originalLanguage = preferences.Language;
            var delay = (NumberBox)Field("_startDelay");
            var limit = (NumberBox)Field("_maxClicks");
            var duration = (NumberBox)Field("_maxDuration");
            var target = (SelectBox)Field("_targetMode");
            var oldDelay = delay.Value;
            var oldLimit = limit.Value;
            var oldDuration = duration.Value;
            var oldTarget = target.SelectedIndex;
            var engine = (ClickEngine)Field("_engine");
            var custom = new HotkeySettings("Ctrl+Alt+Shift+F13", "Ctrl+Alt+Shift+F14",
                "Ctrl+Alt+Shift+F15", "Ctrl+Alt+Shift+F16");
            try
            {
                await (Task)Call("EmergencyStopAsync")!;
                Check(TryApply(custom, out var applyError), "Custom global shortcuts register: " + applyError);
                Check((HotkeySettings)Field("_hotkeys") == custom, "Successful registration commits the complete shortcut set");
                foreach (var (id, mode, text) in new[]
                {
                    (10, ClickMode.Speed, custom.Speed), (11, ClickMode.RandomInterval, custom.Random),
                    (12, ClickMode.Burst, custom.Burst)
                })
                {
                    Check(Dispatch(id, text) && (ClickMode)Field("_selectedMode") == mode,
                        $"Registered shortcut selects {mode} while stopped");
                }
                Check(!Dispatch(10, HotkeySettings.Defaults.Speed) &&
                    (ClickMode)Field("_selectedMode") == ClickMode.Burst,
                    "Queued shortcut messages from an old binding are ignored after reassignment");
                Check(!Dispatch(10, "Ctrl+Alt+F14") && !Dispatch(99, custom.Speed),
                    "Global dispatch rejects incorrect modifiers and unknown IDs");

                Call("SuspendHotkeys");
                Check(!Dispatch(1, custom.Toggle) && !Dispatch(10, custom.Speed) &&
                    !Dispatch(3, "Ctrl+Shift+F6") && !engine.IsRunning,
                    "Settings suspension blocks queued start, mode and emergency shortcut dispatch");
                Check(TryApply(custom, out _) && !Dispatch(1, custom.Toggle) && !engine.IsRunning,
                    "Committing shortcuts inside settings keeps their actions suspended until the dialog closes");
                Call("ResumeHotkeys");
                Check(Dispatch(10, custom.Speed) && (ClickMode)Field("_selectedMode") == ClickMode.Speed,
                    "Closing shortcut settings restores global mode bindings");

                using (var occupied = ReservedShortcut.Create("Ctrl+Alt+Shift+F24"))
                {
                    var conflicting = custom with { Toggle = occupied.Gesture.ToString() };
                    Check(!TryApply(conflicting, out var conflictError) && conflictError.Length > 0,
                        "An occupied global shortcut rejects the proposed set with an explanation");
                    Check((HotkeySettings)Field("_hotkeys") == custom && Dispatch(12, custom.Burst),
                        "Failed registration restores the previous configuration and mode bindings");
                    using var probe = ReservedShortcut.TryCreate(custom.Toggle);
                    Check(probe is null, "Failed registration really restores the previous native toggle binding");
                }
                var preview = custom with { Toggle = "Ctrl+Alt+Shift+F23" };
                Check(Call("CheckHotkeyAvailability", preview) is null && (HotkeySettings)Field("_hotkeys") == custom,
                    "Shortcut availability preview leaves the active configuration unchanged");
                using (var previewReleased = ReservedShortcut.Create(preview.Toggle))
                {
                    Check(true, "Availability preview releases every temporary native registration");
                }

                delay.Value = 0;
                limit.Value = 0;
                duration.Value = 0;
                target.SelectedIndex = (int)TargetMode.Cursor;
                KeyEvent("MainFormKeyDown", custom.Toggle);
                await Task.Delay(30);
                Check(!engine.IsRunning, "Local key handling does not duplicate an already registered global shortcut");

                await (Task)Call("ToggleRunningAsync")!;
                Check(engine.IsRunning, "The lifecycle test starts only the injected fake-input engine");
                var runningMode = (ClickMode)Field("_selectedMode");
                Dispatch(10, custom.Speed);
                Check((ClickMode)Field("_selectedMode") == runningMode &&
                    !(bool)Call("TrySelectMode", ClickMode.RandomInterval)!,
                    "Mode shortcuts and shared mode selection are guarded during an active session");
                await (Task)Call("EmergencyStopAsync")!;

                delay.Value = 1;
                var countdown = (Task)Call("ToggleRunningAsync")!;
                await Task.Delay(30);
                Check(FieldOrNull("_countdownCancellation") is not null, "Countdown begins before the fake-input session");
                Dispatch(10, custom.Speed);
                Check((ClickMode)Field("_selectedMode") == runningMode &&
                    !(bool)Call("TrySelectMode", ClickMode.RandomInterval)!,
                    "Mode shortcuts and shared mode selection are guarded during countdown");
                await (Task)Call("EmergencyStopAsync")!;
                await countdown;
                Check(!engine.IsRunning && FieldOrNull("_countdownCancellation") is null,
                    "Emergency cancellation leaves no delayed session behind");

                delay.Value = 0;
                Call("SuspendHotkeys");
                using (var occupied = ReservedShortcut.Create(custom.Toggle))
                {
                    Call("ResumeHotkeys");
                    Check(!(bool)Field("_toggleHotKeyRegistered") && !Dispatch(1, custom.Toggle),
                        "An unavailable binding is honestly marked local and ignores global messages");
                    KeyEvent("MainFormKeyUp", custom.Toggle);
                    KeyEvent("MainFormKeyDown", custom.Toggle);
                    await WaitForAsync(() => engine.IsRunning);
                    KeyEvent("MainFormKeyDown", custom.Toggle);
                    await Task.Delay(40);
                    Check(engine.IsRunning, "Local shortcut fallback ignores keyboard autorepeat");
                    KeyEvent("MainFormKeyUp", custom.Toggle);
                    KeyEvent("MainFormKeyDown", custom.Toggle);
                    await WaitForAsync(() => !engine.IsRunning);
                    KeyEvent("MainFormKeyDown", custom.Toggle);
                    await Task.Delay(40);
                    Check(!engine.IsRunning, "A repeated stop key cannot restart the session until released");
                    KeyEvent("MainFormKeyUp", custom.Toggle);
                }
                Call("SuspendHotkeys");
                Call("ResumeHotkeys");
                Check((bool)Field("_toggleHotKeyRegistered"), "A released shortcut can register again after settings close");
                foreach (var language in new[] { "en", "ru" })
                {
                    main.SetLanguage(language);
                    Check(((PrimaryButton)Field("_startButton")).HotKeyHint == custom.Toggle,
                        $"The start button shows the customized shortcut in {language}");
                }
            }
            finally
            {
                await (Task)Call("EmergencyStopAsync")!;
                if ((bool)Field("_hotkeysSuspended")) Call("ResumeHotkeys");
                TryApply(original, out _);
                delay.Value = oldDelay;
                limit.Value = oldLimit;
                duration.Value = oldDuration;
                target.SelectedIndex = oldTarget;
                Call("TrySelectMode", originalMode);
                main.SetLanguage(originalLanguage);
            }

            bool TryApply(HotkeySettings settings, out string error)
            {
                object?[] args = [settings, string.Empty];
                var applied = (bool)typeof(MainForm).GetMethod("TryApplyHotkeySettings", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(main, args)!;
                error = (string)args[1]!;
                return applied;
            }
            bool Dispatch(int id, string shortcut)
            {
                if (!HotkeyGesture.TryParse(shortcut, out var gesture)) throw new InvalidOperationException(shortcut);
                var lParam = new IntPtr(((int)gesture.Key << 16) | (int)gesture.Modifiers);
                return (bool)Call("DispatchHotkey", id, lParam)!;
            }
            void KeyEvent(string method, string shortcut)
            {
                HotkeyGesture.TryParse(shortcut, out var gesture);
                var data = gesture.Key
                    | ((gesture.Modifiers & HotkeyGesture.Control) != 0 ? Keys.Control : Keys.None)
                    | ((gesture.Modifiers & HotkeyGesture.Alt) != 0 ? Keys.Alt : Keys.None)
                    | ((gesture.Modifiers & HotkeyGesture.Shift) != 0 ? Keys.Shift : Keys.None);
                Call(method, main, new KeyEventArgs(data));
            }
        }

        object Field(string name) => FieldOrNull(name)!;
        object? FieldOrNull(string name) => typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(main);
        object? Call(string name, params object[] args)
        {
            try
            {
                return typeof(MainForm).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        void Check(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
            report.Add("PASS " + message);
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(10);
        if (!condition()) throw new InvalidOperationException("Shortcut action did not complete before timeout");
    }

    // Reserve on another thread so Windows reports a real competing owner. No input is generated.
    private sealed class ReservedShortcut : IDisposable
    {
        private readonly ManualResetEventSlim _ready = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly Thread _thread;
        private bool _registered;
        private bool _disposed;
        public HotkeyGesture Gesture { get; }

        private ReservedShortcut(HotkeyGesture gesture)
        {
            Gesture = gesture;
            _thread = new Thread(() =>
            {
                _registered = NativeInterop.RegisterHotKey(IntPtr.Zero, 901, gesture.Modifiers | 0x4000, (uint)gesture.Key);
                _ready.Set();
                _release.Wait();
                if (_registered) NativeInterop.UnregisterHotKey(IntPtr.Zero, 901);
            }) { IsBackground = true, Name = "Hotkey conflict diagnostic" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            if (!_ready.Wait(TimeSpan.FromSeconds(5)))
            {
                Dispose();
                throw new TimeoutException("Native shortcut reservation timed out");
            }
        }

        public static ReservedShortcut Create(string shortcut) => TryCreate(shortcut)
            ?? throw new InvalidOperationException("Diagnostic shortcut is already occupied: " + shortcut);

        public static ReservedShortcut? TryCreate(string shortcut)
        {
            if (!HotkeyGesture.TryParse(shortcut, out var gesture)) throw new ArgumentException(shortcut);
            var reservation = new ReservedShortcut(gesture);
            if (reservation._registered) return reservation;
            reservation.Dispose();
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _release.Set();
            if (_thread.Join(TimeSpan.FromSeconds(5)))
            {
                _ready.Dispose();
                _release.Dispose();
            }
        }
    }
}
