using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GHubTray.CmdPal;

internal sealed class GHubTrayDockBand : WrappedDockItem
{
    private const string BandId = "ghubtray.devices";
    private readonly TelemetryStatusStore _statusStore;
    private readonly GHubTraySettingsStore _settingsStore;

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
        GHubTraySettingsStore settingsStore)
        : base(CreateItems(statusStore, settingsStore), BandId, "GHubTray")
    {
        _statusStore = statusStore;
        _settingsStore = settingsStore;
        Icon = new IconInfo("\uE7F4");
        RefreshItems();
    }

    internal void RefreshItems()
    {
        var refreshedItems = CreateItems(_statusStore, _settingsStore, RefreshItems);
        if (TryUpdateExistingItems(refreshedItems))
        {
            return;
        }

        Items = refreshedItems;
    }

    private bool TryUpdateExistingItems(IListItem[] refreshedItems)
    {
        if (Items.Length != refreshedItems.Length)
        {
            return false;
        }

        for (var index = 0; index < Items.Length; index++)
        {
            if (Items[index] is not ListItem current
                || refreshedItems[index] is not ListItem refreshed)
            {
                return false;
            }

            current.Command = refreshed.Command;
            current.Title = refreshed.Title;
            current.Subtitle = refreshed.Subtitle;
            current.Icon = refreshed.Icon;
            current.MoreCommands = refreshed.MoreCommands;
        }

        return true;
    }

    private static IListItem[] CreateItems(
        TelemetryStatusStore statusStore,
        GHubTraySettingsStore settingsStore,
        Action? onRefreshCompleted = null)
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
            new ListItem(new TelemetryDeviceInfoPage(statusStore, selectedDevice))
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
        commands.Add(new CommandContextItem(new TelemetryRefreshCommand(
            statusStore,
            onRefreshCompleted))
        {
            Title = "更新",
            Subtitle = "各機器の残量を再確認"
        });
        item.MoreCommands = [.. commands];
        return item;
    }
}
