using Xunit;

namespace LogiBatteryDock.Core.Tests;

public sealed class BatteryVoltageFallbackTests
{
    [Fact]
    public void GenericCurveProvidesTheCompatibilityBaseline()
    {
        Assert.Equal(70, GenericBatteryVoltageCurve.Estimate(3922));
        Assert.Equal(100, GenericBatteryVoltageCurve.Estimate(4300));
        Assert.Equal(0, GenericBatteryVoltageCurve.Estimate(3400));
    }

    [Fact]
    public void ProviderAddsAnUncalibratedPercentageForVoltageOnlyTelemetry()
    {
        var provider = new BatteryVoltageFallbackProvider();
        var device = new DeviceTelemetry(
            "Unknown Wireless Mouse",
            0xC539,
            1,
            0x1001,
            null,
            3922,
            PowerState.Discharging,
            0x01);

        var result = provider.Apply(device);

        Assert.Equal(70, result.Percent);
        Assert.Equal(TelemetrySource.NativeVoltageEstimate, result.Source);
        Assert.True(result.IsEstimatedPercentage);
        Assert.False(result.IsExactPercentage);
    }

    [Fact]
    public void ProviderDoesNotUseANameMatchedGHubObservation()
    {
        var provider = new BatteryVoltageFallbackProvider();
        var device = new DeviceTelemetry(
            "G703 LIGHTSPEED Wireless Gaming Mouse",
            0xC539,
            1,
            0x1001,
            null,
            3922,
            PowerState.Discharging,
            0x01);

        var result = provider.Apply(device);

        // A G HUB value observed elsewhere must not shift the generic native
        // curve or become a persistent cross-source correction.
        Assert.Equal(70, result.Percent);
        Assert.False(result.IsExactPercentage);
    }

    [Fact]
    public void ProviderLeavesOutOfRangeVoltageUnestimated()
    {
        var provider = new BatteryVoltageFallbackProvider();
        var device = new DeviceTelemetry(
            "Unknown Wireless Mouse",
            0xC539,
            1,
            0x1001,
            null,
            2800,
            PowerState.Discharging,
            0x01);

        var result = provider.Apply(device);

        Assert.Same(device, result);
        Assert.Null(result.Percent);
        Assert.Equal(TelemetrySource.NativeHidpp, result.Source);
    }
}
