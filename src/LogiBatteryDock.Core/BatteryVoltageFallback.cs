namespace LogiBatteryDock.Core;

internal interface IBatteryVoltageFallbackProvider
{
    DeviceTelemetry Apply(DeviceTelemetry device);
}

/// <summary>
/// Applies the same generic voltage curve used by native HID++ compatibility
/// implementations. G HUB's device-specific percentage calculation remains
/// the primary source whenever its local agent is available.
/// </summary>
internal sealed class BatteryVoltageFallbackProvider : IBatteryVoltageFallbackProvider
{
    public DeviceTelemetry Apply(DeviceTelemetry device)
    {
        if (device.Source != TelemetrySource.NativeHidpp
            || device.BatteryFeature != 0x1001
            || device.Millivolts is not int millivolts
            || !GenericBatteryVoltageCurve.IsValidVoltage(millivolts))
        {
            return device;
        }

        return device with
        {
            Percent = Math.Clamp(
                (int)Math.Round(
                    GenericBatteryVoltageCurve.Estimate(millivolts),
                    MidpointRounding.AwayFromZero),
                0,
                100),
            Source = TelemetrySource.NativeVoltageEstimate
        };
    }
}

internal static class GenericBatteryVoltageCurve
{
    public const int MinimumVoltageMillivolts = 3000;
    public const int MaximumVoltageMillivolts = 5000;

    // Generic Li-ion curve used only for native HID++ fallback. It is not a
    // reproduction of G HUB's device-specific lookup tables.
    private static readonly (int Millivolts, double Percent)[] Points =
    [
        (3500, 0),
        (3579, 2),
        (3646, 5),
        (3671, 10),
        (3717, 20),
        (3751, 30),
        (3778, 40),
        (3811, 50),
        (3859, 60),
        (3922, 70),
        (3989, 80),
        (4067, 90),
        (4186, 100)
    ];

    public static bool IsValidVoltage(int millivolts) =>
        millivolts is >= MinimumVoltageMillivolts and <= MaximumVoltageMillivolts;

    public static double Estimate(int millivolts)
    {
        if (millivolts <= Points[0].Millivolts)
        {
            return Points[0].Percent;
        }

        if (millivolts >= Points[^1].Millivolts)
        {
            return Points[^1].Percent;
        }

        for (var index = 1; index < Points.Length; index++)
        {
            var left = Points[index - 1];
            var right = Points[index];
            if (millivolts > right.Millivolts)
            {
                continue;
            }

            var fraction = (millivolts - left.Millivolts)
                / (double)(right.Millivolts - left.Millivolts);
            return left.Percent + (right.Percent - left.Percent) * fraction;
        }

        return Points[^1].Percent;
    }
}
