using System.Diagnostics;

namespace LogiBatteryDock.Core;

internal sealed class ScanDeadline(TimeSpan timeout)
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly bool _unbounded = timeout == Timeout.InfiniteTimeSpan;

    public bool IsExpired =>
        !_unbounded && (timeout <= TimeSpan.Zero || _clock.Elapsed >= timeout);
}
