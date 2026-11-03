using GHubTray.Core;

namespace GHubTray.CmdPal;

internal static class CmdPalDisplayFormatter
{
    private const int MaxDeviceTitleLength = 16;

    public static string DeviceTitle(string name)
    {
        var words = name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return "デバイス";
        }

        var title = words[0];
        if (title.Length < 3 && words.Length > 1)
        {
            title = $"{title} {words[1]}";
        }

        return title.Length <= MaxDeviceTitleLength
            ? title
            : $"{title[..(MaxDeviceTitleLength - 1)]}…";
    }

    public static string DeviceSubtitle(TelemetryDeviceStatus device)
    {
        var batteryText = device.IsEstimatedPercentage
            ? $"{device.BatteryText}（推定）"
            : device.BatteryText;
        var subtitle = $"{batteryText} / {device.PowerText}";
        return device.IsStale
            ? $"{subtitle} / 最終取得値"
            : subtitle;
    }

    public static string DeviceCommandId(string prefix, TelemetryDeviceStatus device) =>
        $"{prefix}.{device.Id}";
}
