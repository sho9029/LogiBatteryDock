namespace LogiBatteryDock.Core;

/// <summary>
/// Decodes only values that the device reports in its HID++ battery feature.
/// This type deliberately has no voltage-to-percentage conversion.
/// </summary>
public static class BatteryTelemetryDecoder
{
    public static bool TryDecode(
        string name,
        int receiverProductId,
        byte deviceIndex,
        ushort featureId,
        ReadOnlySpan<byte> reply,
        out DeviceTelemetry? telemetry,
        out string? error)
    {
        telemetry = null;
        error = null;
        if (reply.Length < 7)
        {
            error = "response frame is shorter than the HID++ header";
            return false;
        }

        switch (featureId)
        {
            case 0x1000:
            case 0x1004:
            {
                var reportedLevel = reply[4];
                if (reportedLevel is > 100)
                {
                    error = $"device-reported percentage is outside 0..100: {reportedLevel}";
                    return false;
                }

                var state = reply[6] switch
                {
                    0 => PowerState.Discharging,
                    1 or 2 or 4 => PowerState.Charging,
                    3 => PowerState.Full,
                    _ => PowerState.Unknown
                };
                telemetry = new DeviceTelemetry(
                    name,
                    receiverProductId,
                    deviceIndex,
                    featureId,
                    reportedLevel >= 1 && reportedLevel <= 100 ? reportedLevel : null,
                    null,
                    state,
                    reply[6]);
                return true;
            }

            case 0x1001:
            {
                var millivolts = (reply[4] << 8) | reply[5];
                if (millivolts == 0)
                {
                    error = "reported voltage is zero";
                    return false;
                }

                var flags = reply[6];
                telemetry = new DeviceTelemetry(
                    name,
                    receiverProductId,
                    deviceIndex,
                    featureId,
                    null,
                    millivolts,
                    (flags & 0x80) != 0 ? PowerState.ExternalPower : PowerState.Discharging,
                    flags);
                return true;
            }

            default:
                error = $"unsupported battery feature: 0x{featureId:X4}";
                return false;
        }
    }
}
