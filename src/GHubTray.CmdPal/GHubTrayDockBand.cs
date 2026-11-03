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
        var items = new List<IListItem>
        {
            new ListItem(new TelemetryRefreshCommand(statusStore))
            {
                Title = "更新"
            }
        };

        if (status.Error == "not_checked" || statusStore.IsRefreshing && status.Devices.Count == 0)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "—",
                Subtitle = "状態を確認中"
            });
            return [.. items];
        }

        if (status.Error is not null && status.Devices.Count == 0)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "—",
                Subtitle = "状態を取得できない"
            });
            return [.. items];
        }

        if (status.Devices.Count == 0)
        {
            items.Add(new ListItem(new NoOpCommand())
            {
                Title = "—",
                Subtitle = status.ReceiverCount > 0
                    ? "デバイス応答なし"
                    : "デバイスなし"
            });
            return [.. items];
        }

        items.AddRange(status.Devices.Select(device =>
        {
            var voltage = device.Millivolts is int millivolts
                ? $" / {millivolts} mV"
                : string.Empty;
            var subtitle = $"{device.BatteryText} / {device.PowerText}{voltage}";
            if (device.IsStale)
            {
                subtitle += " / 最終取得値";
            }

            return (IListItem)new ListItem(new CopyTextCommand(
                $"{device.Name}: {device.BatteryText} / {device.PowerText} / {device.TechnicalText}")
            {
                Id = $"ghubtray.dock.device.{device.Id}",
                Name = $"{device.Name} の状態をコピー"
            })
            {
                Title = device.Name,
                Subtitle = subtitle
            };
        }));

        return [.. items];
    }
}
