using LogiBatteryDock.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace LogiBatteryDock.CmdPal;

internal sealed class LogiBatteryDockPage : TelemetryListPage
{
    private readonly TelemetryStatusStore _statusStore;
    private readonly TelemetryNotificationScope? _scope;

    public LogiBatteryDockPage()
        : this(new TelemetryStatusStore())
    {
    }

    internal LogiBatteryDockPage(TelemetryStatusStore statusStore, TelemetryNotificationScope? scope = null)
        : base(statusStore, scope)
    {
        _statusStore = statusStore;
        _scope = scope;
        Icon = new IconInfo("\uE7F4");
        Title = "LogiBatteryDock";
        Name = "状態を表示";
        Id = "logibatterydock.status";
    }

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
            CreateStatusItem(new ListItem(new TelemetryDeviceInfoPage(_statusStore, device, _scope))
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
