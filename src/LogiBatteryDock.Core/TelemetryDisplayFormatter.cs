namespace LogiBatteryDock.Core;

public sealed record TelemetryDisplay(
    string BatteryText,
    string PowerText,
    string TechnicalText);

public static class TelemetryDisplayFormatter
{
    public static TelemetryDisplay Format(DeviceTelemetry device, bool isStale = false)
    {
        var batteryText = device.HasPercentage ? $"{device.Percent}%" : "残量は未提供";
        var powerText = ToPowerText(device.PowerState);
        var technicalText = device.Source switch
        {
            TelemetrySource.GHubAgent =>
                $"G HUB 連携  •  取得 {device.ObservedAt.ToLocalTime():HH:mm:ss}",
            TelemetrySource.NativeVoltageEstimate when device.Millivolts is int millivolts =>
                $"{millivolts} mV  •  標準カーブ推定  •  HID++ 0x{device.BatteryFeature:X4}  •  flags 0x{device.RawStatus:X2}  •  取得 {device.ObservedAt.ToLocalTime():HH:mm:ss}",
            TelemetrySource.NativeVoltageEstimate =>
                $"電圧推定  •  HID++ 0x{device.BatteryFeature:X4}  •  status 0x{device.RawStatus:X2}  •  取得 {device.ObservedAt.ToLocalTime():HH:mm:ss}",
            _ when device.Millivolts is int millivolts =>
                $"{millivolts} mV  •  HID++ 0x{device.BatteryFeature:X4}  •  flags 0x{device.RawStatus:X2}  •  取得 {device.ObservedAt.ToLocalTime():HH:mm:ss}",
            _ =>
                $"HID++ 0x{device.BatteryFeature:X4}  •  status 0x{device.RawStatus:X2}  •  取得 {device.ObservedAt.ToLocalTime():HH:mm:ss}"
        };

        if (isStale)
        {
            var lastKnownValue = device.HasPercentage
                ? $"最終取得値 {batteryText}"
                : "最終取得時も残量は未提供";
            batteryText = "現在値は未確認";
            powerText = "応答なし（スリープまたは圏外）";
            technicalText = $"{lastKnownValue} • {technicalText} • 最終取得値";
        }

        return new(batteryText, powerText, technicalText);
    }

    public static string ToPowerText(PowerState state) => state switch
    {
        PowerState.Charging => "充電中",
        PowerState.Full => "満充電",
        PowerState.ExternalPower => "外部電源あり",
        PowerState.Discharging => "接続中",
        _ => "状態不明"
    };
}
