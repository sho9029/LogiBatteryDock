using Microsoft.CommandPalette.Extensions;
using System.Runtime.InteropServices;
using System.Threading;

namespace GHubTray.CmdPal;

[Guid("2B9797B8-2F2E-4C5F-8C63-C0C55C2B9E12")]
public sealed class GHubTrayExtension : IExtension, IDisposable
{
    private readonly ManualResetEvent _disposed;
    private readonly GHubTrayCommandsProvider _provider = new();

    public GHubTrayExtension(ManualResetEvent disposed) => _disposed = disposed;

    public object? GetProvider(ProviderType providerType) => providerType switch
    {
        ProviderType.Commands => _provider,
        _ => null
    };

    public void Dispose() => _disposed.Set();
}
