using System.Diagnostics;

namespace BenchAutoClicker;

internal sealed class ClickEngine : IAsyncDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource? _cancellation;
    private Task? _worker;

    public event Action<ClickResult>? ClickCompleted;
    public event Action<DateTimeOffset, string>? WaitScheduled;
    public event Action<Exception>? Faulted;
    public event Action? Stopped;

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
            if (_worker is { IsCompleted: false })
            {
                return false;
            }

            _cancellation?.Dispose();
            _cancellation = new CancellationTokenSource();
            _worker = Task.Run(() => RunAsync(configuration, _cancellation.Token));
            return true;
        }
    }

    public async Task StopAsync()
    {
        Task? worker;
        lock (_gate)
        {
            RequestStopUnsafe();
            worker = _worker;
        }

        if (worker is null)
        {
            return;
        }

        try
        {
            await worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    public void RequestStop()
    {
        lock (_gate)
        {
            RequestStopUnsafe();
        }
    }

    private async Task RunAsync(ClickConfiguration configuration, CancellationToken cancellationToken)
    {
        try
        {
            switch (configuration.Mode)
            {
                case ClickMode.RandomInterval:
                    await RunRandomAsync(configuration, cancellationToken).ConfigureAwait(false);
                    break;
                case ClickMode.Burst:
                    await RunBurstAsync(configuration, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    await RunSpeedAsync(configuration, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when the user presses Stop.
        }
        catch (Exception exception)
        {
            SafeInvoke(Faulted, exception);
        }
        finally
        {
            SafeInvoke(Stopped);
        }
    }

    private async Task RunSpeedAsync(ClickConfiguration configuration, CancellationToken cancellationToken)
    {
        var interval = ScheduleMath.IntervalFromMilliseconds(configuration.SpeedIntervalMilliseconds);
        var stopwatch = Stopwatch.StartNew();
        var next = stopwatch.Elapsed;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PerformClick(configuration);
            next += interval;
            var wait = next - stopwatch.Elapsed;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // Never try to "catch up" with a burst after a slow or sleeping system.
                next = stopwatch.Elapsed;
            }
        }
    }

    private async Task RunRandomAsync(ClickConfiguration configuration, CancellationToken cancellationToken)
    {
        while (true)
        {
            var wait = ScheduleMath.RandomInterval(
                configuration.RandomMinimumSeconds,
                configuration.RandomMaximumSeconds,
                Random.Shared);
            SafeInvoke(WaitScheduled, DateTimeOffset.Now + wait, "Следующий случайный клик");
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            PerformClick(configuration);
        }
    }

    private async Task RunBurstAsync(ClickConfiguration configuration, CancellationToken cancellationToken)
    {
        var interval = ScheduleMath.IntervalFromClicksPerSecond(configuration.BurstClicksPerSecond);
        var pause = TimeSpan.FromSeconds(configuration.BurstPauseSeconds);

        while (true)
        {
            for (var click = 0; click < configuration.BurstClickCount; click++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PerformClick(configuration);
                if (click + 1 < configuration.BurstClickCount)
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                }
            }

            SafeInvoke(WaitScheduled, DateTimeOffset.Now + pause, "Пауза между сериями");
            await Task.Delay(pause, cancellationToken).ConfigureAwait(false);
        }
    }

    private void PerformClick(ClickConfiguration configuration)
    {
        var result = NativeInterop.TryClick(configuration);
        SafeInvoke(ClickCompleted, result);
    }

    private void RequestStopUnsafe()
    {
        try
        {
            _cancellation?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A concurrent completed stop already disposed the token source.
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
            // UI teardown must not escape the engine worker or prevent cancellation.
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
            // Subscribers are observers and cannot be allowed to break the click loop.
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
            // Subscribers are observers and cannot be allowed to break the click loop.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _cancellation?.Dispose();
            _cancellation = null;
            _worker = null;
        }
    }
}
