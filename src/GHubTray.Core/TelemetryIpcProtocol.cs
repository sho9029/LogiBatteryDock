using System.Text.Json;
using System.Text.Json.Serialization;

namespace GHubTray.Core;

public static class TelemetryIpcProtocol
{
    public const int CurrentVersion = 1;
    public const int MaxMessageBytes = 64 * 1024;
    public const string PipeName = "GHubTray.Status.v1";
    public const string GetStatusCommand = "get_status";
    public const string RefreshCommand = "refresh";
    public const string OpenTrayCommand = "open_tray";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static byte[] SerializeRequest(TelemetryIpcRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(request, JsonOptions);
        EnsureMessageSize(bytes);
        return bytes;
    }

    public static byte[] SerializeResponse(TelemetryIpcResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(response, JsonOptions);
        EnsureMessageSize(bytes);
        return bytes;
    }

    public static bool TryParseRequest(
        ReadOnlySpan<byte> message,
        out TelemetryIpcRequest? request,
        out string error)
    {
        request = null;
        error = string.Empty;
        if (message.Length >= 3 &&
            message[0] == 0xEF &&
            message[1] == 0xBB &&
            message[2] == 0xBF)
        {
            message = message[3..];
        }
        if (message.Length == 0 || message.Length > MaxMessageBytes)
        {
            error = "message_size_invalid";
            return false;
        }

        try
        {
            request = JsonSerializer.Deserialize<TelemetryIpcRequest>(message, JsonOptions);
        }
        catch (JsonException)
        {
            error = "json_invalid";
            return false;
        }
        catch (NotSupportedException)
        {
            error = "json_unsupported";
            return false;
        }

        if (request is null)
        {
            error = "request_missing";
            return false;
        }
        if (request.Version != CurrentVersion)
        {
            error = "version_unsupported";
            return false;
        }
        if (string.IsNullOrWhiteSpace(request.Command) || request.Command.Length > 64)
        {
            error = "command_invalid";
            return false;
        }
        if (request.RequestId is { Length: > 64 })
        {
            error = "request_id_invalid";
            return false;
        }

        return true;
    }

    public static TelemetryStatusDocument CreateStatus(
        LogitechReadSnapshot snapshot,
        IReadOnlyList<MergedDeviceTelemetry> states,
        string? error = null) =>
        TelemetryStatusDocumentFactory.CreateStatus(snapshot, states, error);

    public static TelemetryIpcResponse CreateSuccessResponse(
        TelemetryIpcRequest request,
        TelemetryStatusDocument? status = null) =>
        new(CurrentVersion, request.RequestId ?? string.Empty, true, null, status);

    public static TelemetryIpcResponse CreateErrorResponse(
        string error,
        string? requestId = null) =>
        new(CurrentVersion, requestId ?? string.Empty, false, error, null);

    private static void EnsureMessageSize(byte[] bytes)
    {
        if (bytes.Length > MaxMessageBytes)
        {
            throw new InvalidOperationException("IPC message exceeds the size limit.");
        }
    }
}

public sealed record TelemetryIpcRequest
{
    public int Version { get; init; } = TelemetryIpcProtocol.CurrentVersion;
    public string Command { get; init; } = string.Empty;
    public string? RequestId { get; init; }
}

public sealed record TelemetryIpcResponse(
    int Version,
    string RequestId,
    bool Ok,
    string? Error,
    TelemetryStatusDocument? Status);
