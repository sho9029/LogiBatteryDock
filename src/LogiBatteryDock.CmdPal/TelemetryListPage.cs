using LogiBatteryDock.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Foundation;

namespace LogiBatteryDock.CmdPal;

internal abstract class TelemetryListPage : ListPage, IListPage, IDisposable
{
    internal TelemetryPageNotifications Notifications { get; }

    protected TelemetryListPage(TelemetryStatusStore store, TelemetryNotificationScope? scope = null)
        => Notifications = new(store, this, scope);

    public new event TypedEventHandler<object, IItemsChangedEventArgs>? ItemsChanged
    {
        add { if (value is not null) Notifications.Add(value); }
        remove { if (value is not null) Notifications.Remove(value); }
    }

    internal void NotifyStatusChanged() => Notifications.Request();

    public void Dispose() => Notifications.Dispose();
}
