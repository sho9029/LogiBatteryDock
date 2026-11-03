using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GHubTray.CmdPal;

internal sealed class GHubTrayDockBand : WrappedDockItem
{
    private const string BandId = "ghubtray.devices";
    private readonly TelemetryStatusStore _statusStore;

    public GHubTrayDockBand()
        : this(new TelemetryStatusStore())
    {
    }

    internal GHubTrayDockBand(TelemetryStatusStore statusStore)
        : base(CreateItems(statusStore), BandId, "GHubTray")
    {
        _statusStore = statusStore;
        Icon = new IconInfo("\uE7F4");
    }

    internal void RefreshItems() => Items = CreateItems(_statusStore);

    private static IListItem[] CreateItems(TelemetryStatusStore statusStore)
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
            }));
            return [.. items];
        }

        if (status.Error is not null && status.Devices.Count == 0)
        {
            items.Add(CreateStatusItem(statusStore, new ListItem(new NoOpCommand())
            {
                Title = "—",
                Subtitle = "状態を取得できない"
            }));
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
            }));
            return [.. items];
        }

        items.AddRange(status.Devices.Select(device =>
        {
            return (IListItem)CreateStatusItem(statusStore, new ListItem(new NoOpCommand
            {
                Id = CmdPalDisplayFormatter.DeviceCommandId("ghubtray.dock.device", device),
                Name = $"{device.Name} の状態"
            })
            {
                Title = CmdPalDisplayFormatter.DeviceTitle(device.Name),
                Subtitle = CmdPalDisplayFormatter.DeviceSubtitle(device)
            });
        }));

        return [.. items];
    }

    private static ListItem CreateStatusItem(TelemetryStatusStore statusStore, ListItem item)
    {
        item.MoreCommands =
        [
            new CommandContextItem(new TelemetryRefreshCommand(statusStore))
            {
                Title = "更新",
                Subtitle = "各機器の残量を再確認"
            }
        ];
        return item;
    }
}
