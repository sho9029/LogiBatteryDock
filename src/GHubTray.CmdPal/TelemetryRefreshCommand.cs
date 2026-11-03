using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GHubTray.CmdPal;

internal sealed class TelemetryRefreshCommand : InvokableCommand
{
    private readonly TelemetryStatusStore _statusStore;
    private readonly Action? _onRefreshCompleted;

    public TelemetryRefreshCommand(
        TelemetryStatusStore statusStore,
        Action? onRefreshCompleted = null)
    {
        _statusStore = statusStore;
        _onRefreshCompleted = onRefreshCompleted;
        Name = "更新";
        Id = "ghubtray.command.refresh";
    }

    public override ICommandResult Invoke()
    {
        // Keep the command invocation alive until the new snapshot exists.
        // The provider's background StatusChanged callback is not enough for
        // every CmdPal surface, especially a pinned Dock band. Repaint from
        // the command path as well so the item that was just refreshed cannot
        // remain on the previous percentage.
        try
        {
            _statusStore.RefreshAsync().GetAwaiter().GetResult();
            _onRefreshCompleted?.Invoke();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"CmdPal telemetry refresh failed: {exception.GetType().Name}");
        }

        return CommandResult.KeepOpen();
    }
}
