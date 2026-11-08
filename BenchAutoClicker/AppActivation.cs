namespace BenchAutoClicker;

/// <summary>Restores the existing hidden window when the executable is opened a second time.</summary>
internal sealed class AppActivation : IDisposable
{
    private const string EventName = @"Local\BenchAutoClicker.ShowWindow";
    private readonly EventWaitHandle _signal = new(false, EventResetMode.AutoReset, EventName);
    private readonly RegisteredWaitHandle _registration;
    private volatile bool _disposed;

    public AppActivation(Form owner, Action showWindow)
    {
        // Create the HWND on the UI thread before any ThreadPool callback can arrive.
        _ = owner.Handle;
        _registration = ThreadPool.RegisterWaitForSingleObject(_signal, (_, _) =>
        {
            if (_disposed || owner.IsDisposed || !owner.IsHandleCreated)
            {
                return;
            }
            try
            {
                owner.BeginInvoke((Action)(() =>
                {
                    if (!_disposed && !owner.IsDisposed)
                    {
                        showWindow();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
                // The original instance may have closed while activation was being delivered.
            }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public static bool TryActivate()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(EventName);
            return signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _registration.Unregister(null);
        _signal.Dispose();
    }
}
