using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using GHubTray.Core;

namespace GHubTray.App;

public sealed class TrayViewModel : INotifyPropertyChanged
{
    private readonly TelemetryStatusStore _statusStore;
    private readonly object _refreshGate = new();
    private bool _refreshing;
    private Task? _activeRefresh;
    private string _updatedText = string.Empty;
    private string _toolTip = "GHubTray — 検索中";

    public TrayViewModel()
        : this(new TelemetryStatusStore())
    {
    }

    internal TrayViewModel(TelemetryStatusStore statusStore) =>
        _statusStore = statusStore;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<DeviceCard> Devices { get; } =
    [
        new("検索中…", "—", "接続を確認中", string.Empty)
    ];
    public string UpdatedText { get => _updatedText; private set => Set(ref _updatedText, value); }
    public string ToolTip { get => _toolTip; private set => Set(ref _toolTip, value); }

    public TelemetryStatusDocument GetStatus() => _statusStore.GetStatus();

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
            await _statusStore.RefreshAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var status = _statusStore.GetStatus();
            var states = _statusStore.GetMergedStates();
            if (status.Error == "read_failed")
            {
                Devices.Clear();
                Devices.Add(new("読み取りに失敗", "—", "状態を取得できない", "次回更新または手動更新で再試行"));
                ToolTip = "GHubTray — 読み取り失敗";
                UpdatedText = $"更新失敗 {DateTime.Now:HH:mm:ss}";
                return;
            }

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
                        : $"{liveDevice.Name}: {TelemetryDisplayFormatter.ToPowerText(liveDevice.PowerState)}";
            }
            else
            {
                Devices.Clear();
                Devices.Add(status.ReceiverCount > 0
                    ? new("Logitech受信機", "—", "デバイス応答なし", FailureSummary(status))
                    : new("Logitechデバイスなし", "—", "受信機を確認できない", "HID++インターフェイスなし"));
                ToolTip = status.ReceiverCount > 0
                    ? "GHubTray — 受信機あり / デバイス応答なし"
                    : "GHubTray — デバイスなし";
            }
            UpdatedText = $"確認 {status.CheckedAt.ToLocalTime():HH:mm:ss}";
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
        var display = TelemetryDisplayFormatter.Format(device, state.IsStale);

        return new(device.Name, display.BatteryText, display.PowerText, display.TechnicalText)
        {
            IsStale = state.IsStale,
            ReceiverKey = device.ReceiverKey,
            ReceiverProductId = device.ReceiverProductId,
            DeviceIndex = device.DeviceIndex
        };
    }

    private static string FailureSummary(TelemetryStatusDocument status) =>
        status.Failures.Count == 0
            ? "スリープまたは圏外"
            : string.Join(" / ", status.Failures.Select(f => f.Kind switch
            {
                DeviceReadFailureKind.OpenFailed => "HIDを開けない",
                DeviceReadFailureKind.EnumerationFailed => "HID列挙失敗",
                DeviceReadFailureKind.NoResponsiveDevice => "デバイス無応答",
                DeviceReadFailureKind.BatteryFeatureUnsupported => "電池機能なし",
                DeviceReadFailureKind.BatteryReadFailed => "電池値取得失敗",
                DeviceReadFailureKind.InvalidResponse => "不正応答",
                DeviceReadFailureKind.ScanTimedOut => "取得時間切れ",
                _ => "取得失敗"
            }).Distinct());

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
    public string ReceiverKey { get; set; } = string.Empty;
    public int ReceiverProductId { get; set; }
    public byte DeviceIndex { get; set; }
}
