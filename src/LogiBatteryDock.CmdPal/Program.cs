using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;
using System.Threading;

namespace LogiBatteryDock.CmdPal;

public static class Program
{
    [MTAThread]
    public static void Main(string[] args)
    {
        if (args.Length == 0 || args[0] != "-RegisterProcessAsComServer")
        {
            return;
        }

        var server = new ComServer();
        using var extensionDisposed = new ManualResetEvent(false);
        var extension = new LogiBatteryDockExtension(extensionDisposed);
        server.RegisterClass<LogiBatteryDockExtension, IExtension>(() => extension);
        server.Start();
        extensionDisposed.WaitOne();
        server.Stop();
        server.UnsafeDispose();
    }
}
