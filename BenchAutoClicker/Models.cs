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
    IntPtr ExcludedWindowHandle);

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
    public int WindowWidth { get; set; } = 640;
    public int WindowHeight { get; set; } = 500;
}

public static class ScheduleMath
{
    public const double MinimumClicksPerSecond = 0.2;
    public const double MaximumClicksPerSecond = 50;
    public const double MinimumIntervalMilliseconds = 20;
    public const double MaximumIntervalMilliseconds = 5000;

    public static TimeSpan IntervalFromClicksPerSecond(double clicksPerSecond)
    {
        if (clicksPerSecond is < MinimumClicksPerSecond or > MaximumClicksPerSecond)
        {
            throw new ArgumentOutOfRangeException(nameof(clicksPerSecond));
        }

        return TimeSpan.FromSeconds(1d / clicksPerSecond);
    }

    public static TimeSpan IntervalFromMilliseconds(double milliseconds)
    {
        if (milliseconds is < MinimumIntervalMilliseconds or > MaximumIntervalMilliseconds)
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
        if (minimumSeconds < 0.1 || maximumSeconds < minimumSeconds || maximumSeconds > 3600)
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
