using Microsoft.CommandPalette.Extensions;
using System.Runtime.InteropServices;
using System.Threading;

namespace LogiBatteryDock.CmdPal;

[Guid("FAEC353D-F245-459C-A257-0749AAD511BD")]
public sealed class LogiBatteryDockExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _disposed;
    private readonly LogiBatteryDockCommandsProvider _provider = new();

    public LogiBatteryDockExtension(ManualResetEvent disposed) => _disposed = disposed;

    public object? GetProvider(ProviderType providerType) => providerType switch
    {
        ProviderType.Commands => _provider,
        _ => null
    };

    public void Dispose()
    {
        _provider.Dispose();
        _disposed.Set();
    }
}
