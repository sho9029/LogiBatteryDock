using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GHubTray.Core;
using GHubTray.CmdPal;
using Microsoft.CommandPalette.Extensions;
using Shmuelie.WinRTServer;
using Shmuelie.WinRTServer.CsWinRT;
using Windows.Foundation;
using WinRT;

var mode = args[0];
var run = args[1];
using var done = new EventWaitHandle(false, EventResetMode.ManualReset, $@"Local\GHubTrayFlowSmoke-{run}-done");
using var entered = new EventWaitHandle(false, EventResetMode.ManualReset, $@"Local\GHubTrayFlowSmoke-{run}-entered");
using var release = new EventWaitHandle(false, EventResetMode.ManualReset, $@"Local\GHubTrayFlowSmoke-{run}-release");

if (mode == "server")
{
    var extension = new SmokeExtension(done);
    var server = new ComServer();
    server.RegisterClass<SmokeExtension, IExtension>(() => extension);
    server.Start();
    Console.WriteLine("server.ready");
    done.WaitOne(TimeSpan.FromSeconds(45));
    release.Set();
    server.Stop();
    extension.Dispose();
    server.UnsafeDispose();
    return;
}

var clsid = typeof(SmokeExtension).GUID;
var iid = typeof(IExtension).GUID;
Marshal.ThrowExceptionForHR(Native.CoCreateInstance(ref clsid, 0, 4, ref iid, out var pointer));
var remote = MarshalInterface<IExtension>.FromAbi(pointer);
Marshal.Release(pointer);
var provider = remote.GetProvider(ProviderType.Commands).As<ICommandProvider3>();
var dock = provider.GetDockBands()[0].Command.As<IListPage>();

if (mode is "blocked-host" or "disconnected-host")
{
    var calls = 0;
    TypedEventHandler<object, IItemsChangedEventArgs> handler = (_, _) =>
    {
        Console.WriteLine($"old-host.call={Interlocked.Increment(ref calls)}");
        entered.Set();
        if (mode == "blocked-host") release.WaitOne(TimeSpan.FromSeconds(40));
        throw new COMException("Simulated disconnected host", unchecked((int)0x80010108));
    };
    dock.ItemsChanged += handler;
    done.WaitOne(TimeSpan.FromSeconds(45));
    GC.KeepAlive(handler);
    return;
}

var received = new ConcurrentQueue<string>();
TypedEventHandler<object, IItemsChangedEventArgs> current = (_, _) =>
{
    var title = dock.GetItems()[0].Title;
    received.Enqueue(title);
    Console.WriteLine("host.pull=" + title);
};
dock.ItemsChanged += current;
try
{
    foreach (var expected in new[] { "51%", "50%" })
    {
        var refresh = dock.GetItems()[0].MoreCommands.OfType<ICommandContextItem>()
            .Single(item => item.Title == "更新").Command.As<IInvokableCommand>();
        var elapsed = Stopwatch.StartNew();
        refresh.Invoke(null!);
        Console.WriteLine($"manual.returned elapsedMs={elapsed.ElapsedMilliseconds} value={dock.GetItems()[0].Title}");
        var deadline = Stopwatch.StartNew();
        while (!received.Contains(expected) && deadline.Elapsed < TimeSpan.FromSeconds(5)) Thread.Sleep(10);
        if (!received.Contains(expected)) throw new InvalidOperationException("Missing remote notification: " + expected);
    }
    Console.WriteLine("PASS: latest snapshot and notifications delivered while old host was failed/blocked");
}
finally
{
    release.Set();
    dock.ItemsChanged -= current;
    remote.Dispose();
}

[Guid("F778E57B-212F-4E10-A709-264593A67A3E")]
public sealed class SmokeExtension : IExtension
{
    private readonly EventWaitHandle _done;
    private readonly GHubTrayCommandsProvider _provider;

    public SmokeExtension(EventWaitHandle done)
    {
        _done = done;
        var store = new TelemetryStatusStore(new SmokeReader());
        store.RefreshAsync().GetAwaiter().GetResult();
        _provider = new(store, new GHubTraySettingsStore(), TimeSpan.FromHours(1));
        ((GHubTrayDockBand)_provider.GetDockBands()[0]).Notifications.Trace = message => Console.WriteLine("dock." + message);
    }

    public object? GetProvider(ProviderType providerType) => _provider;
    public void Dispose() { _provider.Dispose(); _done.Set(); }
}

internal sealed class SmokeReader : ITelemetryReader
{
    private int _reads;
    public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default)
    {
        var read = Interlocked.Increment(ref _reads);
        Console.WriteLine($"reader.read={read}");
        return read == 1 ? new(1, []) : new(1,
            [new DeviceTelemetry("G703", 0xC539, 1, 0x1004, 53 - read, null, PowerState.Discharging, 0)]);
    }
}

internal static class Native
{
    [DllImport("ole32.dll")]
    internal static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint instance);
}
