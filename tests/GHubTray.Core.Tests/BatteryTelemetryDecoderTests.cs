using GHubTray.Core;
using Xunit;

namespace GHubTray.Core.Tests;

public sealed class BatteryTelemetryDecoderTests
{
    [Fact]
    public void VoltageFeatureNeverProducesPercentage()
    {
        var reply = Frame(0x10, 0x41, 0x00);

        var ok = BatteryTelemetryDecoder.TryDecode(
            "G703", 0xC539, 1, 0x1001, reply, out var telemetry, out var error);

        Assert.True(ok, error);
        Assert.NotNull(telemetry);
        Assert.Null(telemetry!.Percent);
        Assert.Equal(0x1041, telemetry.Millivolts);
        Assert.False(telemetry.IsExactPercentage);
    }

    [Fact]
    public void DeviceReportedPercentageIsPreserved()
    {
        var reply = Frame(87, 0, 1);

        var ok = BatteryTelemetryDecoder.TryDecode(
            "Wireless headset", 0xC539, 2, 0x1004, reply, out var telemetry, out var error);

        Assert.True(ok, error);
        Assert.NotNull(telemetry);
        Assert.Equal(87, telemetry!.Percent);
        Assert.True(telemetry.IsExactPercentage);
        Assert.Equal(PowerState.Charging, telemetry.PowerState);
    }

    [Fact]
    public void InvalidVoltageIsRejected()
    {
        var reply = Frame(0, 0, 0);

        var ok = BatteryTelemetryDecoder.TryDecode(
            "broken", 0xC539, 1, 0x1001, reply, out var telemetry, out var error);

        Assert.False(ok);
        Assert.Null(telemetry);
        Assert.Contains("voltage", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvalidDeviceReportedPercentageIsRejected()
    {
        var ok = BatteryTelemetryDecoder.TryDecode(
            "broken", 0xC539, 1, 0x1000, Frame(101, 0, 0), out var telemetry, out var error);

        Assert.False(ok);
        Assert.Null(telemetry);
        Assert.Contains("percentage", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShortFrameIsRejectedWithoutThrowing()
    {
        var ok = BatteryTelemetryDecoder.TryDecode(
            "short", 0xC539, 1, 0x1001, new byte[] { 0x11, 1 }, out var telemetry, out var error);

        Assert.False(ok);
        Assert.Null(telemetry);
        Assert.Contains("shorter", error, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] Frame(byte payload4, byte payload5, byte payload6) =>
        [0x11, 1, 6, 0x0B, payload4, payload5, payload6];
}
