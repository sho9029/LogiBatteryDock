namespace LogiBatteryDock.Core;

public readonly record struct DeviceIdentity(string ReceiverKey, int ReceiverProductId, byte DeviceIndex);

public sealed record MergedDeviceTelemetry(DeviceTelemetry Telemetry, bool IsStale);

/// <summary>
/// Keeps the last device-reported reading while a known receiver is present but
/// the device does not answer the current scan.
/// </summary>
public sealed class DeviceStateMerger
{
    private readonly Dictionary<DeviceIdentity, DeviceTelemetry> _lastKnown = [];

    public IReadOnlyList<MergedDeviceTelemetry> Merge(LogitechReadSnapshot snapshot)
    {
        var receiverPresence = ReceiverPresence(snapshot);
        var liveKeys = new HashSet<DeviceIdentity>();
        var merged = new List<MergedDeviceTelemetry>(snapshot.Devices.Count + _lastKnown.Count);

        foreach (var telemetry in snapshot.Devices)
        {
            var key = new DeviceIdentity(
                ReceiverKey(telemetry),
                telemetry.ReceiverProductId,
                telemetry.DeviceIndex);
            _lastKnown[key] = telemetry;
            if (liveKeys.Add(key))
            {
                merged.Add(new(telemetry, IsStale: false));
            }
        }

        var removed = new List<DeviceIdentity>();
        foreach (var pair in _lastKnown)
        {
            if (liveKeys.Contains(pair.Key))
            {
                continue;
            }

            var receiverPresent = receiverPresence.Keys.Count > 0
                ? receiverPresence.Keys.Contains(pair.Key.ReceiverKey)
                : receiverPresence.ProductIds.Contains(pair.Key.ReceiverProductId);
            if (receiverPresent)
            {
                merged.Add(new(pair.Value, IsStale: true));
            }
            else
            {
                removed.Add(pair.Key);
            }
        }

        foreach (var key in removed)
        {
            _lastKnown.Remove(key);
        }

        return merged;
    }

    private (HashSet<string> Keys, HashSet<int> ProductIds) ReceiverPresence(LogitechReadSnapshot snapshot)
    {
        if (snapshot.ReceiverKeys.Count > 0)
        {
            return (
                snapshot.ReceiverKeys.ToHashSet(StringComparer.Ordinal),
                new HashSet<int>());
        }

        if (snapshot.ReceiverProductIds.Count > 0)
        {
            return (
                new HashSet<string>(StringComparer.Ordinal),
                snapshot.ReceiverProductIds.ToHashSet());
        }

        // Snapshots created by older callers do not have receiver identity. If
        // they still report a receiver, retain known product IDs conservatively
        // instead of claiming that every previously seen device was unplugged.
        return (
            new HashSet<string>(StringComparer.Ordinal),
            snapshot.ReceiverCount > 0
                ? _lastKnown.Keys.Select(key => key.ReceiverProductId).ToHashSet()
                : new HashSet<int>());
    }

    private static string ReceiverKey(DeviceTelemetry telemetry) =>
        string.IsNullOrEmpty(telemetry.ReceiverKey)
            ? $"pid:{telemetry.ReceiverProductId:X4}"
            : telemetry.ReceiverKey;
}
