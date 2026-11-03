using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GHubTray.CmdPal;

internal sealed class GHubTrayDockBand : CommandItem, IDisposable
{
    private const string BandId = "ghubtray.devices";
    private readonly TelemetryStatusStore _statusStore;
    private readonly GHubTraySettingsStore _settingsStore;
    private readonly DockListPage _page;
    private readonly TelemetryNotificationScope? _scope;

    internal IListItem[] Items => _page.GetItems();
    internal TelemetryPageNotifications Notifications => _page.Notifications;

    public GHubTrayDockBand()
        : this(new TelemetryStatusStore(), new GHubTraySettingsStore())
    {
    }

    internal GHubTrayDockBand(TelemetryStatusStore statusStore)
        : this(statusStore, new GHubTraySettingsStore())
    {
    }

    internal GHubTrayDockBand(
        TelemetryStatusStore statusStore,
        GHubTraySettingsStore settingsStore,
        TelemetryNotificationScope? scope = null)
    {
        _statusStore = statusStore;
        _settingsStore = settingsStore;
        _scope = scope;
        _page = new DockListPage(statusStore, () => CreateItems(_statusStore, _settingsStore, RefreshItems, _scope), scope);
        Command = _page;
        Title = "GHubTray";
        Icon = new IconInfo("\uE7F4");
        RefreshItems();
        _statusStore.EnsureInitialRefresh();
    }

    internal void RefreshItems()
    {
        _page.NotifyStatusChanged();
    }

    public void Dispose() => _page.Dispose();

    private static IListItem[] CreateItems(
        TelemetryStatusStore statusStore,
        GHubTraySettingsStore settingsStore,
        Action? onRefreshCompleted = null,
        TelemetryNotificationScope? scope = null)
    {
        statusStore.EnsureInitialRefresh();
        var status = statusStore.GetStatus();
        var items = new List<IListItem>();

        if (status.Error == "not_checked")
        {
            items.Add(CreateStatusItem(statusStore, new ListItem(new NoOpCommand())
            {
                Title = "—",
                Subtitle = "状態を確認中"
            }, settingsStore, status.Devices, null, onRefreshCompleted));
            return [.. items];
        }

        if (status.Error is not null && status.Devices.Count == 0)
        {
            items.Add(CreateStatusItem(statusStore, new ListItem(new NoOpCommand())
            {
                Title = "—",
                Subtitle = "状態を取得できない"
            }, settingsStore, status.Devices, null, onRefreshCompleted));
            return [.. items];
        }

        if (status.Devices.Count == 0)
        {
            items.Add(CreateStatusItem(statusStore, new ListItem(new NoOpCommand())
            {
                Title = "—",
                Subtitle = status.ReceiverCount > 0
                    ? "デバイス応答なし"
                    : "デバイスなし"
            }, settingsStore, status.Devices, null, onRefreshCompleted));
            return [.. items];
        }

        var selectedDeviceId = settingsStore.Load().DockDeviceId;
        var selectedDevice = status.Devices.FirstOrDefault(
            device => device.Id == selectedDeviceId)
            ?? status.Devices[0];
        items.Add(CreateStatusItem(
            statusStore,
            new ListItem(new TelemetryDeviceInfoPage(statusStore, selectedDevice, scope))
            {
                Title = CmdPalDisplayFormatter.DeviceDockTitle(selectedDevice),
                Subtitle = CmdPalDisplayFormatter.DeviceDockSubtitle(selectedDevice),
                Icon = CmdPalDisplayFormatter.DeviceBatteryIcon(selectedDevice)
            },
            settingsStore,
            status.Devices,
            selectedDevice,
            onRefreshCompleted));

        return [.. items];
    }

    private static ListItem CreateStatusItem(
        TelemetryStatusStore statusStore,
        ListItem item,
        GHubTraySettingsStore settingsStore,
        IReadOnlyList<TelemetryDeviceStatus> devices,
        TelemetryDeviceStatus? selectedDevice,
        Action? onRefreshCompleted)
    {
        item.Icon ??= CmdPalDisplayFormatter.UnavailableBatteryIcon;
        var selectionCallback = onRefreshCompleted ?? (() => { });
        var commands = devices
            .Select(device => new CommandContextItem(new DockDeviceSelectionCommand(
                settingsStore,
                device,
                selectionCallback))
            {
                Title = CmdPalDisplayFormatter.DeviceTitle(device.Name),
                Subtitle = device.Id == selectedDevice?.Id
                    ? "現在の常駐端末"
                    : "Dockに常駐表示"
            })
            .Cast<ICommandContextItem>()
            .ToList();
        commands.Add(new CommandContextItem(new TelemetryRefreshCommand(statusStore))
        {
            Title = "更新",
            Subtitle = "各機器の残量を再確認"
        });
        item.MoreCommands = [.. commands];
        return item;
    }

    private sealed class DockListPage : TelemetryListPage
    {
        private readonly Func<IListItem[]> _getItems;

        internal DockListPage(TelemetryStatusStore store, Func<IListItem[]> getItems, TelemetryNotificationScope? scope)
            : base(store, scope)
        {
            _getItems = getItems;
            Id = BandId;
            Name = "GHubTray";
        }

        public override IListItem[] GetItems() => _getItems();
    }
}
