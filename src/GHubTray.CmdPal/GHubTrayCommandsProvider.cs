using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GHubTray.CmdPal;

public sealed class GHubTrayCommandsProvider : CommandProvider
{
    private readonly TelemetryStatusStore _statusStore;
    private readonly GHubTrayPage _page;
    private readonly GHubTrayDockBand _dockBand;
    private readonly ICommandItem[] _commands;

    public GHubTrayCommandsProvider()
        : this(new TelemetryStatusStore())
    {
    }

    internal GHubTrayCommandsProvider(TelemetryStatusStore statusStore)
        : this(statusStore, new GHubTraySettingsStore())
    {
    }

    internal GHubTrayCommandsProvider(
        TelemetryStatusStore statusStore,
        GHubTraySettingsStore settingsStore)
    {
        _statusStore = statusStore;
        DisplayName = "GHubTray";
        Icon = new IconInfo("\uE7F4");

        _page = new GHubTrayPage(_statusStore);
        _dockBand = new GHubTrayDockBand(_statusStore, settingsStore);
        _statusStore.StatusChanged += StatusStoreOnStatusChanged;
        _commands = [new CommandItem(_page) { Title = DisplayName }];
    }

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[] GetDockBands() => [_dockBand];

    private void StatusStoreOnStatusChanged(object? sender, EventArgs e)
    {
        _page.NotifyStatusChanged();
        _dockBand.RefreshItems();
        RaiseItemsChanged();
    }
}
