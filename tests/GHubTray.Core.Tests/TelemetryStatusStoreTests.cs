using GHubTray.Core;
using Xunit;

namespace GHubTray.Core.Tests;

public sealed class TelemetryStatusStoreTests
{
    [Fact]
    public async Task RefreshPublishesVoltageWithoutEstimatingPercentage()
    {
        var reader = new FakeReader(Snapshot(
            new DeviceTelemetry(
                "G703",
                0xC547,
                1,
                0x1001,
                null,
                3960,
                PowerState.Discharging,
                0x01)));
        var store = new TelemetryStatusStore(reader);

        await store.RefreshAsync();

        var device = Assert.Single(store.GetStatus().Devices);
        Assert.Equal("残量は未提供", device.BatteryText);
        Assert.Null(device.Percent);
        Assert.Equal(3960, device.Millivolts);
        Assert.False(device.IsStale);
    }

    [Fact]
    public async Task MissingDeviceIsRetainedAsStaleWithinTheSameFrontend()
    {
        var first = Snapshot(new DeviceTelemetry(
            "Mouse",
            0xC539,
            1,
            0x1004,
            73,
            null,
            PowerState.Discharging,
            0x01)) with
        {
            ReceiverProductIds = [0xC539]
        };
        var second = new LogitechReadSnapshot(1, [])
        {
            ReceiverProductIds = [0xC539]
        };
        var reader = new FakeReader(first, second);
        var store = new TelemetryStatusStore(reader);

        await store.RefreshAsync();
        await store.RefreshAsync();

        var device = Assert.Single(store.GetStatus().Devices);
        Assert.True(device.IsStale);
        Assert.Equal("現在値は未確認", device.BatteryText);
        Assert.Contains("最終取得値 73%", device.TechnicalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConcurrentRefreshesShareOneRead()
    {
        var reader = new FakeReader(Snapshot(), TimeSpan.FromMilliseconds(50));
        var store = new TelemetryStatusStore(reader);

        var first = store.RefreshAsync();
        var second = store.RefreshAsync();

        Assert.Same(first, second);
        await first;
        Assert.Equal(1, reader.ReadCount);
    }

    [Fact]
    public async Task ReaderFailureIsExposedAsFrontendLocalError()
    {
        var store = new TelemetryStatusStore(new ThrowingReader());

        await store.RefreshAsync();

        var status = store.GetStatus();
        Assert.Equal("read_failed", status.Error);
        Assert.Empty(status.Devices);
        Assert.False(store.IsRefreshing);
    }

    [Fact]
    public async Task EnsureInitialRefreshStartsOnlyOnceAfterCompletion()
    {
        var reader = new FakeReader(Snapshot());
        var store = new TelemetryStatusStore(reader);

        Assert.True(store.EnsureInitialRefresh());
        Assert.False(store.EnsureInitialRefresh());
        await store.RefreshAsync();
        await store.RefreshAsync();
        Assert.False(store.EnsureInitialRefresh());
        Assert.Equal(2, reader.ReadCount);
    }

    private static LogitechReadSnapshot Snapshot(params DeviceTelemetry[] devices) =>
        new(1, devices)
        {
            ReceiverProductIds = [0xC539],
            ReceiverKeys = ["pid:C539"]
        };

    private sealed class FakeReader : ITelemetryReader
    {
        private readonly IReadOnlyList<LogitechReadSnapshot> _snapshots;
        private readonly TimeSpan _delay;
        private int _index;

        public FakeReader(params LogitechReadSnapshot[] snapshots)
            : this(snapshots, TimeSpan.Zero)
        {
        }

        public FakeReader(LogitechReadSnapshot snapshot, TimeSpan delay)
            : this([snapshot], delay)
        {
        }

        public FakeReader(
            IReadOnlyList<LogitechReadSnapshot> snapshots,
            TimeSpan delay)
        {
            _snapshots = snapshots;
            _delay = delay;
        }

        public int ReadCount { get; private set; }

        public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default)
        {
            ReadCount++;
            if (_delay > TimeSpan.Zero)
            {
                Thread.Sleep(_delay);
            }

            var index = Math.Min(
                Interlocked.Increment(ref _index) - 1,
                _snapshots.Count - 1);
            return _snapshots[index];
        }
    }

    private sealed class ThrowingReader : ITelemetryReader
    {
        public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("test failure");
    }
}
