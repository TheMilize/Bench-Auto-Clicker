using System.Globalization;

namespace BenchAutoClicker;

/// <summary>A keyboard shortcut in the same modifier format used by RegisterHotKey.</summary>
internal readonly record struct HotkeyGesture(uint Modifiers, Keys Key)
{
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint Shift = 0x0004;

    public static bool TryParse(string? value, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        uint modifiers = 0;
        Keys key = Keys.None;
        foreach (var part in value.Split('+', StringSplitOptions.TrimEntries))
        {
            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => Control,
                "ALT" => Alt,
                "SHIFT" => Shift,
                _ => 0u
            };
            if (modifier != 0)
            {
                if ((modifiers & modifier) != 0) return false;
                modifiers |= modifier;
                continue;
            }
            if (key != Keys.None || !TryParseKey(part, out key)) return false;
        }
        if (!IsSupported(modifiers, key)) return false;
        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }

    public static bool TryFromKeys(Keys keyData, out HotkeyGesture gesture, out string error)
    {
        gesture = default;
        var key = keyData & Keys.KeyCode;
        var modifiers = ((keyData & Keys.Control) != 0 ? Control : 0u)
            | ((keyData & Keys.Alt) != 0 ? Alt : 0u)
            | ((keyData & Keys.Shift) != 0 ? Shift : 0u);
        if (!IsSupported(modifiers, key))
        {
            error = AppStrings.T("Используйте F1–F24 или Ctrl / Alt с другой клавишей.",
                "Use F1–F24 or Ctrl / Alt with another key.");
            return false;
        }
        gesture = new HotkeyGesture(modifiers, key);
        error = string.Empty;
        return true;
    }

    public bool Matches(Keys keyData) => TryFromKeys(keyData, out var candidate, out _) && this == candidate;

    public bool IsReserved => this == new HotkeyGesture(0, Keys.F7)
        || this == new HotkeyGesture(Control | Shift, Keys.F6)
        || this == new HotkeyGesture(Control | Shift, Keys.F12);

    public override string ToString()
    {
        var parts = new List<string>(4);
        if ((Modifiers & Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & Alt) != 0) parts.Add("Alt");
        if ((Modifiers & Shift) != 0) parts.Add("Shift");
        parts.Add(KeyName(Key));
        return string.Join('+', parts);
    }

    private static bool TryParseKey(string part, out Keys key)
    {
        key = Keys.None;
        if (part.Length == 1)
        {
            var character = char.ToUpperInvariant(part[0]);
            if (character is >= 'A' and <= 'Z') key = Keys.A + (character - 'A');
            else if (character is >= '0' and <= '9') key = Keys.D0 + (character - '0');
            return key != Keys.None;
        }
        if (part.StartsWith('F') || part.StartsWith('f'))
        {
            if (int.TryParse(part.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                && number is >= 1 and <= 24)
            {
                key = Keys.F1 + number - 1;
                return true;
            }
        }
        return Enum.TryParse(part, true, out key) && (key & Keys.KeyCode) == key && IsKeySupported(key);
    }

    private static bool IsSupported(uint modifiers, Keys key) => (modifiers & ~(Alt | Control | Shift)) == 0
        && IsKeySupported(key)
        && (key is >= Keys.F1 and <= Keys.F24 || (modifiers & (Control | Alt)) != 0);

    private static bool IsKeySupported(Keys key) => key is >= Keys.F1 and <= Keys.F24
        or >= Keys.A and <= Keys.Z or >= Keys.D0 and <= Keys.D9
        or >= Keys.NumPad0 and <= Keys.Divide
        or Keys.Space or Keys.Insert or Keys.Delete or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown
        or Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Back
        or Keys.Oemcomma or Keys.OemPeriod or Keys.OemMinus or Keys.Oemplus
        or Keys.OemQuestion or Keys.OemSemicolon or Keys.OemQuotes or Keys.OemOpenBrackets
        or Keys.OemCloseBrackets or Keys.OemPipe or Keys.Oemtilde or Keys.OemBackslash;

    private static string KeyName(Keys key) => key is >= Keys.D0 and <= Keys.D9
        ? ((int)key - (int)Keys.D0).ToString(CultureInfo.InvariantCulture)
        : key.ToString();
}

internal sealed record HotkeySettings(string Toggle, string Speed, string Random, string Burst)
{
    public static HotkeySettings Defaults { get; } = new("F6", "Ctrl+Shift+1", "Ctrl+Shift+2", "Ctrl+Shift+3");

    public HotkeySettings Normalize() => TryValidate(out var normalized, out _) ? normalized : Defaults;

    public bool TryValidate(out HotkeySettings normalized, out string error)
    {
        normalized = this;
        var values = new[] { Toggle, Speed, Random, Burst };
        var gestures = new HotkeyGesture[values.Length];
        var labels = new[]
        {
            AppStrings.T("Запуск / стоп", "Start / stop"), AppStrings.T("Скорость", "Speed"),
            AppStrings.T("Случайно", "Random"), AppStrings.T("Серии", "Burst")
        };
        for (var index = 0; index < values.Length; index++)
        {
            if (!HotkeyGesture.TryParse(values[index], out gestures[index]))
            {
                error = AppStrings.T($"«{labels[index]}»: задайте F-клавишу или сочетание с Ctrl / Alt.",
                    $"“{labels[index]}”: choose an F-key or a shortcut with Ctrl / Alt.");
                return false;
            }
            if (gestures[index].IsReserved)
            {
                error = AppStrings.T($"{gestures[index]} зарезервировано для точки или аварийной остановки.",
                    $"{gestures[index]} is reserved for point capture or emergency stop.");
                return false;
            }
            for (var earlier = 0; earlier < index; earlier++)
            {
                if (gestures[earlier] != gestures[index]) continue;
                error = AppStrings.T($"«{labels[earlier]}» и «{labels[index]}» используют одинаковое сочетание.",
                    $"“{labels[earlier]}” and “{labels[index]}” use the same shortcut.");
                return false;
            }
        }
        normalized = new HotkeySettings(gestures[0].ToString(), gestures[1].ToString(),
            gestures[2].ToString(), gestures[3].ToString());
        error = string.Empty;
        return true;
    }
}
