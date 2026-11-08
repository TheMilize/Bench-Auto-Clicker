namespace BenchAutoClicker;

internal sealed class ClickEngine : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly Func<ClickConfiguration, ClickResult> _click;
    private readonly TimeProvider _timeProvider;
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private Session? _session;
    private bool _disposed;

    public ClickEngine(Func<ClickConfiguration, ClickResult>? click = null, TimeProvider? timeProvider = null)
    {
        _click = click ?? NativeInterop.TryClick;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public event Action<ClickResult>? ClickCompleted;
    public event Action<DateTimeOffset, string>? WaitScheduled;
    public event Action<Exception>? Faulted;
    public event Action? Stopped;
    public event Action<SessionEndReason>? SessionCompleted;

    public long TotalClicks
    {
        get
        {
            var session = Volatile.Read(ref _session);
            return session is null ? 0 : Interlocked.Read(ref session.PhysicalClicks);
        }
    }

    public TimeSpan Elapsed
    {
        get
        {
            var session = Volatile.Read(ref _session);
            if (session is null)
            {
                return TimeSpan.Zero;
            }
            var frozenTicks = Interlocked.Read(ref session.ElapsedTicks);
            return frozenTicks >= 0 ? TimeSpan.FromTicks(frozenTicks) : _timeProvider.GetElapsedTime(session.StartedAt);
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _worker is { IsCompleted: false };
            }
        }
    }

    public bool Start(ClickConfiguration configuration)
    {
        ClickConfigurationValidator.Validate(configuration);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_worker is { IsCompleted: false })
            {
                return false;
            }

            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            // Capture this run's token, never a mutable CTS field shared with a later run.
            var token = _cancellation.Token;
            var session = new Session(_timeProvider.GetTimestamp());
            Volatile.Write(ref _session, session);
            _worker = Task.Run(() => RunAsync(configuration, session, token));
            return true;
        }
    }

    public async Task StopAsync()
    {
        Task? worker;
        lock (_gate)
        {
            _cancellation?.Cancel();
            worker = _worker;
        }

        if (worker is not null)
        {
            await worker.ConfigureAwait(false);
        }
    }

    public void RequestStop()
    {
        lock (_gate)
        {
            _cancellation?.Cancel();
        }
    }

    private async Task RunAsync(ClickConfiguration configuration, Session session, CancellationToken cancellationToken)
    {
        try
        {
            var reason = configuration.Mode switch
            {
                ClickMode.RandomInterval => await RunRandomAsync(configuration, session, cancellationToken).ConfigureAwait(false),
                ClickMode.Burst => await RunBurstAsync(configuration, session, cancellationToken).ConfigureAwait(false),
                _ => await RunSpeedAsync(configuration, session, cancellationToken).ConfigureAwait(false)
            };
            SafeInvoke(SessionCompleted, reason);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // User/system stop interrupts every delay, including a long random or burst pause.
        }
        catch (Exception exception)
        {
            SafeInvoke(Faulted, exception);
        }
        finally
        {
            Interlocked.Exchange(ref session.ElapsedTicks, _timeProvider.GetElapsedTime(session.StartedAt).Ticks);
            SafeInvoke(Stopped);
        }
    }

    private async Task<SessionEndReason> RunSpeedAsync(
        ClickConfiguration configuration, Session session, CancellationToken token)
    {
        var interval = ScheduleMath.IntervalFromMilliseconds(configuration.SpeedIntervalMilliseconds);
        while (true)
        {
            var startedAt = _timeProvider.GetTimestamp();
            if (PerformClick(configuration, session, token) is { } reason)
            {
                return reason;
            }

            var elapsed = _timeProvider.GetElapsedTime(startedAt);
            // Maintain cadence under normal load. An overrun starts a fresh interval;
            // it must never create back-to-back catch-up clicks after a stall.
            var wait = elapsed < interval ? interval - elapsed : interval;
            await DelayAsync(wait, configuration, session, token).ConfigureAwait(false);
        }
    }

    private async Task<SessionEndReason> RunRandomAsync(
        ClickConfiguration configuration, Session session, CancellationToken token)
    {
        while (true)
        {
            var wait = ScheduleMath.RandomInterval(
                configuration.RandomMinimumSeconds, configuration.RandomMaximumSeconds, Random.Shared);
            SafeInvoke(WaitScheduled, _timeProvider.GetUtcNow() + wait, "Следующий случайный клик");
            await DelayAsync(wait, configuration, session, token).ConfigureAwait(false);
            if (PerformClick(configuration, session, token) is { } reason)
            {
                return reason;
            }
        }
    }

    private async Task<SessionEndReason> RunBurstAsync(
        ClickConfiguration configuration, Session session, CancellationToken token)
    {
        var interval = ScheduleMath.IntervalFromClicksPerSecond(configuration.BurstClicksPerSecond);
        var pause = TimeSpan.FromSeconds(configuration.BurstPauseSeconds);
        while (true)
        {
            for (var click = 0; click < configuration.BurstClickCount; click++)
            {
                if (PerformClick(configuration, session, token) is { } reason)
                {
                    return reason;
                }
                if (click + 1 < configuration.BurstClickCount)
                {
                    await DelayAsync(interval, configuration, session, token).ConfigureAwait(false);
                }
            }

            SafeInvoke(WaitScheduled, _timeProvider.GetUtcNow() + pause, "Пауза между сериями");
            await DelayAsync(pause, configuration, session, token).ConfigureAwait(false);
        }
    }

    private SessionEndReason? PerformClick(ClickConfiguration configuration, Session session, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (GetLimitReason(configuration, session) is { } before)
        {
            return before;
        }

        var result = _click(configuration);
        if (result.WasSent)
        {
            Interlocked.Add(ref session.PhysicalClicks, result.PhysicalClickCount);
        }
        SafeInvoke(ClickCompleted, result);
        token.ThrowIfCancellationRequested();
        return GetLimitReason(configuration, session);
    }

    private SessionEndReason? GetLimitReason(ClickConfiguration configuration, Session session)
    {
        var clicksPerAction = configuration.MouseAction == MouseAction.DoubleLeft ? 2 : 1;
        if (configuration.MaxClicks > 0 && session.PhysicalClicks + clicksPerAction > configuration.MaxClicks)
        {
            return SessionEndReason.ClickLimit;
        }
        if (configuration.MaxDurationSeconds > 0 &&
            _timeProvider.GetElapsedTime(session.StartedAt).TotalSeconds >= configuration.MaxDurationSeconds)
        {
            return SessionEndReason.DurationLimit;
        }
        return null;
    }

    private async Task DelayAsync(TimeSpan delay, ClickConfiguration configuration, Session session, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (configuration.MaxDurationSeconds > 0)
        {
            var remaining = TimeSpan.FromSeconds(configuration.MaxDurationSeconds) - _timeProvider.GetElapsedTime(session.StartedAt);
            delay = remaining <= TimeSpan.Zero ? TimeSpan.Zero : TimeSpan.FromTicks(Math.Min(delay.Ticks, remaining.Ticks));
        }
        var startedAt = _timeProvider.GetTimestamp();
        while (delay > TimeSpan.Zero)
        {
            // Timer implementations can truncate fractional milliseconds. Round up and
            // recheck the monotonic deadline so a truncated limit wait cannot emit an
            // early click at the end of a session.
            var remaining = delay - _timeProvider.GetElapsedTime(startedAt);
            if (remaining <= TimeSpan.Zero)
            {
                return;
            }
            var timerDelay = TimeSpan.FromMilliseconds(Math.Max(1, Math.Ceiling(remaining.TotalMilliseconds)));
            await Task.Delay(timerDelay, _timeProvider, token).ConfigureAwait(false);
        }
    }

    private static void SafeInvoke(Action? callback)
    {
        try
        {
            callback?.Invoke();
        }
        catch
        {
            // Observers must not break worker shutdown.
        }
    }

    private static void SafeInvoke<T>(Action<T>? callback, T argument)
    {
        try
        {
            callback?.Invoke(argument);
        }
        catch
        {
            // Observers must not break worker shutdown.
        }
    }

    private static void SafeInvoke<T1, T2>(Action<T1, T2>? callback, T1 first, T2 second)
    {
        try
        {
            callback?.Invoke(first, second);
        }
        catch
        {
            // Observers must not break worker shutdown.
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? worker;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _cancellation?.Cancel();
            worker = _worker;
        }
        if (worker is not null)
        {
            await worker.ConfigureAwait(false);
        }
        lock (_gate)
        {
            _cancellation?.Dispose();
            _cancellation = null;
            _worker = null;
        }
    }

    private sealed class Session(long startedAt)
    {
        public long StartedAt { get; } = startedAt;
        public long PhysicalClicks;
        public long ElapsedTicks = -1;
    }
}
