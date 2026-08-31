using BenchAutoClicker;
using System.Drawing;

var checks = new (string Name, Action Run)[]
{
    ("5 CPS produces 200 ms interval", () =>
        Equal(200d, ScheduleMath.IntervalFromClicksPerSecond(5).TotalMilliseconds, 0.001)),
    ("0.2 CPS produces 5 second interval", () =>
        Equal(5d, ScheduleMath.IntervalFromClicksPerSecond(0.2).TotalSeconds, 0.001)),
    ("50 CPS produces 20 ms interval", () =>
        Equal(20d, ScheduleMath.IntervalFromClicksPerSecond(50).TotalMilliseconds, 0.001)),
    ("Editable 333 ms interval stays exact", () =>
        Equal(333d, ScheduleMath.IntervalFromMilliseconds(333).TotalMilliseconds, 0.001)),
    ("5000 ms interval produces 0.2 CPS", () =>
        Equal(0.2d, ScheduleMath.ClicksPerSecondFromInterval(5000), 0.001)),
    ("Interval below 20 ms is rejected", () =>
        Throws<ArgumentOutOfRangeException>(() => ScheduleMath.IntervalFromMilliseconds(19))),
    ("Numeric input accepts decimal comma", () =>
        Parses("3,5", 3.5m)),
    ("Numeric input accepts decimal point", () =>
        Parses("3.5", 3.5m)),
    ("CPS lower boundary is rejected", () =>
        Throws<ArgumentOutOfRangeException>(() => ScheduleMath.IntervalFromClicksPerSecond(0.19))),
    ("CPS upper boundary is rejected", () =>
        Throws<ArgumentOutOfRangeException>(() => ScheduleMath.IntervalFromClicksPerSecond(50.01))),
    ("Random values stay inside range", RandomValuesStayInsideRange),
    ("Equal random bounds stay exact", () =>
        Equal(2.5d, ScheduleMath.RandomInterval(2.5, 2.5, new Random(42)).TotalSeconds, 0.001)),
    ("Reversed random bounds are rejected", () =>
        Throws<ArgumentOutOfRangeException>(() => ScheduleMath.RandomInterval(3, 2, new Random(42)))),
    ("Speed mode accepts editable interval", () =>
        ClickConfigurationValidator.Validate(Configuration(ClickMode.Speed) with { SpeedIntervalMilliseconds = 333 })),
    ("Random mode accepts 1 to 15 seconds", () =>
        ClickConfigurationValidator.Validate(Configuration(ClickMode.RandomInterval))),
    ("Burst mode accepts count, CPS and pause", () =>
        ClickConfigurationValidator.Validate(Configuration(ClickMode.Burst))),
    ("Burst mode rejects zero clicks", () =>
        Throws<ArgumentOutOfRangeException>(() =>
            ClickConfigurationValidator.Validate(Configuration(ClickMode.Burst) with { BurstClickCount = 0 }))),
    ("Screen center is calculated for Full HD", () =>
        EqualPoint(new Point(960, 540), TargetMath.CenterOf(new Rectangle(0, 0, 1920, 1080)))),
    ("Screen center supports negative monitor coordinates", () =>
        EqualPoint(new Point(-540, 1051), TargetMath.CenterOf(new Rectangle(-1080, 91, 1080, 1920))))
};

var failures = 0;
foreach (var check in checks)
{
    try
    {
        check.Run();
        Console.WriteLine($"PASS  {check.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.WriteLine($"FAIL  {check.Name}: {exception.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"{checks.Length - failures}/{checks.Length} checks passed.");
return failures == 0 ? 0 : 1;

static void RandomValuesStayInsideRange()
{
    var random = new Random(20260827);
    for (var sample = 0; sample < 10_000; sample++)
    {
        var value = ScheduleMath.RandomInterval(1, 15, random).TotalSeconds;
        if (value < 1 || value > 15)
        {
            throw new InvalidOperationException($"Random interval {value} is outside 1..15 seconds.");
        }
    }
}

static ClickConfiguration Configuration(ClickMode mode) => new(
    mode,
    MouseAction.Left,
    TargetMode.Cursor,
    Point.Empty,
    0,
    200,
    1,
    15,
    5,
    10,
    3,
    IntPtr.Zero);

static void Equal(double expected, double actual, double tolerance)
{
    if (Math.Abs(expected - actual) > tolerance)
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void Parses(string text, decimal expected)
{
    if (!NumberText.TryParse(text, out var actual) || actual != expected)
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual} from '{text}'.");
    }
}

static void EqualPoint(Point expected, Point actual)
{
    if (actual != expected)
    {
        throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}

static void Throws<TException>(Action action) where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}
