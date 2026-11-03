namespace GHubTray.Core;

internal static class HidppProtocol
{
    internal const byte LongReportId = 0x11;
    internal const byte SoftwareId = 0x0B;

    internal static byte[] Request(byte deviceIndex, byte featureIndex, byte functionId, params byte[] parameters)
    {
        var report = new byte[20];
        report[0] = LongReportId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = (byte)((functionId << 4) | SoftwareId);
        Array.Copy(parameters, 0, report, 4, Math.Min(parameters.Length, report.Length - 4));
        return report;
    }

    internal static byte[]? Normalize(byte[] buffer, int count)
    {
        if (count >= 20 && buffer[0] == LongReportId)
        {
            return buffer[..20];
        }

        if (count >= 21 && buffer[1] == LongReportId)
        {
            return buffer[1..21];
        }

        return null;
    }

    internal static DeviceTelemetry DecodeBattery(
        string name,
        int receiverProductId,
        byte deviceIndex,
        ushort featureId,
        byte[] reply)
    {
        if (BatteryTelemetryDecoder.TryDecode(
            name, receiverProductId, deviceIndex, featureId, reply, out var telemetry, out var error)
            && telemetry is not null)
        {
            return telemetry;
        }

        throw new FormatException(error ?? "invalid battery response");
    }
}

