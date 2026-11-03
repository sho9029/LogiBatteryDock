using LogiBatteryDock.Core;
using Xunit;

namespace LogiBatteryDock.Core.Tests;

public sealed class TelemetryDisplayFormatterTests
{
    [Fact]
    public void VoltageTelemetryNeverGetsAFormattedPercentage()
    {
        var display = TelemetryDisplayFormatter.Format(
            Telemetry(0x1001, percent: null, millivolts: 3969));

        Assert.Equal("残量は未提供", display.BatteryText);
        Assert.DoesNotContain("%", display.BatteryText);
        Assert.Contains("3969 mV", display.TechnicalText);
        Assert.Equal("接続中", display.PowerText);
    }

    [Fact]
    public void StaleReportedPercentageMovesToTechnicalDetails()
    {
        var display = TelemetryDisplayFormatter.Format(
            Telemetry(0x1004, percent: 73, millivolts: null),
            isStale: true);

        Assert.Equal("現在値は未確認", display.BatteryText);
        Assert.Equal("応答なし（スリープまたは圏外）", display.PowerText);
        Assert.Contains("最終取得値 73%", display.TechnicalText);
        Assert.Contains("HID++ 0x1004", display.TechnicalText);
    }

    [Fact]
    public void GHubAgentPercentageIsDisplayedAsReported()
    {
        var display = TelemetryDisplayFormatter.Format(
            Telemetry(0, percent: 71, millivolts: null) with
            {
                Source = TelemetrySource.GHubAgent
            });

        Assert.Equal("71%", display.BatteryText);
        Assert.Equal("接続中", display.PowerText);
        Assert.Contains("G HUB 連携", display.TechnicalText);
    }

    [Fact]
    public void VoltageEstimateShowsPercentageAndItsProvenance()
    {
        var display = TelemetryDisplayFormatter.Format(
            Telemetry(0x1001, percent: 71, millivolts: 3927) with
            {
                Source = TelemetrySource.NativeVoltageEstimate
            });

        Assert.Equal("71%", display.BatteryText);
        Assert.Contains("標準カーブ推定", display.TechnicalText);
        Assert.Contains("3927 mV", display.TechnicalText);
    }

    private static DeviceTelemetry Telemetry(ushort feature, int? percent, int? millivolts) =>
        new(
            "Test device",
            0xC539,
            1,
            feature,
            percent,
            millivolts,
            PowerState.Discharging,
            0)
        {
            ObservedAt = DateTimeOffset.Parse("2026-09-02T00:00:00Z")
        };
}
