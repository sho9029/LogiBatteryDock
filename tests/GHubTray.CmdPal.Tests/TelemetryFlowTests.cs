using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Foundation;
using Xunit;
using Xunit.Abstractions;

namespace GHubTray.CmdPal.Tests;

public sealed class TelemetryFlowTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ResumeUpdatesPageAndDockWithoutTrayProcess()
    {
        var reader = new MutableReader { Missing = true };
        var store = new TelemetryStatusStore(reader);
        await store.RefreshAsync();
        using var provider = new GHubTrayCommandsProvider(
            store, new GHubTraySettingsStore(), TimeSpan.FromHours(1));
        var page = Assert.IsType<GHubTrayPage>(provider.TopLevelCommands()[0].Command);
        var dock = Assert.IsType<GHubTrayDockBand>(provider.GetDockBands()[0]);
        var pageUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dockUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.ItemsChanged += (_, _) =>
        {
            if (page.GetItems()[0].Subtitle.Contains("46%")) pageUpdated.TrySetResult();
        };
        Assert.IsAssignableFrom<IListPage>(dock.Command).ItemsChanged += (_, _) =>
        {
            if (dock.Items[0].Title == "46%") dockUpdated.TrySetResult();
        };
        reader.Missing = false;
        reader.Percent = 46;
        await provider.RefreshAfterResumeAsync();
        await Task.WhenAll(pageUpdated.Task, dockUpdated.Task).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, reader.ReadCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BlockedHostCannotStopReadsOtherSurfacesOrManualRefresh(bool blockDock)
    {
        var trace = new ConcurrentQueue<object>();
        void Record(string stage, int? percent = null) => trace.Enqueue(new
        {
            stage, percent, thread = Environment.CurrentManagedThreadId
        });
        var reader = new MutableReader(() => Record("reader.read"));
        var store = new TelemetryStatusStore(reader);
        await store.RefreshAsync();
        using var provider = new GHubTrayCommandsProvider(
            store, new GHubTraySettingsStore(), TimeSpan.FromHours(1));
        var main = Assert.IsType<GHubTrayPage>(provider.TopLevelCommands()[0].Command);
        var dock = Assert.IsType<GHubTrayDockBand>(provider.GetDockBands()[0]);
        var dockPage = Assert.IsAssignableFrom<IListPage>(dock.Command);
        IListPage blocked = blockDock ? dockPage : main;
        IListPage other = blockDock ? main : dockPage;
        var notifications = blockDock ? dock.Notifications : main.Notifications;
        notifications.Trace = message => Record(message);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var calls = 0;
        var hostValue = -1;
        var otherValue = -1;
        TypedEventHandler<object, IItemsChangedEventArgs> stalled = (_, _) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                Record("old-host.blocked");
                entered.TrySetResult();
                release.Wait();
            }
            if (call > 1)
            {
                Record("old-host.caught-up", CurrentPercent(blocked));
                resumed.TrySetResult();
            }
        };
        blocked.ItemsChanged += stalled;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // A second recipient on the SAME page must also remain independent.
            blocked.ItemsChanged += (_, _) =>
            {
                hostValue = CurrentPercent(blocked);
                Record("healthy-host.pull", hostValue);
            };
            other.ItemsChanged += (_, _) => otherValue = CurrentPercent(other);
            using var committed = store.Subscribe();
            for (var i = 1; i <= 100; i++)
            {
                reader.Percent = i;
                await store.RefreshAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(store.IsRefreshing);
                var update = await committed.Updates.ReadAsync();
                Assert.Equal(i, update.Status.Devices[0].Percent);
                Record($"store.commit revision={update.Revision}", i);
            }
            await Eventually(() => hostValue == 100 && otherValue == 100);
            Assert.Equal(1, calls);
            Assert.InRange(notifications.ActiveCalls, 1, 2);
            Assert.InRange(notifications.PendingCalls, 0, 2);
            Record("bounded-after-100-updates", reader.Percent);

            reader.Percent = 42;
            var refresh = new TelemetryRefreshCommand(store);
            await Task.Run(() => refresh.Invoke()).WaitAsync(TimeSpan.FromSeconds(5));
            Record("manual-refresh.returned", store.GetStatus().Devices[0].Percent);
            await Eventually(() => hostValue == 42 && otherValue == 42);
            Assert.Equal(1, calls);

            release.Set();
            await resumed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(42, CurrentPercent(blocked));
            await Eventually(() => notifications.ActiveCalls == 0);
            Assert.InRange(calls, 2, 3);
        }
        finally
        {
            release.Set();
            blocked.ItemsChanged -= stalled;
            foreach (var item in trace) output.WriteLine(JsonSerializer.Serialize(item));
        }
    }

    [Fact]
    public async Task ReconnectReplaysLatestWithoutReadingAndDisposesWithoutWaitingForOldHost()
    {
        var reader = new MutableReader();
        var store = new TelemetryStatusStore(reader);
        await store.RefreshAsync();
        using var provider = new GHubTrayCommandsProvider(
            store, new GHubTraySettingsStore(), TimeSpan.FromHours(1));
        var main = Assert.IsType<GHubTrayPage>(provider.TopLevelCommands()[0].Command);
        var dock = Assert.IsType<GHubTrayDockBand>(provider.GetDockBands()[0]);
        var detail = Assert.IsType<TelemetryDeviceInfoPage>(main.GetItems()[0].Command);
        var detailEvents = 0;
        ((IContentPage)detail).ItemsChanged += (_, _) => Interlocked.Increment(ref detailEvents);
        await Eventually(() => detailEvents > 0);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dockPage = Assert.IsAssignableFrom<IListPage>(dock.Command);
        dockPage.ItemsChanged += (_, _) => { entered.TrySetResult(); release.Wait(); };
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            reader.Percent = 67;
            await store.RefreshAsync();
            provider.ResetHostSubscriptions();
            Assert.Equal(0, detail.Notifications.SubscriberCount);
            var readsBeforeReconnect = reader.ReadCount;
            var connected = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            dockPage.ItemsChanged += (_, _) => connected.TrySetResult(CurrentPercent(dockPage));
            Assert.Equal(67, await connected.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(readsBeforeReconnect, reader.ReadCount);
            Assert.Equal(1, dock.Notifications.SubscriberCount);
            output.WriteLine("reconnect: old subscriptions retired -> latest 67% replayed -> no extra device read");
            await Task.Run(provider.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, dock.Notifications.SubscriberCount);
            output.WriteLine("dispose: completed while old host callback was still blocked");
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task AutomaticReadsContinueWhileDockNotificationIsBlocked()
    {
        var reader = new MutableReader();
        var store = new TelemetryStatusStore(reader);
        await store.RefreshAsync();
        using var provider = new GHubTrayCommandsProvider(
            store, new GHubTraySettingsStore(), TimeSpan.FromMilliseconds(20));
        var dock = Assert.IsType<GHubTrayDockBand>(provider.GetDockBands()[0]);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        Assert.IsAssignableFrom<IListPage>(dock.Command).ItemsChanged += (_, _) =>
        {
            entered.TrySetResult();
            release.Wait();
        };
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            reader.Percent = 42;
            await Eventually(() => reader.ReadCount >= 3 && store.GetStatus().Devices[0].Percent == 42);
            Assert.Equal(1, dock.Notifications.ActiveCalls);
            Assert.Equal("42%", dock.Items[0].Title);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task MissingDeviceDoesNotReuseLiveValuesInAnOpenDetailPage()
    {
        var reader = new MutableReader();
        var store = new TelemetryStatusStore(reader);
        await store.RefreshAsync();
        using var page = new TelemetryDeviceInfoPage(store, store.GetStatus().Devices[0]);
        reader.Missing = true;
        await store.RefreshAsync();
        var form = Assert.IsType<FormContent>(page.GetContent()[0]);
        var json = JsonNode.Parse(form.DataJson)!;
        Assert.Equal("現在値は未確認", json["batteryText"]!.GetValue<string>());
        Assert.Equal("現在は未確認", json["powerText"]!.GetValue<string>());
        Assert.DoesNotContain("51%", form.DataJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DetailRefreshPublishesNewFormWithoutCallingOldFormSubscribers()
    {
        var reader = new MutableReader();
        var store = new TelemetryStatusStore(reader);
        await store.RefreshAsync();
        using var detail = new TelemetryDeviceInfoPage(store, store.GetStatus().Devices[0]);
        var oldForm = Assert.IsType<FormContent>(detail.GetContent()[0]);
        var oldJson = oldForm.DataJson;
        oldForm.PropChanged += (_, _) => throw new InvalidOperationException("Old form must be immutable");
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ((IContentPage)detail).ItemsChanged += (_, _) =>
        {
            var form = Assert.IsType<FormContent>(detail.GetContent()[0]);
            if (JsonNode.Parse(form.DataJson)?["batteryText"]?.GetValue<string>() == "42%") updated.TrySetResult();
        };
        reader.Percent = 42;
        var command = Assert.IsType<TelemetryRefreshCommand>(Assert.IsType<CommandContextItem>(detail.Commands[0]).Command);
        await Task.Run(() => command.Invoke()).WaitAsync(TimeSpan.FromSeconds(5));
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(oldJson, oldForm.DataJson);
        Assert.NotSame(oldForm, detail.GetContent()[0]);
    }

    private static int CurrentPercent(IListPage page)
    {
        var item = page.GetItems()[0];
        var text = item.Title.Contains('%') ? item.Title : item.Subtitle;
        return int.Parse(text.Split('%')[0]);
    }

    private static async Task Eventually(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) await Task.Delay(10, timeout.Token);
    }

    private sealed class MutableReader(Action? onRead = null) : ITelemetryReader
    {
        internal int Percent = 51;
        internal int ReadCount;
        internal bool Missing;
        public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref ReadCount);
            onRead?.Invoke();
            if (Missing) return new(0, []);
            return new(1, [new DeviceTelemetry("G703", 0xC539, 1, 0x1004,
                Percent, null, PowerState.Discharging, 0)]) { ReceiverProductIds = [0xC539] };
        }
    }
}
