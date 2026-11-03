using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;

namespace GHubTray.CmdPal;

public sealed class GHubTrayCommandsProvider : CommandProvider, IDisposable
{
    private readonly TelemetryStatusStore _statusStore;
    private readonly GHubTrayPage _page;
    private readonly GHubTrayDockBand _dockBand;
    private readonly ICommandItem[] _commands;
    private readonly PeriodicTimer _refreshTimer;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _refreshLoop;
    private readonly WindowsResumeNotification? _resumeNotification;
    private readonly TelemetryNotificationScope _notifications = new();
    private int _disposed;

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
        : this(statusStore, settingsStore, null)
    {
    }

    internal GHubTrayCommandsProvider(
        TelemetryStatusStore statusStore,
        GHubTraySettingsStore settingsStore,
        TimeSpan? refreshInterval)
    {
        _statusStore = statusStore;
        DisplayName = "GHubTray";
        Icon = new IconInfo("\uE7F4");

        _page = new GHubTrayPage(_statusStore, _notifications);
        _dockBand = new GHubTrayDockBand(_statusStore, settingsStore, _notifications);
        _commands = [new CommandItem(_page) { Title = DisplayName }];

        var interval = refreshInterval ?? settingsStore.Load().RefreshInterval;
        _refreshTimer = new PeriodicTimer(interval);
        _refreshLoop = RunRefreshLoopAsync(_lifetime.Token);
        _resumeNotification = WindowsResumeNotification.TryCreate(() => _ = RefreshAfterResumeAsync());
    }

    internal Task RefreshAfterResumeAsync() => _statusStore.RefreshAfterResumeAsync(_lifetime.Token);

    public override ICommandItem[] TopLevelCommands() => _commands;

    public override ICommandItem[] GetDockBands() => [_dockBand];

    public override void InitializeWithHost(IExtensionHost host)
    {
        ResetHostSubscriptions();
        base.InitializeWithHost(host);
    }

    internal void ResetHostSubscriptions()
    {
        _notifications.Reset();
    }

    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        _resumeNotification?.Dispose();
        _refreshTimer.Dispose();
        _page.Dispose();
        _dockBand.Dispose();
        _notifications.Dispose();
        _lifetime.Dispose();
        base.Dispose();
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _refreshTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await _statusStore.RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine(
                $"CmdPal automatic telemetry refresh failed: {exception.GetType().Name}");
        }
    }

}
