using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GHubTray.CmdPal;

internal sealed class DockDeviceSelectionCommand : InvokableCommand
{
    private readonly GHubTraySettingsStore _settingsStore;
    private readonly string _deviceId;
    private readonly Action _onSelected;

    internal DockDeviceSelectionCommand(
        GHubTraySettingsStore settingsStore,
        TelemetryDeviceStatus device,
        Action onSelected)
    {
        _settingsStore = settingsStore;
        _deviceId = device.Id;
        _onSelected = onSelected;
        Id = CmdPalDisplayFormatter.DeviceCommandId("ghubtray.dock.select", device);
        Name = CmdPalDisplayFormatter.DeviceTitle(device.Name);
    }

    public override ICommandResult Invoke()
    {
        try
        {
            var settings = _settingsStore.Load();
            _settingsStore.Save(settings with { DockDeviceId = _deviceId });
            _onSelected();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Dock device selection failed: {exception.GetType().Name}");
        }

        return CommandResult.KeepOpen();
    }
}
