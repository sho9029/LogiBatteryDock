using LogiBatteryDock.Core;
using Xunit;

namespace LogiBatteryDock.Core.Tests;

public sealed class DeviceStateMergerTests
{
    [Fact]
    public void MissingDeviceOnPresentReceiverIsKeptAsStale()
    {
        var first = new LogitechReadSnapshot(
            1,
            [
                Telemetry("Mouse", 0xC539, 1, 0x1001, 4000),
                Telemetry("Keyboard", 0xC539, 2, 0x1004, 0, 73)
            ])
        {
            ReceiverProductIds = [0xC539]
        };
        var second = new LogitechReadSnapshot(1, [first.Devices[0]])
        {
            ReceiverProductIds = [0xC539]
        };

        var merger = new DeviceStateMerger();
        _ = merger.Merge(first);
        var merged = merger.Merge(second);

        Assert.Equal(2, merged.Count);
        Assert.False(merged[0].IsStale);
        Assert.Equal("Mouse", merged[0].Telemetry.Name);
        Assert.True(merged[1].IsStale);
        Assert.Equal("Keyboard", merged[1].Telemetry.Name);
        Assert.Equal(73, merged[1].Telemetry.Percent);
        Assert.Equal(first.Devices[1].ObservedAt, merged[1].Telemetry.ObservedAt);
    }

    [Fact]
    public void RemovedReceiverDropsPreviouslyKnownDevices()
    {
        var merger = new DeviceStateMerger();
        var known = new LogitechReadSnapshot(1, [Telemetry("Mouse", 0xC539, 1, 0x1001, 4000)])
        {
            ReceiverProductIds = [0xC539]
        };
        _ = merger.Merge(known);

        var merged = merger.Merge(new LogitechReadSnapshot(0, []));

        Assert.Empty(merged);
    }

    [Fact]
    public void LegacySnapshotWithoutIdsRetainsKnownReceiverConservatively()
    {
        var merger = new DeviceStateMerger();
        _ = merger.Merge(new LogitechReadSnapshot(1, [Telemetry("Mouse", 0xC539, 1, 0x1001, 4000)]));

        var merged = merger.Merge(new LogitechReadSnapshot(1, []));

        var state = Assert.Single(merged);
        Assert.True(state.IsStale);
        Assert.Equal("Mouse", state.Telemetry.Name);
    }

    [Fact]
    public void SameProductIdDifferentReceiverKeysRemainSeparate()
    {
        var first = new LogitechReadSnapshot(
            2,
            [
                Telemetry("Mouse A", 0xC539, 1, 0x1001, 4000, receiverKey: "path:A"),
                Telemetry("Mouse B", 0xC539, 1, 0x1001, 4050, receiverKey: "path:B")
            ])
        {
            ReceiverProductIds = [0xC539],
            ReceiverKeys = ["path:A", "path:B"]
        };
        var second = new LogitechReadSnapshot(
            2,
            [first.Devices[0]])
        {
            ReceiverProductIds = [0xC539],
            ReceiverKeys = ["path:A", "path:B"]
        };

        var merger = new DeviceStateMerger();
        _ = merger.Merge(first);
        var merged = merger.Merge(second);

        Assert.Equal(2, merged.Count);
        Assert.False(merged[0].IsStale);
        Assert.Equal("path:A", merged[0].Telemetry.ReceiverKey);
        Assert.True(merged[1].IsStale);
        Assert.Equal("path:B", merged[1].Telemetry.ReceiverKey);
    }

    private static DeviceTelemetry Telemetry(
        string name,
        int receiverProductId,
        byte deviceIndex,
        ushort batteryFeature,
        int millivolts,
        int? percent = null,
        string receiverKey = "") =>
        new(
            name,
            receiverProductId,
            deviceIndex,
            batteryFeature,
            percent,
            millivolts,
            PowerState.Discharging,
            0)
        {
            ReceiverKey = receiverKey,
            ObservedAt = DateTimeOffset.Parse("2026-09-02T00:00:00Z")
        };
}
