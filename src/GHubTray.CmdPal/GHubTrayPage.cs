using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GHubTray.CmdPal;

internal sealed class GHubTrayPage : ListPage
{
    private readonly TelemetryStatusStore _statusStore;

    public GHubTrayPage()
        : this(new TelemetryStatusStore())
    {
    }

    internal GHubTrayPage(TelemetryStatusStore statusStore)
    {
        _statusStore = statusStore;
        Icon = new IconInfo("\uE7F4");
        Title = "GHubTray";
        Name = "状態を表示";
        Id = "ghubtray.status";
    }

    internal void NotifyStatusChanged() => RaiseItemsChanged();

    public override IListItem[] GetItems()
    {
        _statusStore.EnsureInitialRefresh();
        var status = _statusStore.GetStatus();
        var items = new List<IListItem>();

        if (status.Error == "not_checked")
        {
            items.Add(CreateStatusItem(new ListItem(new NoOpCommand())
            {
                Title = "状態を確認中",
                Subtitle = "HID++を確認中"
            }));
            return [.. items];
        }

        if (status.Error is not null && status.Devices.Count == 0)
        {
            items.Add(CreateStatusItem(new ListItem(new NoOpCommand())
            {
                Title = "状態を取得できない",
                Subtitle = "更新を実行して再試行して"
            }));
            return [.. items];
        }

        items.AddRange(status.Devices.Select(device =>
            CreateStatusItem(new ListItem(new NoOpCommand
            {
                Id = CmdPalDisplayFormatter.DeviceCommandId("ghubtray.device", device),
                Name = $"{device.Name} の状態"
            })
            {
                Title = CmdPalDisplayFormatter.DeviceTitle(device.Name),
                Subtitle = CmdPalDisplayFormatter.DeviceSubtitle(device)
            })));

        if (status.Devices.Count == 0)
        {
            items.Add(CreateStatusItem(new ListItem(new NoOpCommand())
            {
                Title = status.ReceiverCount > 0 ? "デバイス応答なし" : "Logitechデバイスなし",
                Subtitle = status.Error ?? "受信機とデバイスを確認して"
            }));
        }

        return [.. items];
    }

    private ListItem CreateStatusItem(ListItem item)
    {
        item.MoreCommands =
        [
            new CommandContextItem(new TelemetryRefreshCommand(_statusStore))
            {
                Title = "更新",
                Subtitle = "各機器の残量を再確認"
            }
        ];
        return item;
    }
}
