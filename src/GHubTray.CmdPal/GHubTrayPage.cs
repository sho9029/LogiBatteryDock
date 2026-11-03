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
        var items = new List<IListItem>
        {
            new ListItem(new TelemetryRefreshCommand(_statusStore))
            {
                Title = "今すぐ更新",
                Subtitle = "この拡張自身がHID++を再確認する"
            }
        };

        if (status.Error == "not_checked" || _statusStore.IsRefreshing && status.Devices.Count == 0)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "状態を確認中",
                Subtitle = "この拡張の初回取得を待って"
            });
            return [.. items];
        }

        if (status.Error is not null && status.Devices.Count == 0)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "状態を取得できない",
                Subtitle = "更新を実行して再試行して"
            });
            return [.. items];
        }

        items.AddRange(status.Devices.Select(device =>
            new ListItem(new CopyTextCommand(FormatCopyText(device))
            {
                Id = $"ghubtray.device.{device.Id}",
                Name = $"{device.Name} の状態をコピー"
            })
            {
                Title = device.Name,
                Subtitle = FormatSubtitle(device)
            }));

        if (status.Devices.Count == 0)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = status.ReceiverCount > 0 ? "デバイス応答なし" : "Logitechデバイスなし",
                Subtitle = status.Error ?? "受信機とデバイスを確認して"
            });
        }

        return [.. items];
    }

    private static string FormatSubtitle(TelemetryDeviceStatus device)
    {
        var stale = device.IsStale ? " / 最終取得値" : string.Empty;
        var voltage = device.Millivolts is int millivolts
            ? $" / {millivolts} mV"
            : string.Empty;
        return $"{device.BatteryText} / {device.PowerText}{voltage}{stale} / 取得 {device.ObservedAt.ToLocalTime():HH:mm:ss}";
    }

    private static string FormatCopyText(TelemetryDeviceStatus device) =>
        $"{device.Name}: {device.BatteryText} / {device.PowerText} / {device.TechnicalText}";
}
