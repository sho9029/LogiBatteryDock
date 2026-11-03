using LogiBatteryDock.Core;
using Xunit;

namespace LogiBatteryDock.Core.Tests;

public sealed class ScanDeadlineTests
{
    [Fact]
    public void ZeroTimeoutIsAlreadyExpired()
    {
        var deadline = new ScanDeadline(TimeSpan.Zero);

        Assert.True(deadline.IsExpired);
    }

    [Fact]
    public void InfiniteTimeoutNeverExpires()
    {
        var deadline = new ScanDeadline(Timeout.InfiniteTimeSpan);

        Assert.False(deadline.IsExpired);
    }
}
