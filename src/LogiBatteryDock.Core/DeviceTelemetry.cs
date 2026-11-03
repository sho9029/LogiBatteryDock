namespace LogiBatteryDock.Core;

public enum PowerState
{
    Unknown,
    Discharging,
    Charging,
    Full,
    ExternalPower
}

public enum TelemetrySource
{
    NativeHidpp,
    GHubAgent,
    NativeVoltageEstimate
}

public sealed record DeviceTelemetry(
    string Name,
    int ReceiverProductId,
    byte DeviceIndex,
    ushort BatteryFeature,
    int? Percent,
    int? Millivolts,
    PowerState PowerState,
    byte RawStatus)
{
    public string ReceiverKey { get; init; } = string.Empty;
    public TelemetrySource Source { get; init; } = TelemetrySource.NativeHidpp;
    public string? SourceDeviceId { get; init; }
    public bool IsEstimatedPercentage => Percent is not null
        && Source is TelemetrySource.NativeVoltageEstimate;
    public bool IsExactPercentage => Percent is not null
        && !IsEstimatedPercentage
        && (Source is TelemetrySource.GHubAgent || BatteryFeature is 0x1000 or 0x1004);
    public bool HasPercentage => Percent is not null
        && (IsExactPercentage || IsEstimatedPercentage);
    public DateTimeOffset ObservedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record LogitechReadSnapshot(int ReceiverCount, IReadOnlyList<DeviceTelemetry> Devices)
{
    public IReadOnlyList<int> ReceiverProductIds { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> ReceiverKeys { get; init; } = Array.Empty<string>();
    public IReadOnlyList<DeviceReadFailure> Failures { get; init; } = Array.Empty<DeviceReadFailure>();
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;
}

public enum DeviceReadFailureKind
{
    EnumerationFailed,
    OpenFailed,
    NoResponsiveDevice,
    BatteryFeatureUnsupported,
    BatteryReadFailed,
    InvalidResponse,
    ScanTimedOut
}

public sealed record DeviceReadFailure(
    int ReceiverProductId,
    byte? DeviceIndex,
    DeviceReadFailureKind Kind,
    string Detail);
