using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace LogiBatteryDock.Core;

/// <summary>
/// Reads the same percentage that G HUB exposes through its local agent.
/// The agent is optional; callers should use <see cref="GHubAwareTelemetryReader"/>
/// when a native HID++ fallback is desired.
/// </summary>
public sealed class GHubAgentTelemetryReader : ITelemetryReader
{
    public const int DefaultPort = 9010;
    public const int MaxMessageBytes = 1024 * 1024;

    private readonly Uri _endpoint;

    public GHubAgentTelemetryReader(int port = DefaultPort)
    {
        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        _endpoint = new Uri($"ws://127.0.0.1:{port}");
    }

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(2);

    public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        try
        {
            return ReadSnapshotAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (GHubAgentUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GHubAgentUnavailableException("G HUB agent request timed out.");
        }
        catch (Exception exception) when (IsUnavailableException(exception))
        {
            throw new GHubAgentUnavailableException(
                $"G HUB agent request failed: {exception.GetType().Name}",
                exception);
        }
    }

    private async Task<LogitechReadSnapshot> ReadSnapshotAsync(CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("json");
        socket.Options.SetRequestHeader("Origin", "file://");
        await socket.ConnectAsync(_endpoint, cancellationToken).ConfigureAwait(false);

        var listResponse = await RequestAsync(socket, "/devices/list", cancellationToken)
            .ConfigureAwait(false);
        var devices = GHubAgentResponseParser.ParseDeviceList(listResponse);
        var batteryDevices = devices.Where(device => device.HasBatteryStatus).ToArray();
        var readings = new List<DeviceTelemetry>(batteryDevices.Length);
        var failures = new List<DeviceReadFailure>();

        foreach (var device in batteryDevices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = await RequestAsync(
                    socket,
                    $"/battery/{Uri.EscapeDataString(device.Id)}/state",
                    cancellationToken)
                .ConfigureAwait(false);
            if (GHubAgentResponseParser.TryParseBatteryState(
                response,
                device,
                out var telemetry,
                out var error)
                && telemetry is not null)
            {
                readings.Add(telemetry);
            }
            else
            {
                failures.Add(new(
                    device.ProductId,
                    null,
                    DeviceReadFailureKind.BatteryReadFailed,
                    error ?? "G HUB battery response was invalid"));
            }
        }

        var receiverKeys = readings
            .Select(reading => reading.ReceiverKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var productIds = readings
            .Select(reading => reading.ReceiverProductId)
            .Where(productId => productId != 0)
            .Distinct()
            .ToArray();

        return new LogitechReadSnapshot(readings.Count, readings)
        {
            ReceiverProductIds = productIds,
            ReceiverKeys = receiverKeys,
            Failures = failures
        };
    }

    private static async Task<JsonElement> RequestAsync(
        ClientWebSocket socket,
        string path,
        CancellationToken cancellationToken)
    {
        var messageId = Guid.NewGuid().ToString("N");
        var request = JsonSerializer.SerializeToUtf8Bytes(new
        {
            msgId = messageId,
            verb = "GET",
            path
        });
        await socket.SendAsync(
                new ArraySegment<byte>(request),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken)
            .ConfigureAwait(false);

        while (true)
        {
            var message = await ReceiveTextAsync(socket, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (!root.TryGetProperty("path", out var responsePath)
                || !string.Equals(responsePath.GetString(), path, StringComparison.Ordinal))
            {
                continue;
            }

            if (root.TryGetProperty("msgId", out var responseId)
                && !string.IsNullOrWhiteSpace(responseId.GetString())
                && !string.Equals(responseId.GetString(), messageId, StringComparison.Ordinal))
            {
                continue;
            }

            return root.Clone();
        }
    }

    private static async Task<string> ReceiveTextAsync(
        ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            var builder = new StringBuilder();
            var totalBytes = 0;
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new WebSocketException("G HUB agent closed the websocket.");
                }

                totalBytes += result.Count;
                if (totalBytes > MaxMessageBytes)
                {
                    throw new InvalidOperationException("G HUB agent response is too large.");
                }

                builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
            while (!result.EndOfMessage);

            return builder.ToString();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static bool IsUnavailableException(Exception exception) =>
        exception is WebSocketException
            or IOException
            or JsonException
            or InvalidOperationException
            or UriFormatException;
}

/// <summary>
/// Prefers G HUB's own percentage and falls back to native HID++. The two
/// sources deliberately keep their own conversion rules: G HUB supplies its
/// device-specific percentage, while native HID++ uses the generic fallback
/// curve without learning or persisting cross-source corrections.
/// </summary>
public sealed class GHubAwareTelemetryReader : ITelemetryReader
{
    private readonly ITelemetryReader _ghubReader;
    private readonly ITelemetryReader _nativeReader;
    private readonly IBatteryVoltageFallbackProvider _fallbackProvider;

    public GHubAwareTelemetryReader()
        : this(
            new GHubAgentTelemetryReader(),
            new LogitechDeviceReader(),
            new BatteryVoltageFallbackProvider())
    {
    }

    /// <summary>
    /// Creates a reader with a custom G HUB port. This is useful for a
    /// repeatable fallback check against an intentionally unavailable agent.
    /// </summary>
    public GHubAwareTelemetryReader(int ghubPort)
        : this(
            new GHubAgentTelemetryReader(ghubPort),
            new LogitechDeviceReader(),
            new BatteryVoltageFallbackProvider())
    {
    }

    internal GHubAwareTelemetryReader(
        ITelemetryReader ghubReader,
        ITelemetryReader nativeReader,
        IBatteryVoltageFallbackProvider? fallbackProvider = null)
    {
        _ghubReader = ghubReader;
        _nativeReader = nativeReader;
        _fallbackProvider = fallbackProvider ?? new BatteryVoltageFallbackProvider();
    }

    public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default)
    {
        try
        {
            var ghubSnapshot = _ghubReader.ReadSnapshot(cancellationToken);
            if (ghubSnapshot.Devices.Count > 0)
            {
                return ghubSnapshot;
            }
        }
        catch (GHubAgentUnavailableException) when (!cancellationToken.IsCancellationRequested)
        {
            // G HUB is optional. Native HID++ remains the fallback source.
        }

        return ApplyFallback(_nativeReader.ReadSnapshot(cancellationToken));
    }

    private LogitechReadSnapshot ApplyFallback(LogitechReadSnapshot snapshot)
    {
        var devices = new DeviceTelemetry[snapshot.Devices.Count];
        var changed = false;
        for (var index = 0; index < snapshot.Devices.Count; index++)
        {
            var original = snapshot.Devices[index];
            var fallback = _fallbackProvider.Apply(original);
            devices[index] = fallback;
            changed |= !ReferenceEquals(original, fallback);
        }

        return changed
            ? snapshot with { Devices = devices }
            : snapshot;
    }
}

internal sealed class GHubAgentUnavailableException : Exception
{
    public GHubAgentUnavailableException(string message)
        : base(message)
    {
    }

    public GHubAgentUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed record GHubAgentDeviceInfo(
    string Id,
    int ProductId,
    string Name,
    string IdentityKey,
    bool HasBatteryStatus);

internal static class GHubAgentResponseParser
{
    public static IReadOnlyList<GHubAgentDeviceInfo> ParseDeviceList(JsonElement root)
    {
        EnsureSuccess(root);
        if (!TryGetProperty(root, "payload", out var payload)
            || !payload.TryGetProperty("deviceInfos", out var deviceInfos)
            || deviceInfos.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("G HUB device list did not contain deviceInfos.");
        }

        var result = new List<GHubAgentDeviceInfo>();
        foreach (var device in deviceInfos.EnumerateArray())
        {
            var id = GetString(device, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var productId = TryGetInt32(device, "pid") ?? 0;
            var name = FirstNonEmpty(
                GetString(device, "extendedDisplayName"),
                GetString(device, "displayName"),
                GetString(device, "deviceModel"),
                id);
            var identityKey = FirstNonEmpty(
                GetString(device, "deviceSignature"),
                GetString(device, "deviceUnitId"),
                id);
            var hasBatteryStatus = device.TryGetProperty("capabilities", out var capabilities)
                && capabilities.TryGetProperty("hasBatteryStatus", out var battery)
                && battery.ValueKind == JsonValueKind.True;
            result.Add(new(id, productId, name, identityKey, hasBatteryStatus));
        }

        return result;
    }

    public static bool TryParseBatteryState(
        JsonElement root,
        GHubAgentDeviceInfo device,
        out DeviceTelemetry? telemetry,
        out string? error)
    {
        telemetry = null;
        error = null;
        if (!TryGetSuccess(root, out var resultError))
        {
            error = resultError;
            return false;
        }

        if (!TryGetProperty(root, "payload", out var payload)
            || !payload.TryGetProperty("percentage", out var percentageElement)
            || !percentageElement.TryGetInt32(out var percentage)
            || percentage is < 0 or > 100)
        {
            error = "G HUB battery response did not contain a percentage in 0..100";
            return false;
        }

        var charging = GetBoolean(payload, "charging");
        var fullyCharged = GetBoolean(payload, "fullyCharged");
        var powerState = fullyCharged
            ? PowerState.Full
            : charging
                ? PowerState.Charging
                : PowerState.Discharging;
        var rawStatus = fullyCharged ? (byte)3 : charging ? (byte)1 : (byte)0;
        telemetry = new DeviceTelemetry(
            device.Name,
            device.ProductId,
            0,
            0,
            percentage,
            null,
            powerState,
            rawStatus)
        {
            ReceiverKey = $"ghub:{device.IdentityKey}",
            Source = TelemetrySource.GHubAgent,
            SourceDeviceId = device.Id
        };
        return true;
    }

    private static void EnsureSuccess(JsonElement root)
    {
        if (!TryGetSuccess(root, out var error))
        {
            throw new JsonException(error);
        }
    }

    private static bool TryGetSuccess(JsonElement root, out string error)
    {
        error = "G HUB agent response was missing a successful result.";
        if (!root.TryGetProperty("result", out var result)
            || !result.TryGetProperty("code", out var codeElement))
        {
            return false;
        }

        var code = codeElement.GetString();
        if (string.Equals(code, "SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            error = string.Empty;
            return true;
        }

        var detail = GetString(result, "what");
        error = string.IsNullOrWhiteSpace(detail) ? $"G HUB agent returned {code}" : detail;
        return false;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value) =>
        element.TryGetProperty(name, out value);

    private static int? TryGetInt32(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;

    private static bool GetBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.True;

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "Logitech device";
}
