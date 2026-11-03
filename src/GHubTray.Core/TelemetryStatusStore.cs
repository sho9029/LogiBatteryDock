using System.Threading.Channels;

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
    private readonly List<TelemetryStatusSubscription> _subscriptions = [];
    private long _revision;

    public TelemetryStatusStore(ITelemetryReader? reader = null)
    {
        _reader = reader ?? new GHubAwareTelemetryReader();
    }

    // Publishing a snapshot must never invoke frontend or remote host code.
    // Each subscriber holds only the latest update, including an initial replay.
    public TelemetryStatusSubscription Subscribe()
    {
        lock (_gate)
        {
            var subscription = new TelemetryStatusSubscription(RemoveSubscription);
            _subscriptions.Add(subscription);
            subscription.Publish(new(_revision, _status));
            return subscription;
        }
    }

    private void RemoveSubscription(TelemetryStatusSubscription subscription)
    {
        lock (_gate)
        {
            _subscriptions.Remove(subscription);
        }
    }

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

            StartRefresh(CancellationToken.None);
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

            return StartRefresh(cancellationToken);
        }
    }

    // A read spanning sleep cannot satisfy a resume request. Wait for it, then
    // share any newer normal read or start exactly one new read. No retries.
    public async Task RefreshAfterResumeAsync(CancellationToken cancellationToken = default)
    {
        Task? previous;
        lock (_gate)
        {
            previous = _refreshTask;
        }
        if (previous is not null) await previous.ConfigureAwait(false);
        if (!cancellationToken.IsCancellationRequested)
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task StartRefresh(CancellationToken cancellationToken)
    {
        // Install the completion before scheduling: even an immediate reader
        // cannot finish and then have its completed task stored as active.
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _refreshTask = completion.Task;
        _ = Task.Run(() => RefreshCore(cancellationToken, completion));
        return completion.Task;
    }

    private void RefreshCore(CancellationToken cancellationToken, TaskCompletionSource completion)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = _reader.ReadSnapshot(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var states = _stateMerger.Merge(snapshot);
            SetStatus(TelemetryStatusDocumentFactory.CreateStatus(snapshot, states), states);
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
                completion.TrySetResult();
            }
        }
    }

    private void SetStatus(
        TelemetryStatusDocument status,
        IReadOnlyList<MergedDeviceTelemetry> states)
    {
        lock (_gate)
        {
            _status = status;
            _states = states;
            _hasCompletedRead = true;
            PublishStatus();
        }
    }

    private void SetError(string error)
    {
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
            PublishStatus();
        }
    }

    private void PublishStatus()
    {
        var update = new TelemetryStatusUpdate(++_revision, _status);
        foreach (var subscription in _subscriptions)
        {
            subscription.Publish(update);
        }
    }

    private static TelemetryStatusDocument InitialStatus() => new(
        DateTimeOffset.UtcNow,
        0,
        Array.Empty<TelemetryDeviceStatus>(),
        Array.Empty<TelemetryFailureStatus>(),
        "not_checked");
}

public sealed record TelemetryStatusUpdate(long Revision, TelemetryStatusDocument Status);

public sealed class TelemetryStatusSubscription : IDisposable
{
    private readonly Action<TelemetryStatusSubscription> _remove;
    private readonly Channel<TelemetryStatusUpdate> _channel = Channel.CreateBounded<TelemetryStatusUpdate>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false
        });
    private int _disposed;

    internal TelemetryStatusSubscription(Action<TelemetryStatusSubscription> remove) => _remove = remove;

    public ChannelReader<TelemetryStatusUpdate> Updates => _channel.Reader;

    internal void Publish(TelemetryStatusUpdate update) => _channel.Writer.TryWrite(update);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _remove(this);
            _channel.Writer.TryComplete();
        }
    }
}
