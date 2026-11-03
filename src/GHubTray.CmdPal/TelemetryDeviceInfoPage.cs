using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Globalization;
using System.Text.Json.Nodes;
using Windows.Foundation;

namespace GHubTray.CmdPal;

internal sealed class TelemetryDeviceInfoPage : ContentPage, IContentPage, IDisposable
{
    private const string CardTemplate = """
        {
          "type": "AdaptiveCard",
          "body": [
            {
              "type": "Container",
              "items": [
                {
                  "type": "ColumnSet",
                  "columns": [
                    {
                      "type": "Column",
                      "width": "stretch",
                      "items": [
                        {
                          "type": "TextBlock",
                          "text": "残量",
                          "size": "small",
                          "isSubtle": true
                        },
                        {
                          "type": "TextBlock",
                          "text": "${batteryText}",
                          "size": "extraLarge",
                          "weight": "bolder"
                        }
                      ]
                    },
                    {
                      "type": "Column",
                      "width": "stretch",
                      "items": [
                        {
                          "type": "TextBlock",
                          "text": "電源",
                          "size": "small",
                          "isSubtle": true,
                          "horizontalAlignment": "right"
                        },
                        {
                          "type": "TextBlock",
                          "text": "${powerText}",
                          "size": "large",
                          "weight": "bolder",
                          "horizontalAlignment": "right"
                        }
                      ]
                    }
                  ]
                },
                {
                  "type": "TextBlock",
                  "text": "${modelName}",
                  "size": "medium",
                  "wrap": true,
                  "spacing": "medium"
                },
                {
                  "type": "FactSet",
                  "facts": [
                    {
                      "title": "状態",
                      "value": "${statusText}"
                    },
                    {
                      "title": "取得元",
                      "value": "${sourceText}"
                    },
                    {
                      "title": "最終更新",
                      "value": "${observedAt}"
                    }
                  ],
                  "spacing": "medium"
                }
              ]
            }
          ],
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "version": "1.5"
        }
        """;

    private readonly TelemetryStatusStore _statusStore;
    private readonly TelemetryDeviceStatus _initialDevice;
    private readonly TelemetryPageNotifications _notifications;
    internal TelemetryPageNotifications Notifications => _notifications;

    public new event TypedEventHandler<object, IItemsChangedEventArgs>? ItemsChanged
    {
        add { if (value is not null) _notifications.Add(value); }
        remove { if (value is not null) _notifications.Remove(value); }
    }

    internal TelemetryDeviceInfoPage(
        TelemetryStatusStore statusStore,
        TelemetryDeviceStatus device,
        TelemetryNotificationScope? scope = null)
    {
        _statusStore = statusStore;
        _notifications = new(statusStore, this, scope);
        _initialDevice = device;
        Icon = CmdPalDisplayFormatter.DeviceBatteryIcon(device);
        Title = CmdPalDisplayFormatter.DeviceTitle(device.Name);
        Id = CmdPalDisplayFormatter.DeviceCommandId("ghubtray.device.info", device);
        Commands =
        [
            new CommandContextItem(new TelemetryRefreshCommand(_statusStore))
            {
                Title = "更新",
                Subtitle = "この機器の残量を再確認"
            }
        ];
    }

    public override IContent[] GetContent()
    {
        _statusStore.EnsureInitialRefresh();
        var device = _statusStore.GetStatus().Devices
            .FirstOrDefault(current => current.Id == _initialDevice.Id)
            ?? _initialDevice with
            {
                IsStale = true,
                BatteryText = "現在値は未確認",
                PowerText = "現在は未確認"
            };

        // Published forms are immutable. Mutating an old form here would invoke
        // its host PropChanged subscribers on a pull/refresh command's stack.
        return [new FormContent
        {
            TemplateJson = CardTemplate,
            DataJson = CreateData(device).ToJsonString()
        }];
    }

    public void Dispose() => _notifications.Dispose();

    private static JsonObject CreateData(TelemetryDeviceStatus device) => new()
    {
        ["batteryText"] = device.BatteryText,
        ["powerText"] = device.PowerText,
        ["modelName"] = device.Name,
        ["statusText"] = CreateStatusText(device),
        ["sourceText"] = CreateSourceText(device),
        ["observedAt"] = device.ObservedAt.ToLocalTime()
            .ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.CurrentCulture)
    };

    private static string CreateStatusText(TelemetryDeviceStatus device)
    {
        if (device.IsStale)
        {
            return "最終取得値（現在は未確認）";
        }

        return device.IsEstimatedPercentage
            ? "接続中（電圧から残量を推定）"
            : "接続中";
    }

    private static string CreateSourceText(TelemetryDeviceStatus device) => device.Source switch
    {
        TelemetrySource.GHubAgent => "G HUB連携",
        TelemetrySource.NativeVoltageEstimate => "HID++（電圧から推定）",
        _ => "HID++"
    };
}
