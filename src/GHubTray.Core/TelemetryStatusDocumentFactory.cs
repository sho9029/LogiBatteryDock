using System.Security.Cryptography;
using System.Text;

namespace GHubTray.Core;

public static class TelemetryStatusDocumentFactory
{
    public static TelemetryStatusDocument CreateStatus(
        LogitechReadSnapshot snapshot,
        IReadOnlyList<MergedDeviceTelemetry> states,
        string? error = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(states);

        var devices = states.Select(state =>
        {
            var telemetry = state.Telemetry;
            var display = TelemetryDisplayFormatter.Format(telemetry, state.IsStale);
            return new TelemetryDeviceStatus(
                CreateDeviceId(telemetry),
                telemetry.Name,
                display.BatteryText,
                display.PowerText,
                display.TechnicalText,
                state.IsStale,
                telemetry.IsExactPercentage,
                telemetry.Percent,
                telemetry.Millivolts,
                telemetry.PowerState,
                telemetry.ObservedAt,
                telemetry.ReceiverProductId,
                telemetry.DeviceIndex);
        }).ToArray();

        var failures = snapshot.Failures
            .Select(failure => new TelemetryFailureStatus(
                failure.ReceiverProductId,
                failure.DeviceIndex,
                failure.Kind,
                failure.Detail))
            .ToArray();

        return new(
            snapshot.CheckedAt,
            snapshot.ReceiverCount,
            devices,
            failures,
            error);
    }

    private static string CreateDeviceId(DeviceTelemetry telemetry)
    {
        var receiverKey = string.IsNullOrWhiteSpace(telemetry.ReceiverKey)
            ? $"pid:{telemetry.ReceiverProductId:X4}"
            : telemetry.ReceiverKey;
        var source = Encoding.UTF8.GetBytes($"{receiverKey}:{telemetry.DeviceIndex:X2}");
        var digest = SHA256.HashData(source);
        return $"device:{Convert.ToHexString(digest.AsSpan(0, 12))}";
    }
}
