using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Foundation;

namespace GHubTray.CmdPal;

/// <summary>
/// A page's pull model is authoritative. Push notifications are independent
/// invalidations: one running call and one pending latest revision per recipient.
/// No remote callback runs on the acquisition, publication, or command path.
/// </summary>
internal sealed class TelemetryPageNotifications : IDisposable
{
    private readonly object _gate = new();
    private readonly TelemetryStatusStore _store;
    private readonly object _sender;
    private readonly List<Recipient> _recipients = [];
    private TelemetryStatusSubscription? _subscription;
    private long _revision;
    private bool _disposed;
    private int _activeCalls;
    private long _nextRecipientId;

    internal TelemetryPageNotifications(
        TelemetryStatusStore store, object sender, TelemetryNotificationScope? scope = null)
    {
        _store = store;
        _sender = sender;
        scope?.Register(this);
    }

    internal Action<string>? Trace { get; set; }
    internal int ActiveCalls { get { lock (_gate) return _activeCalls; } }
    internal int PendingCalls { get { lock (_gate) return _recipients.Count(r => r.Pending); } }
    internal int SubscriberCount { get { lock (_gate) return _recipients.Count; } }

    internal void Add(TypedEventHandler<object, IItemsChangedEventArgs> handler)
    {
        lock (_gate)
        {
            if (_disposed) return;
            var recipient = new Recipient(++_nextRecipientId, handler);
            _recipients.Add(recipient);
            if (_subscription is null)
            {
                _subscription = _store.Subscribe();
                var subscription = _subscription;
                _ = Task.Run(() => PumpAsync(subscription));
            }
            // Reconnecting hosts get an invalidation even if no new device read
            // happens. GetItems/GetContent always pull the current saved snapshot.
            else Request(recipient);
        }
    }

    internal void Remove(TypedEventHandler<object, IItemsChangedEventArgs> handler)
    {
        lock (_gate)
        {
            var recipient = _recipients.LastOrDefault(r => r.Handler == handler);
            if (recipient is not null) Retire(recipient);
        }
    }

    internal void Request()
    {
        lock (_gate)
        {
            foreach (var recipient in _recipients) Request(recipient);
        }
    }

    internal void Reset()
    {
        lock (_gate)
        {
            foreach (var recipient in _recipients) recipient.Retired = true;
            _recipients.Clear();
            StopSubscription();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Reset();
        }
    }

    private async Task PumpAsync(TelemetryStatusSubscription subscription)
    {
        await foreach (var update in subscription.Updates.ReadAllAsync().ConfigureAwait(false))
        {
            lock (_gate)
            {
                if (_subscription != subscription) return;
                _revision = update.Revision;
                foreach (var recipient in _recipients) Request(recipient);
            }
        }
    }

    private void Request(Recipient recipient)
    {
        if (_disposed || recipient.Retired) return;
        recipient.Pending = true;
        if (recipient.Running) return;
        recipient.Running = true;
        _ = Task.Run(() => Deliver(recipient));
    }

    private void Deliver(Recipient recipient)
    {
        while (true)
        {
            long revision;
            lock (_gate)
            {
                if (recipient.Retired || !recipient.Pending)
                {
                    recipient.Running = false;
                    return;
                }
                recipient.Pending = false;
                revision = _revision;
                _activeCalls++;
            }

            try
            {
                Log($"notify.begin recipient={recipient.Id} revision={revision}");
                recipient.Handler(_sender, new ItemsChangedEventArgs(-1));
                Log($"notify.end recipient={recipient.Id} revision={revision}");
            }
            catch (Exception exception)
            {
                Log($"notify.failed recipient={recipient.Id} revision={revision} hresult=0x{exception.HResult:X8}");
                if (exception.HResult is unchecked((int)0x80010108) or unchecked((int)0x800706BA))
                {
                    lock (_gate) Retire(recipient);
                }
            }
            finally
            {
                lock (_gate) _activeCalls--;
            }
        }
    }

    private void Retire(Recipient recipient)
    {
        recipient.Retired = true;
        recipient.Pending = false;
        _recipients.Remove(recipient);
        if (_recipients.Count == 0) StopSubscription();
    }

    private void StopSubscription()
    {
        _subscription?.Dispose();
        _subscription = null;
    }

    private void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        try { Trace?.Invoke(message); } catch { /* Diagnostics cannot break delivery. */ }
    }

    private sealed class Recipient(long id, TypedEventHandler<object, IItemsChangedEventArgs> handler)
    {
        internal readonly long Id = id;
        internal readonly TypedEventHandler<object, IItemsChangedEventArgs> Handler = handler;
        internal bool Running;
        internal bool Pending;
        internal bool Retired;
    }
}

// The provider survives host reconnects. All pages it created, including open
// device detail pages, must discard the previous host's subscriptions together.
internal sealed class TelemetryNotificationScope : IDisposable
{
    private readonly object _gate = new();
    private readonly List<WeakReference<TelemetryPageNotifications>> _pages = [];
    private bool _disposed;

    internal void Register(TelemetryPageNotifications page)
    {
        lock (_gate)
        {
            if (_disposed) { page.Dispose(); return; }
            _pages.RemoveAll(reference => !reference.TryGetTarget(out _));
            _pages.Add(new(page));
        }
    }

    internal void Reset()
    {
        lock (_gate)
        {
            foreach (var reference in _pages)
                if (reference.TryGetTarget(out var page)) page.Reset();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var reference in _pages)
                if (reference.TryGetTarget(out var page)) page.Dispose();
            _pages.Clear();
        }
    }
}
