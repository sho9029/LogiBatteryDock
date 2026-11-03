using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Xunit;

namespace GHubTray.CmdPal.Tests;

public sealed class GHubTrayCmdPalTests
{
    [Fact]
    public async Task PageReadsDeviceWhenTrayProcessIsAbsent()
    {
        var reader = new FakeReader(Snapshot(new DeviceTelemetry(
            "G703 LIGHTSPEED Wireless Gaming Mouse w/ HERO",
            0xC547,
            1,
            0x1001,
            null,
            3960,
            PowerState.Discharging,
            0x01)));
        var store = new TelemetryStatusStore(reader);
        var page = new GHubTrayPage(store);

        await store.RefreshAsync();

        var items = page.GetItems();
        Assert.Contains(items, item => item.Title == "G703");
        var deviceItem = Assert.Single(items, item => item.Title == "G703");
        Assert.IsType<NoOpCommand>(deviceItem.Command);
        Assert.DoesNotContain(items, item => item.Title == "GHubTrayが起動していません");
        Assert.DoesNotContain(items, item => item.Title == "トレイを開く");
        Assert.Equal("残量は未提供 / 接続中", deviceItem.Subtitle);
        Assert.DoesNotContain("取得", deviceItem.Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DockUsesTheSameIndependentStatusStore()
    {
        var reader = new FakeReader(Snapshot(new DeviceTelemetry(
            "G703 LIGHTSPEED Wireless Gaming Mouse w/ HERO",
            0xC539,
            2,
            0x1004,
            73,
            null,
            PowerState.Discharging,
            0x01)));
        var store = new TelemetryStatusStore(reader);
        var provider = new GHubTrayCommandsProvider(store);

        await store.RefreshAsync();

        var dock = Assert.IsType<GHubTrayDockBand>(Assert.Single(provider.GetDockBands()));
        var deviceItem = Assert.Single(dock.Items, item => item.Title == "G703");
        Assert.IsType<NoOpCommand>(deviceItem.Command);
        Assert.Equal("73% / 接続中", deviceItem.Subtitle);
        Assert.DoesNotContain(dock.Items, item => item.Subtitle?.Contains("GHubTray未起動", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task EmptyReceiverResultDoesNotRemainInLoadingState()
    {
        var store = new TelemetryStatusStore(new FakeReader(new LogitechReadSnapshot(1, [])
        {
            ReceiverProductIds = [0xC539]
        }));
        var provider = new GHubTrayCommandsProvider(store);

        await store.RefreshAsync();

        var dock = Assert.IsType<GHubTrayDockBand>(Assert.Single(provider.GetDockBands()));
        Assert.Contains(dock.Items, item => item.Subtitle == "デバイス応答なし");
    }

    private static LogitechReadSnapshot Snapshot(params DeviceTelemetry[] devices) =>
        new(1, devices)
        {
            ReceiverProductIds = [0xC539],
            ReceiverKeys = ["pid:C539"]
        };

    private sealed class FakeReader : ITelemetryReader
    {
        private readonly LogitechReadSnapshot _snapshot;

        public FakeReader(LogitechReadSnapshot snapshot) => _snapshot = snapshot;

        public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default) => _snapshot;
    }
}
