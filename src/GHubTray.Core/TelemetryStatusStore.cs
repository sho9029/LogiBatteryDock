namespace GHubTray.Core;

/// <summary>
/// Owns one frontend's live telemetry state. The reader is deliberately
/// injected so each process can use the same Core contract without sharing a
/// runtime cache or requiring another process to be alive.
/// </summary>
public sealed class TelemetryStatusStore
{
    private readonly ITelemetryReader _reader;
    private readonly DeviceStateMerger _stateMerger = new();
    private readonly object _gate = new();
    private TelemetryStatusDocument _status = InitialStatus();
    private IReadOnlyList<MergedDeviceTelemetry> _states = Array.Empty<MergedDeviceTelemetry>();
    private Task? _refreshTask;
    private bool _hasCompletedRead;

    public TelemetryStatusStore(ITelemetryReader? reader = null)
    {
        _reader = reader ?? new LogitechDeviceReader();
    }

    public event EventHandler? StatusChanged;

    public bool IsRefreshing
    {
        get
        {
            lock (_gate)
            {
                return _refreshTask is { IsCompleted: false };
            }
        }
    }

    public TelemetryStatusDocument GetStatus()
    {
        lock (_gate)
        {
            return _status;
        }
    }

    public IReadOnlyList<MergedDeviceTelemetry> GetMergedStates()
    {
        lock (_gate)
        {
            return _states;
        }
    }

    /// <summary>
    /// Starts the first read once. Subsequent reads are explicit through
    /// <see cref="RefreshAsync"/> so rebuilding a page does not create a scan
    /// loop.
    /// </summary>
    public bool EnsureInitialRefresh()
    {
        lock (_gate)
        {
            if (_hasCompletedRead || _refreshTask is { IsCompleted: false })
            {
                return false;
            }

            _refreshTask = RefreshCoreAsync(CancellationToken.None);
            return true;
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_refreshTask is { IsCompleted: false } active)
            {
                return active;
            }

            _refreshTask = RefreshCoreAsync(cancellationToken);
            return _refreshTask;
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await Task.Run(
                () => _reader.ReadSnapshot(cancellationToken),
                cancellationToken).ConfigureAwait(false);
            var states = _stateMerger.Merge(snapshot);
            SetStatus(TelemetryIpcProtocol.CreateStatus(snapshot, states), states);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Telemetry read failed: {exception.GetType().Name}");
            SetError("read_failed");
        }
        finally
        {
            lock (_gate)
            {
                _refreshTask = null;
            }
        }
    }

    private void SetStatus(
        TelemetryStatusDocument status,
        IReadOnlyList<MergedDeviceTelemetry> states)
    {
        EventHandler? handler;
        lock (_gate)
        {
            _status = status;
            _states = states;
            _hasCompletedRead = true;
            handler = StatusChanged;
        }

        NotifyStatusChanged(handler);
    }

    private void SetError(string error)
    {
        EventHandler? handler;
        lock (_gate)
        {
            _status = new TelemetryStatusDocument(
                DateTimeOffset.UtcNow,
                0,
                Array.Empty<TelemetryDeviceStatus>(),
                Array.Empty<TelemetryFailureStatus>(),
                error);
            _states = Array.Empty<MergedDeviceTelemetry>();
            _hasCompletedRead = true;
            handler = StatusChanged;
        }

        NotifyStatusChanged(handler);
    }

    private void NotifyStatusChanged(EventHandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        try
        {
            handler.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Telemetry status notification failed: {exception.GetType().Name}");
        }
    }

    private static TelemetryStatusDocument InitialStatus() => new(
        DateTimeOffset.UtcNow,
        0,
        Array.Empty<TelemetryDeviceStatus>(),
        Array.Empty<TelemetryFailureStatus>(),
        "not_checked");
}
