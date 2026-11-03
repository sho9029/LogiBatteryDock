using System.Text.Json;
using Xunit;

namespace GHubTray.Core.Tests;

public sealed class GHubAgentTelemetryReaderTests
{
    [Fact]
    public void DeviceListUsesTheRuntimeNameAndBatteryCapability()
    {
        using var document = JsonDocument.Parse("""
            {
              "result": { "code": "SUCCESS", "what": "" },
              "payload": {
                "deviceInfos": [
                  {
                    "id": "dev00000003",
                    "pid": 16518,
                    "deviceModel": "g703_hero",
                    "displayName": "G703 LIGHTSPEED",
                    "extendedDisplayName": "G703 LIGHTSPEED Wireless Gaming Mouse",
                    "deviceSignature": "MOUSE.g703_hero.0.2221078786",
                    "capabilities": { "hasBatteryStatus": true }
                  },
                  {
                    "id": "dev00000002",
                    "pid": 49977,
                    "deviceModel": "pro_keyboard_ext4",
                    "displayName": "PRO",
                    "capabilities": { "hasBatteryStatus": false }
                  }
                ]
              }
            }
            """);

        var devices = GHubAgentResponseParser.ParseDeviceList(document.RootElement);

        var batteryDevice = Assert.Single(devices, device => device.HasBatteryStatus);
        Assert.Equal("dev00000003", batteryDevice.Id);
        Assert.Equal(16518, batteryDevice.ProductId);
        Assert.Equal("G703 LIGHTSPEED Wireless Gaming Mouse", batteryDevice.Name);
        Assert.Equal("MOUSE.g703_hero.0.2221078786", batteryDevice.IdentityKey);
        Assert.False(Assert.Single(devices, device => !device.HasBatteryStatus).HasBatteryStatus);
    }

    [Fact]
    public void BatteryStatePreservesGHubPercentageWithoutVoltageConversion()
    {
        var device = new GHubAgentDeviceInfo(
            "dev00000003",
            16518,
            "G703 LIGHTSPEED",
            "MOUSE.g703_hero.0.2221078786",
            HasBatteryStatus: true);
        using var document = JsonDocument.Parse("""
            {
              "result": { "code": "SUCCESS", "what": "" },
              "payload": {
                "deviceId": "dev00000003",
                "percentage": 71,
                "charging": false,
                "fullyCharged": false
              }
            }
            """);

        var ok = GHubAgentResponseParser.TryParseBatteryState(
            document.RootElement,
            device,
            out var telemetry,
            out var error);

        Assert.True(ok, error);
        Assert.NotNull(telemetry);
        Assert.Equal(71, telemetry!.Percent);
        Assert.Null(telemetry.Millivolts);
        Assert.Equal(TelemetrySource.GHubAgent, telemetry.Source);
        Assert.Equal("dev00000003", telemetry.SourceDeviceId);
        Assert.True(telemetry.IsExactPercentage);
        Assert.Equal(PowerState.Discharging, telemetry.PowerState);
    }

    [Fact]
    public void FullyChargedGHubStateMapsToFull()
    {
        var device = new GHubAgentDeviceInfo("dev", 1, "Device", "signature", true);
        using var document = JsonDocument.Parse("""
            {
              "result": { "code": "SUCCESS" },
              "payload": { "percentage": 100, "charging": true, "fullyCharged": true }
            }
            """);

        var ok = GHubAgentResponseParser.TryParseBatteryState(
            document.RootElement,
            device,
            out var telemetry,
            out var error);

        Assert.True(ok, error);
        Assert.Equal(PowerState.Full, telemetry!.PowerState);
        Assert.Equal(3, telemetry.RawStatus);
    }

    [Fact]
    public void InvalidGHubPercentageIsRejected()
    {
        var device = new GHubAgentDeviceInfo("dev", 1, "Device", "signature", true);
        using var document = JsonDocument.Parse("""
            {
              "result": { "code": "SUCCESS" },
              "payload": { "percentage": 101, "charging": false }
            }
            """);

        var ok = GHubAgentResponseParser.TryParseBatteryState(
            document.RootElement,
            device,
            out var telemetry,
            out var error);

        Assert.False(ok);
        Assert.Null(telemetry);
        Assert.Contains("0..100", error, StringComparison.Ordinal);
    }

    [Fact]
    public void GHubAgentFailureFallsBackToNativeReader()
    {
        var expected = new LogitechReadSnapshot(1, [NativeTelemetry()]);
        var reader = new GHubAwareTelemetryReader(
            new ThrowingGHubReader(),
            new FakeReader(expected));

        var snapshot = reader.ReadSnapshot();

        Assert.NotSame(expected, snapshot);
        var device = Assert.Single(snapshot.Devices);
        Assert.Equal(TelemetrySource.NativeVoltageEstimate, device.Source);
        Assert.Equal(76, device.Percent);
        Assert.True(device.IsEstimatedPercentage);
    }

    [Fact]
    public void GHubAgentReadingIsPreferredOverNativeReader()
    {
        var expected = new LogitechReadSnapshot(1, [GHubTelemetry()]);
        var reader = new GHubAwareTelemetryReader(
            new FakeReader(expected),
            new ThrowingNativeReader());

        var snapshot = reader.ReadSnapshot();

        Assert.Same(expected, snapshot);
        Assert.Equal(71, Assert.Single(snapshot.Devices).Percent);
    }

    private static DeviceTelemetry NativeTelemetry() => new(
        "Native device",
        0xC539,
        1,
        0x1001,
        null,
        3960,
        PowerState.Discharging,
        0x01);

    private static DeviceTelemetry GHubTelemetry() => new(
        "G703 LIGHTSPEED",
        16518,
        0,
        0,
        71,
        null,
        PowerState.Discharging,
        0)
    {
        ReceiverKey = "ghub:signature",
        Source = TelemetrySource.GHubAgent,
        SourceDeviceId = "dev00000003"
    };

    private sealed class FakeReader(LogitechReadSnapshot snapshot) : ITelemetryReader
    {
        public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default) => snapshot;
    }

    private sealed class ThrowingGHubReader : ITelemetryReader
    {
        public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default) =>
            throw new GHubAgentUnavailableException("test");
    }

    private sealed class ThrowingNativeReader : ITelemetryReader
    {
        public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("native should not be called");
    }
}
