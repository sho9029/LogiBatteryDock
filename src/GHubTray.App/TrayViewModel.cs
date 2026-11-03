using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using GHubTray.Core;

namespace GHubTray.App;

public sealed class TrayViewModel : INotifyPropertyChanged
{
    private readonly LogitechDeviceReader _reader = new();
    private readonly DeviceStateMerger _stateMerger = new();
    private readonly object _refreshGate = new();
    private bool _refreshing;
    private Task? _activeRefresh;
    private string _updatedText = string.Empty;
    private string _toolTip = "GHubTray — 検索中";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DeviceCard> Devices { get; } =
    [
        new("検索中…", "—", "接続を確認中", string.Empty)
    ];
    public string UpdatedText { get => _updatedText; private set => Set(ref _updatedText, value); }
    public string ToolTip { get => _toolTip; private set => Set(ref _toolTip, value); }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (_refreshGate)
        {
            if (_refreshing)
            {
                return Task.CompletedTask;
            }

            _refreshing = true;
            _activeRefresh = RefreshCoreAsync(cancellationToken);
            return _activeRefresh;
        }
    }

    public async Task WaitForRefreshAsync()
    {
        Task? active;
        lock (_refreshGate)
        {
            active = _activeRefresh;
        }

        if (active is not null)
        {
            await active;
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await Task.Run(() => _reader.ReadSnapshot(cancellationToken));
            var states = _stateMerger.Merge(snapshot);
            var liveDevice = states.FirstOrDefault(state => !state.IsStale)?.Telemetry;
            if (states.Count > 0)
            {
                Devices.Clear();
                foreach (var state in states)
                {
                    Devices.Add(ToCard(state));
                }

                ToolTip = liveDevice is null
                    ? "GHubTray — デバイス応答なし"
                    : liveDevice.IsExactPercentage
                        ? $"{liveDevice.Name}: {liveDevice.Percent}%"
                        : $"{liveDevice.Name}: {ToPowerText(liveDevice.PowerState)}";
            }
            else
            {
                Devices.Clear();
                Devices.Add(snapshot.ReceiverCount > 0
                    ? new("Logitech受信機", "—", "デバイス応答なし", FailureSummary(snapshot))
                    : new("Logitechデバイスなし", "—", "受信機を確認できない", "HID++インターフェイスなし"));
                ToolTip = snapshot.ReceiverCount > 0
                    ? "GHubTray — 受信機あり / デバイス応答なし"
                    : "GHubTray — デバイスなし";
            }
            UpdatedText = $"確認 {snapshot.CheckedAt.ToLocalTime():HH:mm:ss}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            Devices.Clear();
            Devices.Add(new("読み取りに失敗", "—", "状態を取得できない", "次回更新または手動更新で再試行"));
            ToolTip = "GHubTray — 読み取り失敗";
            UpdatedText = $"更新失敗 {DateTime.Now:HH:mm:ss}";
        }
        finally
        {
            lock (_refreshGate)
            {
                _refreshing = false;
            }
        }
    }

    private static DeviceCard ToCard(MergedDeviceTelemetry state)
    {
        var device = state.Telemetry;
        var batteryValue = device.IsExactPercentage ? $"{device.Percent}%" : "残量は未提供";
        var powerText = ToPowerText(device.PowerState);
        var technicalText = device.Millivolts is int mv
            ? $"{mv} mV  •  HID++ 0x{device.BatteryFeature:X4}  •  flags 0x{device.RawStatus:X2}  •  取得 {device.ObservedAt.ToLocalTime():HH:mm:ss}"
            : $"HID++ 0x{device.BatteryFeature:X4}  •  status 0x{device.RawStatus:X2}  •  取得 {device.ObservedAt.ToLocalTime():HH:mm:ss}";

        if (state.IsStale)
        {
            var lastKnownValue = device.IsExactPercentage
                ? $"最終取得値 {batteryValue}"
                : "最終取得時も残量は未提供";
            batteryValue = "現在値は未確認";
            powerText = "応答なし（スリープまたは圏外）";
            technicalText = $"{lastKnownValue} • {technicalText} • 最終取得値";
        }

        return new(device.Name, batteryValue, powerText, technicalText)
        {
            IsStale = state.IsStale,
            ReceiverProductId = device.ReceiverProductId,
            DeviceIndex = device.DeviceIndex
        };
    }

    private static string FailureSummary(LogitechReadSnapshot snapshot) =>
        snapshot.Failures.Count == 0
            ? "スリープまたは圏外"
            : string.Join(" / ", snapshot.Failures.Select(f => f.Kind switch
            {
                DeviceReadFailureKind.OpenFailed => "HIDを開けない",
                DeviceReadFailureKind.EnumerationFailed => "HID列挙失敗",
                DeviceReadFailureKind.NoResponsiveDevice => "デバイス無応答",
                DeviceReadFailureKind.BatteryFeatureUnsupported => "電池機能なし",
                DeviceReadFailureKind.BatteryReadFailed => "電池値取得失敗",
                DeviceReadFailureKind.InvalidResponse => "不正応答",
                _ => "取得失敗"
            }).Distinct());

    private static string ToPowerText(PowerState state) => state switch
    {
        PowerState.Charging => "充電中",
        PowerState.Full => "満充電",
        PowerState.ExternalPower => "外部電源あり",
        PowerState.Discharging => "接続中",
        _ => "状態不明"
    };

    private void Set(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

}

public sealed class DeviceCard
{
    public DeviceCard() { }

    public DeviceCard(string name, string batteryValue, string powerText, string technicalText)
    {
        Name = name;
        BatteryValue = batteryValue;
        PowerText = powerText;
        TechnicalText = technicalText;
    }

    public string Name { get; set; } = string.Empty;
    public string BatteryValue { get; set; } = string.Empty;
    public string PowerText { get; set; } = string.Empty;
    public string TechnicalText { get; set; } = string.Empty;
    public bool IsStale { get; set; }
    public int ReceiverProductId { get; set; }
    public byte DeviceIndex { get; set; }
}
