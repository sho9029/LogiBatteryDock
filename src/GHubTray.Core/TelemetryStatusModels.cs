namespace GHubTray.Core;

public sealed record TelemetryStatusDocument(
    DateTimeOffset CheckedAt,
    int ReceiverCount,
    IReadOnlyList<TelemetryDeviceStatus> Devices,
    IReadOnlyList<TelemetryFailureStatus> Failures,
    string? Error);

public sealed record TelemetryDeviceStatus(
    string Id,
    string Name,
    string BatteryText,
    string PowerText,
    string TechnicalText,
    bool IsStale,
    bool IsExactPercentage,
    int? Percent,
    int? Millivolts,
    PowerState PowerState,
    DateTimeOffset ObservedAt,
    int ReceiverProductId,
    byte DeviceIndex);

public sealed record TelemetryFailureStatus(
    int ReceiverProductId,
    byte? DeviceIndex,
    DeviceReadFailureKind Kind,
    string Detail);
