using Microsoft.Win32;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BenchAutoClicker;

internal static class Theme
{
    private enum Role
    {
        Window, Sidebar, Card, CardHover, Input, InputHover, Border, Hairline,
        Text, Muted, Subtle, Accent, AccentHover, AccentDeep, AccentSoft, Green, Red, Amber
    }

    private sealed record Palette(bool Dark, bool SystemDark, bool HighContrast, Color[] Colors);
    private sealed record ColorBinding(Role Role, int LastAssigned);
    private static readonly ConditionalWeakTable<Control, Dictionary<string, ColorBinding>> Bindings = new();
    private static Palette _current = CreatePalette(true, true, false, Color.FromArgb(0, 120, 212));
    private static Palette _previous = _current;

    public static bool IsDark => _current.Dark;
    public static bool SystemIsDark => _current.SystemDark;
    public static bool IsTaskbarDark => _current.SystemDark;
    public static bool HighContrast => _current.HighContrast;
    public static Color Window => Get(Role.Window);
    public static Color Sidebar => Get(Role.Sidebar);
    public static Color Card => Get(Role.Card);
    public static Color CardHover => Get(Role.CardHover);
    public static Color Input => Get(Role.Input);
    public static Color InputHover => Get(Role.InputHover);
    public static Color Border => Get(Role.Border);
    public static Color Hairline => Get(Role.Hairline);
    public static Color Text => Get(Role.Text);
    public static Color Muted => Get(Role.Muted);
    public static Color Subtle => Get(Role.Subtle);
    public static Color Accent => Get(Role.Accent);
    public static Color AccentHover => Get(Role.AccentHover);
    public static Color AccentDeep => Get(Role.AccentDeep);
    public static Color AccentText => Get(Role.AccentDeep);
    public static Color AccentSoft => Get(Role.AccentSoft);
    public static Color Green => Get(Role.Green);
    public static Color Red => Get(Role.Red);
    public static Color Amber => Get(Role.Amber);
    public static Color OnAccent => ContrastText(Accent);

    private static Color Get(Role role) => _current.Colors[(int)role];

    // Call on the UI thread, then ApplyTo each existing window. The owner subscribes
    // to UserPreferenceChanged / WM_THEMECHANGED and owns that subscription's lifetime.
    public static bool RefreshFromWindows()
    {
        var dark = false;
        var systemDark = true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            dark = key?.GetValue("AppsUseLightTheme") is int appValue && appValue == 0;
            systemDark = key?.GetValue("SystemUsesLightTheme") is not int systemValue || systemValue == 0;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // A restricted Windows account can still use the default accessible palette.
        }

        var accent = Color.FromArgb(0, 120, 212);
        try
        {
            if (DwmGetColorizationColor(out var color, out _) >= 0)
            {
                accent = Color.FromArgb((int)((color >> 16) & 255), (int)((color >> 8) & 255), (int)(color & 255));
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            // DWM composition is optional; retain the Windows blue fallback.
        }
        return SetPalette(CreatePalette(dark, systemDark, SystemInformation.HighContrast, accent));
    }

    internal static void SetForTesting(bool dark, Color accent, bool highContrast = false) =>
        SetPalette(CreatePalette(dark, dark, highContrast, accent));

    private static bool SetPalette(Palette palette)
    {
        if (_current.Dark == palette.Dark && _current.SystemDark == palette.SystemDark &&
            _current.HighContrast == palette.HighContrast &&
            _current.Colors.Select(color => color.ToArgb()).SequenceEqual(palette.Colors.Select(color => color.ToArgb())))
        {
            return false;
        }
        _previous = _current;
        _current = palette;
        return true;
    }

    public static void ApplyTo(Control root)
    {
        if (root.IsDisposed)
        {
            return;
        }
        root.SuspendLayout();
        try
        {
            ApplyCore(root);
        }
        finally
        {
            root.ResumeLayout(true);
        }
        root.Invalidate(true);
    }

    private static void ApplyCore(Control root)
    {
        if (root.IsDisposed)
        {
            return;
        }
        var bindings = Bindings.GetOrCreateValue(root);
        Bind("BackColor", () => root.BackColor, value => root.BackColor = value);
        Bind("ForeColor", () => root.ForeColor, value => root.ForeColor = value);
        if (root is CardPanel card)
        {
            Bind("BorderColor", () => card.BorderColor, value => card.BorderColor = value);
        }
        if (root is RoundedButton rounded)
        {
            Bind("FillColor", () => rounded.FillColor, value => rounded.FillColor = value);
            Bind("HoverColor", () => rounded.HoverColor, value => rounded.HoverColor = value);
            Bind("SelectedColor", () => rounded.SelectedColor, value => rounded.SelectedColor = value);
            Bind("BorderColor", () => rounded.BorderColor, value => rounded.BorderColor = value);
        }
        if (root is PrimaryButton primary)
        {
            primary.RefreshThemeColors();
        }
        if (root is SelectBox select)
        {
            select.RefreshThemeColors();
        }
        if (root is Form form && form.IsHandleCreated)
        {
            NativeInterop.ApplyWindowFrame(form.Handle, IsDark && !HighContrast);
        }
        if (root.ContextMenuStrip is { } menu)
        {
            menu.BackColor = Card;
            menu.ForeColor = Text;
            menu.Renderer = new DarkMenuRenderer();
        }
        foreach (Control child in root.Controls)
        {
            ApplyCore(child);
        }

        void Bind(string property, Func<Color> read, Action<Color> write)
        {
            var color = read();
            if (color.A == 0)
            {
                return;
            }
            Role? role = null;
            if (bindings.TryGetValue(property, out var binding) && binding.LastAssigned == color.ToArgb())
            {
                role = binding.Role;
            }
            else
            {
                role = FindRole(color, _current, property == "ForeColor") ??
                    FindRole(color, _previous, property == "ForeColor");
            }
            if (role is { } actual)
            {
                var replacement = Get(actual);
                if (color.ToArgb() != replacement.ToArgb())
                {
                    write(replacement);
                }
                bindings[property] = new ColorBinding(actual, replacement.ToArgb());
            }
        }
    }

    private static Role? FindRole(Color color, Palette palette, bool foreground)
    {
        // Some system palettes intentionally reuse the same color for surfaces and
        // text. Give foreground roles priority so high-contrast transitions never
        // turn a label into a background-colored (invisible) label.
        if (foreground)
        {
            foreach (var role in new[] { Role.Text, Role.Muted, Role.Subtle, Role.Green,
                         Role.Red, Role.Amber, Role.AccentDeep, Role.AccentHover, Role.Accent })
            {
                if (palette.Colors[(int)role].ToArgb() == color.ToArgb())
                {
                    return role;
                }
            }
        }
        for (var index = 0; index < palette.Colors.Length; index++)
        {
            if (palette.Colors[index].ToArgb() == color.ToArgb())
            {
                return (Role)index;
            }
        }
        return null;
    }

    private static Palette CreatePalette(bool dark, bool systemDark, bool highContrast, Color accent)
    {
        if (highContrast)
        {
            return new Palette(dark, systemDark, true,
            [SystemColors.Window, SystemColors.Window, SystemColors.Window, SystemColors.Control,
                SystemColors.Window, SystemColors.Control, SystemColors.WindowText, SystemColors.WindowText,
                SystemColors.WindowText, SystemColors.WindowText, SystemColors.GrayText,
                SystemColors.Highlight, SystemColors.Highlight, SystemColors.HotTrack,
                SystemColors.Control, SystemColors.WindowText, SystemColors.WindowText, SystemColors.WindowText]);
        }
        var window = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
        var card = dark ? Color.FromArgb(43, 43, 43) : Color.White;
        var text = dark ? Color.FromArgb(248, 248, 248) : Color.FromArgb(26, 26, 26);
        var accentText = EnsureTextContrast(accent, card, text);
        return new Palette(dark, systemDark, false,
        [window,
            dark ? Color.FromArgb(38, 38, 38) : Color.FromArgb(238, 238, 238), card,
            dark ? Color.FromArgb(56, 56, 56) : Color.FromArgb(246, 246, 246),
            dark ? Color.FromArgb(29, 29, 29) : Color.FromArgb(250, 250, 250),
            dark ? Color.FromArgb(49, 49, 49) : Color.FromArgb(240, 240, 240),
            dark ? Color.FromArgb(79, 79, 79) : Color.FromArgb(205, 205, 205),
            dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(227, 227, 227), text,
            dark ? Color.FromArgb(190, 190, 190) : Color.FromArgb(92, 92, 92),
            dark ? Color.FromArgb(148, 148, 148) : Color.FromArgb(115, 115, 115),
            accent, Blend(accent, dark ? Color.White : Color.Black, 0.1F), accentText,
            Blend(card, accent, dark ? 0.2F : 0.09F),
            dark ? Color.FromArgb(108, 222, 151) : Color.FromArgb(15, 112, 62),
            dark ? Color.FromArgb(248, 118, 128) : Color.FromArgb(196, 43, 28),
            dark ? Color.FromArgb(255, 206, 96) : Color.FromArgb(143, 77, 0)]);
    }

    public static Color ContrastText(Color background) =>
        ContrastRatio(background, Color.Black) >= ContrastRatio(background, Color.White) ? Color.Black : Color.White;

    internal static double ContrastRatio(Color first, Color second)
    {
        var firstLuminance = Luminance(first);
        var secondLuminance = Luminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) / (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
    }

    private static Color EnsureTextContrast(Color color, Color background, Color text)
    {
        for (var step = 0; step <= 20; step++)
        {
            var candidate = Blend(color, text, step / 20F);
            if (ContrastRatio(candidate, background) >= 4.5)
            {
                return candidate;
            }
        }
        return text;
    }

    public static Color Blend(Color from, Color to, float progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * progress),
            (int)Math.Round(from.R + (to.R - from.R) * progress),
            (int)Math.Round(from.G + (to.G - from.G) * progress),
            (int)Math.Round(from.B + (to.B - from.B) * progress));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetColorizationColor(out uint colorizationColor, [MarshalAs(UnmanagedType.Bool)] out bool opaqueBlend);
}
