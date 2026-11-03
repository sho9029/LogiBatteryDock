using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Xunit;

namespace GHubTray.CmdPal.Tests;

public sealed class GHubTrayCmdPalTests
{
    [Fact]
    public async Task PageReadsDeviceWhenTrayProcessIsAbsent()
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
        var store = new TelemetryStatusStore(reader);
        var page = new GHubTrayPage(store);

        await store.RefreshAsync();

        var items = page.GetItems();
        Assert.Contains(items, item => item.Title == "G703");
        Assert.DoesNotContain(items, item => item.Title == "GHubTrayが起動していません");
        Assert.DoesNotContain(items, item => item.Title == "トレイを開く");
        Assert.Contains(items, item => item.Subtitle?.Contains("残量は未提供", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task DockUsesTheSameIndependentStatusStore()
    {
        var reader = new FakeReader(Snapshot(new DeviceTelemetry(
            "Keyboard",
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
        Assert.Contains(dock.Items, item => item.Title == "Keyboard");
        Assert.DoesNotContain(dock.Items, item => item.Subtitle?.Contains("GHubTray未起動", StringComparison.Ordinal) == true);
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
