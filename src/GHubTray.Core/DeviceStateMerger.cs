namespace GHubTray.Core;

public readonly record struct DeviceIdentity(int ReceiverProductId, byte DeviceIndex);

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
        var receiverIds = ReceiverIds(snapshot);
        var liveKeys = new HashSet<DeviceIdentity>();
        var merged = new List<MergedDeviceTelemetry>(snapshot.Devices.Count + _lastKnown.Count);

        foreach (var telemetry in snapshot.Devices)
        {
            var key = new DeviceIdentity(telemetry.ReceiverProductId, telemetry.DeviceIndex);
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

            if (receiverIds.Contains(pair.Key.ReceiverProductId))
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

    private HashSet<int> ReceiverIds(LogitechReadSnapshot snapshot)
    {
        if (snapshot.ReceiverProductIds.Count > 0)
        {
            return snapshot.ReceiverProductIds.ToHashSet();
        }

        // Snapshots created by older callers do not have IDs. If they still
        // report a receiver, retain known IDs conservatively instead of
        // claiming that every previously seen device was unplugged.
        return snapshot.ReceiverCount > 0
            ? _lastKnown.Keys.Select(key => key.ReceiverProductId).ToHashSet()
            : [];
    }
}
