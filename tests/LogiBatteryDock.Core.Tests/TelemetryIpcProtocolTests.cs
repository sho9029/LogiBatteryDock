using System.Text;
using LogiBatteryDock.Core;
using Xunit;

namespace LogiBatteryDock.Core.Tests;

public sealed class TelemetryIpcProtocolTests
{
    [Fact]
    public void RequestRoundTripsWithVersionAndRequestId()
    {
        var original = new TelemetryIpcRequest
        {
            Command = TelemetryIpcProtocol.GetStatusCommand,
            RequestId = "test-1"
        };

        var parsed = TelemetryIpcProtocol.TryParseRequest(
            TelemetryIpcProtocol.SerializeRequest(original),
            out var request,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal(original, request);
    }

    [Fact]
    public void UnknownVersionIsRejected()
    {
        var message = Encoding.UTF8.GetBytes(
            "{\"version\":99,\"command\":\"get_status\"}");

        var parsed = TelemetryIpcProtocol.TryParseRequest(message, out _, out var error);

        Assert.False(parsed);
        Assert.Equal("version_unsupported", error);
    }

    [Fact]
    public void OversizedRequestIsRejected()
    {
        var message = new byte[TelemetryIpcProtocol.MaxMessageBytes + 1];

        var parsed = TelemetryIpcProtocol.TryParseRequest(message, out _, out var error);

        Assert.False(parsed);
        Assert.Equal("message_size_invalid", error);
    }

    [Fact]
    public void Utf8BomIsAccepted()
    {
        var payload = Encoding.UTF8.GetBytes(
            "{\"version\":1,\"command\":\"get_status\"}");
        var message = new byte[payload.Length + 3];
        Encoding.UTF8.GetPreamble().CopyTo(message, 0);
        payload.CopyTo(message, 3);

        var parsed = TelemetryIpcProtocol.TryParseRequest(message, out var request, out var error);

        Assert.True(parsed, error);
        Assert.Equal(TelemetryIpcProtocol.GetStatusCommand, request!.Command);
    }

    [Fact]
    public void StatusDocumentDoesNotExposeReceiverPath()
    {
        var telemetry = new DeviceTelemetry(
            "G703",
            0xC547,
            1,
            0x1001,
            null,
            3969,
            PowerState.Discharging,
            0x01)
        {
            ReceiverKey = "raw-hid-path-that-must-not-leave-the-process"
        };
        var snapshot = new LogitechReadSnapshot(1, [telemetry])
        {
            ReceiverKeys = [telemetry.ReceiverKey]
        };

        var status = TelemetryIpcProtocol.CreateStatus(
            snapshot,
            [new MergedDeviceTelemetry(telemetry, IsStale: false)]);
        var response = TelemetryIpcProtocol.SerializeResponse(
            TelemetryIpcProtocol.CreateSuccessResponse(
                new TelemetryIpcRequest { Command = TelemetryIpcProtocol.GetStatusCommand },
                status));
        var json = Encoding.UTF8.GetString(response);

        Assert.DoesNotContain(telemetry.ReceiverKey, json, StringComparison.Ordinal);
        Assert.Contains("device:", json, StringComparison.Ordinal);
        Assert.Contains("\"percent\":null", json, StringComparison.Ordinal);
    }
}
