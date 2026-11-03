using LogiBatteryDock.Core;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace LogiBatteryDock.CmdPal;

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
        var subtitle = $"{device.BatteryText} / {device.PowerText}";
        return device.IsStale
            ? $"{subtitle} / 最終取得値"
            : subtitle;
    }

    public static string DeviceDockTitle(TelemetryDeviceStatus device)
        => device.BatteryText;

    public static string DeviceDockSubtitle(TelemetryDeviceStatus device) =>
        DeviceTitle(device.Name);

    public static IconInfo DeviceBatteryIcon(TelemetryDeviceStatus device)
    {
        if (device.IsStale || device.Percent is not int percent)
        {
            return BatteryUnknownIcon;
        }

        var level = (int)Math.Round(Math.Clamp(percent, 0, 100) / 10d);
        return device.PowerState == PowerState.Charging
            ? BatteryChargingIcons[level]
            : BatteryIcons[level];
    }

    public static IconInfo UnavailableBatteryIcon => BatteryUnknownIcon;

    public static string DeviceCommandId(string prefix, TelemetryDeviceStatus device) =>
        $"{prefix}.{device.Id}";

    private static readonly IconInfo[] BatteryIcons = BuildBatteryGlyphs(0xEBA0);
    private static readonly IconInfo[] BatteryChargingIcons = BuildBatteryGlyphs(0xEBAB);
    private static readonly IconInfo BatteryUnknownIcon = new("\uEC02");

    private static IconInfo[] BuildBatteryGlyphs(int baseCodepoint)
    {
        var icons = new IconInfo[11];
        for (var level = 0; level <= 10; level++)
        {
            icons[level] = new IconInfo(char.ConvertFromUtf32(baseCodepoint + level));
        }

        return icons;
    }
}
