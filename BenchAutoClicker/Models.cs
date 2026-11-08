using System.Drawing;

namespace BenchAutoClicker;

public enum ClickMode
{
    Speed,
    RandomInterval,
    Burst
}

public enum MouseAction
{
    Left,
    Right,
    Middle,
    DoubleLeft
}

public enum TargetMode
{
    Cursor,
    FixedPoint
}

public sealed record ClickConfiguration(
    ClickMode Mode,
    MouseAction MouseAction,
    TargetMode TargetMode,
    Point FixedPoint,
    int JitterRadius,
    double SpeedIntervalMilliseconds,
    double RandomMinimumSeconds,
    double RandomMaximumSeconds,
    int BurstClickCount,
    double BurstClicksPerSecond,
    double BurstPauseSeconds,
    IntPtr ExcludedWindowHandle,
    long MaxClicks = 0,
    double MaxDurationSeconds = 0);

public enum SessionEndReason
{
    ClickLimit,
    DurationLimit
}

internal readonly record struct ClickResult(bool WasSent, bool WasBlocked, Point Position, int PhysicalClickCount)
{
    public static ClickResult Sent(Point position, int physicalClickCount) =>
        new(true, false, position, physicalClickCount);

    public static ClickResult Blocked(Point position) =>
        new(false, true, position, 0);
}

public sealed class UserPreferences
{
    public ClickMode Mode { get; set; } = ClickMode.Speed;
    public MouseAction MouseAction { get; set; } = MouseAction.Left;
    public TargetMode TargetMode { get; set; } = TargetMode.Cursor;
    public int FixedX { get; set; }
    public int FixedY { get; set; }
    public bool HasFixedPoint { get; set; }
    public int JitterRadius { get; set; }
    public decimal ClicksPerSecond { get; set; } = 5;
    public decimal? SpeedIntervalMilliseconds { get; set; }
    public decimal RandomMinimumSeconds { get; set; } = 1;
    public decimal RandomMaximumSeconds { get; set; } = 15;
    public int BurstClickCount { get; set; } = 5;
    public decimal BurstClicksPerSecond { get; set; } = 10;
    public decimal BurstPauseSeconds { get; set; } = 3;
    public int StartDelaySeconds { get; set; } = 3;
    public bool AlwaysOnTop { get; set; }
    public string Language { get; set; } = "system";
    public bool LaunchAtLogin { get; set; }
    public bool TrayMode { get; set; }
    public string ToggleHotkey { get; set; } = "F6";
    public string SpeedHotkey { get; set; } = "Ctrl+Shift+1";
    public string RandomHotkey { get; set; } = "Ctrl+Shift+2";
    public string BurstHotkey { get; set; } = "Ctrl+Shift+3";
    public long MaxClicks { get; set; }
    public double MaxDurationSeconds { get; set; }
    public int WindowWidth { get; set; } = 760;
    public int WindowHeight { get; set; } = 620;
}

public static class ScheduleMath
{
    public const double MinimumClicksPerSecond = 0.2;
    public const double MaximumClicksPerSecond = 50;
    public const double MinimumIntervalMilliseconds = 20;
    public const double MaximumIntervalMilliseconds = 5000;

    public static TimeSpan IntervalFromClicksPerSecond(double clicksPerSecond)
    {
        if (!double.IsFinite(clicksPerSecond) || clicksPerSecond is < MinimumClicksPerSecond or > MaximumClicksPerSecond)
        {
            throw new ArgumentOutOfRangeException(nameof(clicksPerSecond));
        }

        return TimeSpan.FromSeconds(1d / clicksPerSecond);
    }

    public static TimeSpan IntervalFromMilliseconds(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds is < MinimumIntervalMilliseconds or > MaximumIntervalMilliseconds)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        }

        return TimeSpan.FromMilliseconds(milliseconds);
    }

    public static double ClicksPerSecondFromInterval(double milliseconds) =>
        1000d / IntervalFromMilliseconds(milliseconds).TotalMilliseconds;

    public static TimeSpan RandomInterval(double minimumSeconds, double maximumSeconds, Random random)
    {
        ArgumentNullException.ThrowIfNull(random);
        ValidateRandomBounds(minimumSeconds, maximumSeconds);

        var seconds = minimumSeconds + (random.NextDouble() * (maximumSeconds - minimumSeconds));
        return TimeSpan.FromSeconds(seconds);
    }

    public static void ValidateRandomBounds(double minimumSeconds, double maximumSeconds)
    {
        if (!double.IsFinite(minimumSeconds) || !double.IsFinite(maximumSeconds) ||
            minimumSeconds < 0.1 || maximumSeconds < minimumSeconds || maximumSeconds > 3600)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumSeconds));
        }
    }
}

public static class ClickConfigurationValidator
{
    public static void Validate(ClickConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Enum.IsDefined(configuration.Mode) ||
            !Enum.IsDefined(configuration.MouseAction) ||
            !Enum.IsDefined(configuration.TargetMode))
        {
            throw new ArgumentOutOfRangeException(nameof(configuration));
        }
        if (configuration.JitterRadius is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration.JitterRadius));
        }
        if (configuration.MaxClicks is < 0 or > 1_000_000_000 ||
            !double.IsFinite(configuration.MaxDurationSeconds) ||
            configuration.MaxDurationSeconds is < 0 or > 86400)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration));
        }

        switch (configuration.Mode)
        {
            case ClickMode.Speed:
                ScheduleMath.IntervalFromMilliseconds(configuration.SpeedIntervalMilliseconds);
                break;
            case ClickMode.RandomInterval:
                ScheduleMath.ValidateRandomBounds(
                    configuration.RandomMinimumSeconds,
                    configuration.RandomMaximumSeconds);
                break;
            case ClickMode.Burst:
                if (configuration.BurstClickCount is < 1 or > 500 ||
                    !double.IsFinite(configuration.BurstPauseSeconds) ||
                    configuration.BurstPauseSeconds is < 0.1 or > 3600)
                {
                    throw new ArgumentOutOfRangeException(nameof(configuration));
                }
                ScheduleMath.IntervalFromClicksPerSecond(configuration.BurstClicksPerSecond);
                break;
        }
    }
}

public static class NumberText
{
    public static bool TryParse(string text, out decimal value)
    {
        var normalized = (text ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(',', '.');
        return decimal.TryParse(
            normalized,
            System.Globalization.NumberStyles.AllowLeadingSign |
            System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }
}

public static class TargetMath
{
    public static Point CenterOf(Rectangle bounds) => new(
        bounds.Left + (bounds.Width / 2),
        bounds.Top + (bounds.Height / 2));
}
