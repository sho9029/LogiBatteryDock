using HidSharp;
using System.Security.Cryptography;
using System.Text;

namespace GHubTray.Core;

public sealed class LogitechDeviceReader
{
    private const int LogitechVendorId = 0x046D;

    public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromSeconds(8);

    public IReadOnlyList<DeviceTelemetry> ReadAll(CancellationToken cancellationToken = default)
        => ReadSnapshot(cancellationToken).Devices;

    public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default)
    {
        var results = new List<DeviceTelemetry>();
        var failures = new List<DeviceReadFailure>();
        var deadline = new ScanDeadline(ScanTimeout);
        var scanTimedOut = false;
        HidDevice[] interfaces;
        try
        {
            interfaces = DeviceList.Local.GetHidDevices(LogitechVendorId)
                // HID++ long reports are 20 bytes. Ping below is the authority: other Logitech
                // collections can pass this size filter, but cannot be mistaken for a device.
                .Where(IsHidppLongInterface)
                .ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsExpired)
            {
                scanTimedOut = true;
            }
        }
        catch (Exception exception) when (IsExpectedDeviceException(exception))
        {
            failures.Add(new(0, null, DeviceReadFailureKind.EnumerationFailed, exception.GetType().Name));
            return new(0, results) { Failures = failures };
        }

        foreach (var device in interfaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsExpired)
            {
                scanTimedOut = true;
                break;
            }
            HidStream? stream;
            try
            {
                if (!device.TryOpen(out stream) || stream is null)
                {
                    failures.Add(new(device.ProductID, null, DeviceReadFailureKind.OpenFailed, "HID interface could not be opened"));
                    continue;
                }
            }
            catch (Exception exception) when (IsExpectedDeviceException(exception))
            {
                failures.Add(new(device.ProductID, null, DeviceReadFailureKind.OpenFailed, exception.GetType().Name));
                continue;
            }

            using (stream)
            {
                var receiverKey = GetReceiverKey(device);
                var session = new HidppSession(new HidSharpChannel(stream));
                var responsive = false;
                var batteryFeatureFound = false;
                var batteryReadSucceeded = false;
                foreach (var index in CandidateIndexes())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (deadline.IsExpired)
                    {
                        scanTimedOut = true;
                        break;
                    }
                    if (!session.Ping(index, cancellationToken))
                    {
                        continue;
                    }
                    responsive = true;

                    if (deadline.IsExpired)
                    {
                        scanTimedOut = true;
                        break;
                    }
                    var feature = session.ResolveBatteryFeature(index, cancellationToken);
                    if (feature is null)
                    {
                        continue;
                    }
                    batteryFeatureFound = true;

                    if (deadline.IsExpired)
                    {
                        scanTimedOut = true;
                        break;
                    }
                    var name = session.ReadName(index, cancellationToken) ?? $"Logitech HID++ device {index:X2}";
                    if (deadline.IsExpired)
                    {
                        scanTimedOut = true;
                        break;
                    }
                    var reply = session.ReadBattery(index, feature.Value.Id, feature.Value.Index, cancellationToken);
                    if (reply is null)
                    {
                        failures.Add(new(device.ProductID, index, DeviceReadFailureKind.BatteryReadFailed, "battery feature did not answer"));
                        continue;
                    }

                    if (BatteryTelemetryDecoder.TryDecode(
                        name, device.ProductID, index, feature.Value.Id, reply, out var telemetry, out var error)
                        && telemetry is not null)
                    {
                        results.Add(telemetry with { ReceiverKey = receiverKey });
                        batteryReadSucceeded = true;
                    }
                    else
                    {
                        failures.Add(new(device.ProductID, index, DeviceReadFailureKind.InvalidResponse, error ?? "invalid battery response"));
                    }
                }

                if (!scanTimedOut && deadline.IsExpired)
                {
                    scanTimedOut = true;
                }
                if (!scanTimedOut && !responsive)
                {
                    failures.Add(new(device.ProductID, null, DeviceReadFailureKind.NoResponsiveDevice, "no candidate device answered HID++ ping"));
                }
                else if (!scanTimedOut && !batteryFeatureFound)
                {
                    failures.Add(new(device.ProductID, null, DeviceReadFailureKind.BatteryFeatureUnsupported, "responsive device exposes no supported battery feature"));
                }
                else if (!scanTimedOut && !batteryReadSucceeded)
                {
                    failures.Add(new(device.ProductID, null, DeviceReadFailureKind.BatteryReadFailed, "no valid battery reading was obtained"));
                }
            }

            if (scanTimedOut)
            {
                break;
            }
        }

        if (scanTimedOut)
        {
            failures.Add(new(
                0,
                null,
                DeviceReadFailureKind.ScanTimedOut,
                $"scan exceeded {ScanTimeout.TotalSeconds:0.###} seconds"));
        }

        var receiverIds = interfaces
            .Select(device => device.ProductID)
            .Distinct()
            .ToArray();
        var receiverKeys = interfaces
            .Select(GetReceiverKey)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new(receiverKeys.Length, results)
        {
            ReceiverProductIds = receiverIds,
            ReceiverKeys = receiverKeys,
            Failures = failures
        };
    }

    private static IEnumerable<byte> CandidateIndexes()
    {
        for (byte index = 1; index <= 6; index++)
        {
            yield return index;
        }
        yield return 0xFF;
    }

    private static bool IsHidppLongInterface(HidDevice device)
    {
        try
        {
            if (device.GetMaxInputReportLength() < 20 || device.GetMaxOutputReportLength() < 20)
            {
                return false;
            }

            // On Windows the HID++ usage 0x0002 collection is MI_02/COL02.
            // Keep the report-length fallback for HidSharp backends with an opaque path.
            var path = device.DevicePath;
            return string.IsNullOrEmpty(path)
                || (path.Contains("MI_02", StringComparison.OrdinalIgnoreCase)
                    && path.Contains("COL02", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception) when (IsExpectedDeviceException(exception))
        {
            return false;
        }
    }

    private static string GetReceiverKey(HidDevice device)
    {
        var productId = device.ProductID;
        try
        {
            var path = device.DevicePath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                var digest = SHA256.HashData(Encoding.UTF8.GetBytes(path));
                return $"path:{Convert.ToHexString(digest.AsSpan(0, 8))}";
            }
        }
        catch (Exception exception) when (IsExpectedDeviceException(exception))
        {
        }

        return $"pid:{productId:X4}";
    }

    private static bool IsExpectedDeviceException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException;
}
