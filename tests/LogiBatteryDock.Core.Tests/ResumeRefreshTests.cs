using LogiBatteryDock.Core;
using Xunit;

namespace LogiBatteryDock.Core.Tests;

public sealed class ResumeRefreshTests
{
    [Fact]
    public void NativeRegistrationCanBeCreatedAndDisposed()
    {
        using var notification = WindowsResumeNotification.TryCreate(() => { });
        Assert.NotNull(notification);
    }

    [Fact]
    public void DuplicateAndInteractiveResumeEventsDoNotAddReads()
    {
        var calls = 0;
        using var notification = new WindowsResumeNotification(() => calls++);
        notification.ProcessPowerEvent(4);
        notification.ProcessPowerEvent(18);
        notification.ProcessPowerEvent(18);
        notification.ProcessPowerEvent(7);
        Assert.Equal(1, calls);
        notification.ProcessPowerEvent(4);
        notification.ProcessPowerEvent(18);
        Assert.Equal(2, calls);
        notification.Dispose();
        notification.ProcessPowerEvent(4);
        notification.ProcessPowerEvent(18);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ResumeDuringOldReadQueuesOnlyOneFreshRead()
    {
        using var reader = new BlockingReader();
        var store = new TelemetryStatusStore(reader);
        var old = store.RefreshAsync();
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var resume = store.RefreshAfterResumeAsync();
        Assert.Equal(1, reader.ReadCount);
        reader.Release.Set();
        await Task.WhenAll(old, resume).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, reader.ReadCount);
        Assert.Equal(2, store.GetStatus().ReceiverCount);
    }

    [Fact]
    public async Task ShutdownCancelsPendingResumeRead()
    {
        using var reader = new BlockingReader();
        var store = new TelemetryStatusStore(reader);
        var old = store.RefreshAsync();
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var lifetime = new CancellationTokenSource();
        var resume = store.RefreshAfterResumeAsync(lifetime.Token);
        lifetime.Cancel();
        reader.Release.Set();
        await Task.WhenAll(old, resume).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, reader.ReadCount);
    }

    private sealed class BlockingReader : ITelemetryReader, IDisposable
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim Release = new();
        internal int ReadCount;
        public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref ReadCount);
            if (count == 1)
            {
                Started.TrySetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(5), cancellationToken)) throw new TimeoutException();
            }
            return new(count, []);
        }
        public void Dispose() { Release.Set(); Release.Dispose(); }
    }
}
