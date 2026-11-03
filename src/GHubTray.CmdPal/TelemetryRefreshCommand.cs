using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GHubTray.CmdPal;

internal sealed class TelemetryRefreshCommand : InvokableCommand
{
    private readonly TelemetryStatusStore _statusStore;

    public TelemetryRefreshCommand(TelemetryStatusStore statusStore)
    {
        _statusStore = statusStore;
        Name = "更新";
        Id = "ghubtray.command.refresh";
    }

    public override ICommandResult Invoke()
    {
        _ = _statusStore.RefreshAsync();
        return CommandResult.KeepOpen();
    }
}
