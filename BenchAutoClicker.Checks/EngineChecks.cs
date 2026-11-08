using System.Diagnostics;
using System.Drawing;
using BenchAutoClicker;

internal static class EngineChecks
{
    private static ClickConfiguration Configuration(ClickMode mode) => new(
        mode, MouseAction.Left, TargetMode.Cursor, Point.Empty, 0, 20, 0.1, 0.1, 2, 50, 0.1, IntPtr.Zero);

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public static async Task SpeedLimit()
    {
        var clicks = 0;
        var reason = await Finish(Configuration(ClickMode.Speed) with { MaxClicks = 3 }, _ =>
        {
            Interlocked.Increment(ref clicks);
            return ClickResult.Sent(Point.Empty, 1);
        });
        Assert(clicks == 3 && reason == SessionEndReason.ClickLimit, $"Got {clicks} clicks / {reason}.");
    }

    public static async Task DoubleClickLimit(long limit, int expected)
    {
        var physicalClicks = 0;
        await Finish(Configuration(ClickMode.Speed) with { MouseAction = MouseAction.DoubleLeft, MaxClicks = limit }, _ =>
        {
            Interlocked.Add(ref physicalClicks, 2);
            return ClickResult.Sent(Point.Empty, 2);
        });
        Assert(physicalClicks == expected, $"Expected {expected}, got {physicalClicks} physical clicks.");
    }

    public static async Task RandomFirstWait()
    {
        var timer = Stopwatch.StartNew();
        var firstAt = TimeSpan.Zero;
        await Finish(Configuration(ClickMode.RandomInterval) with { MaxClicks = 1 }, _ =>
        {
            firstAt = timer.Elapsed;
            return ClickResult.Sent(Point.Empty, 1);
        });
        Assert(firstAt >= TimeSpan.FromMilliseconds(75), $"First click came too early: {firstAt}.");
    }

    public static async Task StopRandomWait()
    {
        var scheduled = Signal();
        var clicks = 0;
        await using var engine = new ClickEngine(_ =>
        {
            Interlocked.Increment(ref clicks);
            return ClickResult.Sent(Point.Empty, 1);
        });
        engine.WaitScheduled += (_, _) => scheduled.TrySetResult();
        engine.Start(Configuration(ClickMode.RandomInterval) with { RandomMinimumSeconds = 3600, RandomMaximumSeconds = 3600 });
        await scheduled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert(clicks == 0 && !engine.IsRunning, "Stop allowed a random click or left a worker running.");
    }

    public static async Task BurstCadence()
    {
        var timer = Stopwatch.StartNew();
        var timestamps = new List<TimeSpan>();
        await Finish(Configuration(ClickMode.Burst) with { MaxClicks = 3 }, _ =>
        {
            timestamps.Add(timer.Elapsed);
            return ClickResult.Sent(Point.Empty, 1);
        });
        Assert(timestamps.Count == 3, $"Got {timestamps.Count} burst clicks.");
        Assert(timestamps[1] - timestamps[0] >= TimeSpan.FromMilliseconds(12), "Intra-series interval skipped.");
        Assert(timestamps[2] - timestamps[1] >= TimeSpan.FromMilliseconds(75), "Inter-series pause skipped.");
    }

    public static async Task DurationLimit()
    {
        var clicks = 0;
        var timer = Stopwatch.StartNew();
        var reason = await Finish(Configuration(ClickMode.RandomInterval) with
        {
            RandomMinimumSeconds = 3600,
            RandomMaximumSeconds = 3600,
            MaxDurationSeconds = 0.08
        }, _ =>
        {
            Interlocked.Increment(ref clicks);
            return ClickResult.Sent(Point.Empty, 1);
        });
        Assert(reason == SessionEndReason.DurationLimit && clicks == 0, "Deadline emitted a click or wrong completion reason.");
        Assert(timer.Elapsed < TimeSpan.FromSeconds(1), "Duration limit did not interrupt long random wait.");
    }

    public static async Task BlockedClicks()
    {
        var attempts = 0;
        await Finish(Configuration(ClickMode.Speed) with { MaxClicks = 1 }, _ =>
        {
            attempts++;
            return attempts <= 2 ? ClickResult.Blocked(Point.Empty) : ClickResult.Sent(Point.Empty, 1);
        });
        Assert(attempts == 3, $"Blocked clicks consumed limit: {attempts} attempts.");
    }

    public static async Task Restart()
    {
        var clicks = 0;
        var scheduled = Signal();
        await using var engine = new ClickEngine(_ =>
        {
            Interlocked.Increment(ref clicks);
            return ClickResult.Sent(Point.Empty, 1);
        });
        engine.WaitScheduled += (_, _) => scheduled.TrySetResult();
        var random = Configuration(ClickMode.RandomInterval) with { RandomMinimumSeconds = 3600, RandomMaximumSeconds = 3600 };
        Assert(engine.Start(random), "First start failed.");
        Assert(!engine.Start(random), "Second concurrent start accepted.");
        await scheduled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await engine.StopAsync();
        var stopped = Signal();
        engine.Stopped += () => stopped.TrySetResult();
        Assert(engine.Start(Configuration(ClickMode.Speed) with { MaxClicks = 1 }), "Restart failed.");
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await engine.StopAsync();
        Assert(clicks == 1 && !engine.IsRunning, $"Restart left {clicks} clicks / running={engine.IsRunning}.");
    }

    public static async Task ObserverFailure()
    {
        var stopped = Signal();
        var clicks = 0;
        await using var engine = new ClickEngine(_ =>
        {
            Interlocked.Increment(ref clicks);
            return ClickResult.Sent(Point.Empty, 1);
        });
        engine.ClickCompleted += _ => throw new InvalidOperationException("Observer failure.");
        engine.Stopped += () => stopped.TrySetResult();
        engine.Start(Configuration(ClickMode.Speed) with { MaxClicks = 2 });
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await engine.StopAsync();
        Assert(clicks == 2, "Observer exception killed the worker.");
    }

    public static async Task NoCatchUp()
    {
        var timer = Stopwatch.StartNew();
        var completedAt = TimeSpan.Zero;
        var secondAt = TimeSpan.Zero;
        var attempts = 0;
        await Finish(Configuration(ClickMode.Speed) with { MaxClicks = 2 }, _ =>
        {
            if (++attempts == 1)
            {
                Thread.Sleep(60);
                completedAt = timer.Elapsed;
            }
            else
            {
                secondAt = timer.Elapsed;
            }
            return ClickResult.Sent(Point.Empty, 1);
        });
        Assert(secondAt - completedAt >= TimeSpan.FromMilliseconds(12), "Overrun produced an immediate catch-up click.");
    }

    public static async Task InputFailure()
    {
        var stopped = Signal();
        Exception? error = null;
        await using var engine = new ClickEngine(_ => throw new InvalidOperationException("Expected input failure."));
        engine.Faulted += exception => error = exception;
        engine.Stopped += () => stopped.TrySetResult();
        engine.Start(Configuration(ClickMode.Speed));
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await engine.StopAsync();
        Assert(error is InvalidOperationException && !engine.IsRunning, "Input failure did not report fault and stop.");
    }

    public static async Task FrozenMetrics()
    {
        var stopped = Signal();
        await using var engine = new ClickEngine(_ => ClickResult.Sent(Point.Empty, 1));
        engine.Stopped += () => stopped.TrySetResult();
        engine.Start(Configuration(ClickMode.Speed) with { MaxClicks = 2 });
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await engine.StopAsync();
        var elapsed = engine.Elapsed;
        await Task.Delay(35);
        Assert(engine.Elapsed == elapsed && elapsed > TimeSpan.Zero, "Elapsed kept running after worker stopped.");
        Assert(engine.TotalClicks == 2, $"Metrics lost clicks: {engine.TotalClicks}.");
    }

    public static async Task BurstDuration()
    {
        var clicks = 0;
        var reason = await Finish(Configuration(ClickMode.Burst) with
        {
            BurstClickCount = 1,
            BurstPauseSeconds = 3600,
            MaxDurationSeconds = 0.08
        }, _ =>
        {
            clicks++;
            return ClickResult.Sent(Point.Empty, 1);
        });
        Assert(reason == SessionEndReason.DurationLimit && clicks == 1, "Burst pause ignored duration limit.");
    }

    public static async Task SpeedDuration()
    {
        var clicks = 0;
        await Finish(Configuration(ClickMode.Speed) with
        {
            SpeedIntervalMilliseconds = 5000,
            MaxDurationSeconds = 0.0805
        }, _ =>
        {
            clicks++;
            return ClickResult.Sent(Point.Empty, 1);
        });
        Assert(clicks == 1, "Shortened deadline wait caused a click before the configured interval.");
    }

    public static async Task DisposedStart()
    {
        var engine = new ClickEngine(_ => ClickResult.Sent(Point.Empty, 1));
        await engine.DisposeAsync();
        try
        {
            engine.Start(Configuration(ClickMode.Speed));
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        throw new InvalidOperationException("Disposed engine accepted new work.");
    }

    private static async Task<SessionEndReason?> Finish(ClickConfiguration configuration, Func<ClickConfiguration, ClickResult> click)
    {
        var stopped = Signal();
        SessionEndReason? reason = null;
        Exception? fault = null;
        await using var engine = new ClickEngine(click);
        engine.SessionCompleted += completed => reason = completed;
        engine.Faulted += error => fault = error;
        engine.Stopped += () => stopped.TrySetResult();
        engine.Start(configuration);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await engine.StopAsync();
        if (fault is not null)
        {
            throw new InvalidOperationException("Engine faulted.", fault);
        }
        return reason;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
