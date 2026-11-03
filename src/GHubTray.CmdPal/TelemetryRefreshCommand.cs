using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GHubTray.CmdPal;

internal sealed class TelemetryRefreshCommand : InvokableCommand
{
    private readonly TelemetryStatusStore _statusStore;

    public TelemetryRefreshCommand(
        TelemetryStatusStore statusStore)
    {
        _statusStore = statusStore;
        Name = "更新";
        Id = "ghubtray.command.refresh";
    }

    public override ICommandResult Invoke()
    {
        // Wait only for acquisition/publication. The store subscriptions are the
        // single update path for every surface, including this command's caller.
        try
        {
            _statusStore.RefreshAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"CmdPal telemetry refresh failed: {exception.GetType().Name}");
        }

        return CommandResult.KeepOpen();
    }
}
