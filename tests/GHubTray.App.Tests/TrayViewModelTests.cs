using GHubTray.App;
using GHubTray.Core;
using Xunit;

namespace GHubTray.App.Tests;

public sealed class TrayViewModelTests
{
    [Fact]
    public async Task ResumeWaitsForOldCardUpdateThenDisplaysFreshRead()
    {
        using var reader = new BlockingResumeReader();
        var viewModel = new TrayViewModel(new TelemetryStatusStore(reader));
        var old = viewModel.RefreshAsync();
        await reader.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var resume = viewModel.RefreshAfterResumeAsync();
        reader.Release.Set();
        await Task.WhenAll(old, resume).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, reader.ReadCount);
        Assert.Equal("46%", Assert.Single(viewModel.Devices).BatteryValue);
    }

    [Fact]
    public async Task RefreshUsesItsOwnReaderAndUpdatesCards()
    {
        var reader = new FakeReader(Snapshot(new DeviceTelemetry(
            "G703",
            0xC547,
            1,
            0x1001,
            null,
            3960,
            PowerState.Discharging,
            0x01)));
        var viewModel = new TrayViewModel(new TelemetryStatusStore(reader));

        await viewModel.RefreshAsync();

        var card = Assert.Single(viewModel.Devices);
        Assert.Equal("G703", card.Name);
        Assert.Equal("残量は未提供", card.BatteryValue);
        Assert.Equal("接続中", card.PowerText);
        Assert.Contains("3960 mV", card.TechnicalText, StringComparison.Ordinal);
        Assert.Contains("GHubTray", viewModel.ToolTip, StringComparison.Ordinal);
        Assert.Contains("G703: 残量は未提供 / 接続中", viewModel.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingDeviceCardIsMarkedAsStale()
    {
        var first = Snapshot(new DeviceTelemetry(
            "Keyboard",
            0xC539,
            2,
            0x1004,
            73,
            null,
            PowerState.Discharging,
            0x01));
        var second = new LogitechReadSnapshot(1, [])
        {
            ReceiverProductIds = [0xC539]
        };
        var viewModel = new TrayViewModel(new TelemetryStatusStore(new FakeReader(first, second)));

        await viewModel.RefreshAsync();
        await viewModel.RefreshAsync();

        var card = Assert.Single(viewModel.Devices);
        Assert.True(card.IsStale);
        Assert.Equal("現在値は未確認", card.BatteryValue);
        Assert.Contains("最終取得値 73%", card.TechnicalText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VoltageEstimateAppearsInCardAndTooltip()
    {
        var reader = new FakeReader(Snapshot(new DeviceTelemetry(
            "G703",
            0xC547,
            1,
            0x1001,
            71,
            3927,
            PowerState.Discharging,
            0x01)
        {
            Source = TelemetrySource.NativeVoltageEstimate
        }));
        var viewModel = new TrayViewModel(new TelemetryStatusStore(reader));

        await viewModel.RefreshAsync();

        var card = Assert.Single(viewModel.Devices);
        Assert.Equal("71%", card.BatteryValue);
        Assert.Contains("標準カーブ推定", card.TechnicalText, StringComparison.Ordinal);
        Assert.Contains("G703: 71% / 接続中", viewModel.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TooltipListsEveryDevice()
    {
        var reader = new FakeReader(Snapshot(
            new DeviceTelemetry(
                "G703",
                0xC547,
                1,
                0x1004,
                71,
                null,
                PowerState.Discharging,
                0x01),
            new DeviceTelemetry(
                "G915",
                0xC547,
                2,
                0x1004,
                88,
                null,
                PowerState.Charging,
                0x02)));
        var viewModel = new TrayViewModel(new TelemetryStatusStore(reader));

        await viewModel.RefreshAsync();

        Assert.Contains("G703: 71% / 接続中", viewModel.ToolTip, StringComparison.Ordinal);
        Assert.Contains("G915: 88% / 充電中", viewModel.ToolTip, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReaderFailureIsShownAsFailureCard()
    {
        var viewModel = new TrayViewModel(new TelemetryStatusStore(new ThrowingReader()));

        await viewModel.RefreshAsync();

        var card = Assert.Single(viewModel.Devices);
        Assert.Equal("読み取りに失敗", card.Name);
        Assert.Equal("GHubTray — 読み取り失敗", viewModel.ToolTip);
        Assert.Equal("read_failed", viewModel.GetStatus().Error);
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
        private int _index;

        public FakeReader(params LogitechReadSnapshot[] snapshots) => _snapshots = snapshots;

        public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default)
        {
            var index = Math.Min(
                Interlocked.Increment(ref _index) - 1,
                _snapshots.Count - 1);
            return _snapshots[index];
        }
    }

    private sealed class BlockingResumeReader : ITelemetryReader, IDisposable
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly ManualResetEventSlim Release = new();
        internal int ReadCount;
        public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref ReadCount) == 1)
            {
                Started.TrySetResult();
                if (!Release.Wait(TimeSpan.FromSeconds(5), cancellationToken)) throw new TimeoutException();
                return new(0, []);
            }
            return Snapshot(new DeviceTelemetry("G703", 0xC539, 1, 0x1004,
                46, null, PowerState.Discharging, 0));
        }
        public void Dispose() { Release.Set(); Release.Dispose(); }
    }

    private sealed class ThrowingReader : ITelemetryReader
    {
        public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("test failure");
    }
}
