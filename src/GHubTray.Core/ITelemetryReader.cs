namespace GHubTray.Core;

/// <summary>
/// Device-reading boundary shared by the tray and Command Palette frontends.
/// </summary>
public interface ITelemetryReader
{
    LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default);
}
