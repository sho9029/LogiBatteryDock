using System.IO.Pipes;
using GHubTray.Core;

namespace GHubTray.App;

internal sealed class TelemetryPipeServer
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(12);
    private readonly Func<TelemetryStatusDocument> _getStatus;
    private readonly Func<CancellationToken, Task> _refresh;
    private readonly Action _openTray;
    private readonly CancellationTokenSource _stop = new();
    private Task? _acceptLoop;

    public TelemetryPipeServer(
        Func<TelemetryStatusDocument> getStatus,
        Func<CancellationToken, Task> refresh,
        Action openTray)
    {
        _getStatus = getStatus;
        _refresh = refresh;
        _openTray = openTray;
    }

    public void Start()
    {
        _acceptLoop = AcceptLoopAsync(_stop.Token);
    }

    public async Task StopAsync()
    {
        _stop.Cancel();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _stop.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = CreateServer();
                await pipe.WaitForConnectionAsync(cancellationToken);
                await HandleConnectionAsync(pipe, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
                // A client can disconnect while a request is in flight. Recreate the pipe.
            }
            catch (InvalidDataException)
            {
                // Malformed or oversized input is isolated to this connection.
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"GHubTray IPC server error: {exception.GetType().Name}");
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private static NamedPipeServerStream CreateServer() => new(
        TelemetryIpcProtocol.PipeName,
        PipeDirection.InOut,
        1,
        PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
        inBufferSize: 4096,
        outBufferSize: 4096);

    private async Task HandleConnectionAsync(
        NamedPipeServerStream pipe,
        CancellationToken lifetimeCancellationToken)
    {
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(
            lifetimeCancellationToken,
            _stop.Token);
        requestTimeout.CancelAfter(RequestTimeout);

        var requestBytes = await ReadMessageAsync(pipe, requestTimeout.Token);
        if (requestBytes is null)
        {
            return;
        }

        TelemetryIpcResponse response;
        if (!TelemetryIpcProtocol.TryParseRequest(requestBytes, out var request, out var error))
        {
            response = TelemetryIpcProtocol.CreateErrorResponse(error);
        }
        else
        {
            response = await ExecuteRequestAsync(request!, requestTimeout.Token);
        }

        var responseBytes = TelemetryIpcProtocol.SerializeResponse(response);
        await pipe.WriteAsync(responseBytes, requestTimeout.Token);
        await pipe.WriteAsync(new byte[] { (byte)'\n' }, requestTimeout.Token);
        await pipe.FlushAsync(requestTimeout.Token);
    }

    private async Task<TelemetryIpcResponse> ExecuteRequestAsync(
        TelemetryIpcRequest request,
        CancellationToken cancellationToken)
    {
        switch (request.Command)
        {
            case TelemetryIpcProtocol.GetStatusCommand:
                return TelemetryIpcProtocol.CreateSuccessResponse(request, _getStatus());
            case TelemetryIpcProtocol.RefreshCommand:
                await _refresh(cancellationToken);
                return TelemetryIpcProtocol.CreateSuccessResponse(request, _getStatus());
            case TelemetryIpcProtocol.OpenTrayCommand:
                _openTray();
                return TelemetryIpcProtocol.CreateSuccessResponse(request, _getStatus());
            default:
                return TelemetryIpcProtocol.CreateErrorResponse(
                    "command_unsupported",
                    request.RequestId);
        }
    }

    private static async Task<byte[]?> ReadMessageAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var buffer = new byte[1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return message.Length == 0 ? null : message.ToArray();
            }

            var newline = Array.IndexOf(buffer, (byte)'\n', 0, read);
            var count = newline >= 0 ? newline : read;
            if (message.Length + count > TelemetryIpcProtocol.MaxMessageBytes)
            {
                throw new InvalidDataException("IPC message exceeds the size limit.");
            }

            message.Write(buffer, 0, count);
            if (newline >= 0)
            {
                return message.ToArray();
            }
        }
    }

}
