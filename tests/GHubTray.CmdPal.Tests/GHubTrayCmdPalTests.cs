using GHubTray.Core;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using Xunit;

namespace GHubTray.CmdPal.Tests;

public sealed class GHubTrayCmdPalTests
{
    [Fact]
    public async Task PageReadsDeviceWhenTrayProcessIsAbsent()
    {
        var reader = new FakeReader(Snapshot(new DeviceTelemetry(
            "G703 LIGHTSPEED Wireless Gaming Mouse w/ HERO",
            0xC547,
            1,
            0x1001,
            null,
            3960,
            PowerState.Discharging,
            0x01)));
        var store = new TelemetryStatusStore(reader);
        var page = new GHubTrayPage(store);

        await store.RefreshAsync();

        var items = page.GetItems();
        Assert.Contains(items, item => item.Title == "G703");
        var deviceItem = Assert.Single(items, item => item.Title == "G703");
        var command = Assert.IsType<TelemetryDeviceInfoPage>(deviceItem.Command);
        Assert.Equal(string.Empty, command.Name);
        var content = Assert.IsType<FormContent>(Assert.Single(command.GetContent()));
        var data = Assert.IsType<JsonObject>(JsonNode.Parse(content.DataJson));
        Assert.Equal("残量は未提供", data["batteryText"]?.GetValue<string>());
        Assert.Equal("接続中", data["powerText"]?.GetValue<string>());
        Assert.Contains("${batteryText}", content.TemplateJson, StringComparison.Ordinal);
        Assert.DoesNotContain("${technicalText}", content.TemplateJson, StringComparison.Ordinal);
        Assert.DoesNotContain(items, item => item.Title == "GHubTrayが起動していません");
        Assert.DoesNotContain(items, item => item.Title == "トレイを開く");
        Assert.DoesNotContain(items, item => item.Title == "今すぐ更新");
        Assert.Equal("残量は未提供 / 接続中", deviceItem.Subtitle);
        var refresh = Assert.IsType<CommandContextItem>(Assert.Single(deviceItem.MoreCommands));
        Assert.Equal("更新", refresh.Title);
        Assert.DoesNotContain("取得", deviceItem.Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DockUsesTheSameIndependentStatusStore()
    {
        var reader = new FakeReader(Snapshot(new DeviceTelemetry(
            "G703 LIGHTSPEED Wireless Gaming Mouse w/ HERO",
            0xC539,
            2,
            0x1004,
            73,
            null,
            PowerState.Discharging,
            0x01)));
        var store = new TelemetryStatusStore(reader);
        using var provider = new GHubTrayCommandsProvider(store);

        await store.RefreshAsync();

        var dock = Assert.IsType<GHubTrayDockBand>(Assert.Single(provider.GetDockBands()));
        var deviceItem = Assert.Single(dock.Items, item => item.Title == "73%");
        var command = Assert.IsType<TelemetryDeviceInfoPage>(deviceItem.Command);
        Assert.Equal(string.Empty, command.Name);
        Assert.Equal("73%", deviceItem.Title);
        Assert.Equal("G703", deviceItem.Subtitle);
        Assert.NotNull(deviceItem.Icon);
        var refresh = Assert.IsType<CommandContextItem>(
            Assert.Single(
                deviceItem.MoreCommands.OfType<CommandContextItem>(),
                item => item.Title == "更新"));
        Assert.Equal("更新", refresh.Title);
        Assert.DoesNotContain(dock.Items, item => item.Title == "更新");
        Assert.DoesNotContain(dock.Items, item => item.Subtitle?.Contains("GHubTray未起動", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task DockSelectionChangesThePersistentDeviceWithoutOpeningInfo()
    {
        var reader = new FakeReader(Snapshot(
            new DeviceTelemetry(
                "G703 LIGHTSPEED Wireless Gaming Mouse",
                0xC539,
                1,
                0x1004,
                73,
                null,
                PowerState.Discharging,
                0x01),
            new DeviceTelemetry(
                "K380 Wireless Keyboard",
                0xB342,
                2,
                0x1004,
                15,
                null,
                PowerState.Discharging,
                0x01)));
        var store = new TelemetryStatusStore(reader);
        await store.RefreshAsync();
        var devices = store.GetStatus().Devices;
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"GHubTray.CmdPal.{Guid.NewGuid():N}.json");
        var settingsStore = new GHubTraySettingsStore(settingsPath);

        try
        {
            settingsStore.Save(new GHubTraySettings
            {
                DockDeviceId = devices[1].Id
            });
            var dock = new GHubTrayDockBand(store, settingsStore);

            var currentItem = Assert.Single(dock.Items);
            Assert.Equal("15%", currentItem.Title);
            var selectFirst = Assert.Single(
                currentItem.MoreCommands.OfType<CommandContextItem>(),
                item => item.Title == "G703");
            var command = Assert.IsType<DockDeviceSelectionCommand>(selectFirst.Command);

            command.Invoke();

            Assert.Equal("73%", Assert.Single(dock.Items).Title);
            Assert.Equal(devices[0].Id, settingsStore.Load().DockDeviceId);
            Assert.IsType<TelemetryDeviceInfoPage>(Assert.Single(dock.Items).Command);
        }
        finally
        {
            if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }
        }
    }

    [Fact]
    public async Task DockContextDeviceItemsAreSelectionCommandsAndInfoCommandIsUnnamed()
    {
        var reader = new FakeReader(Snapshot(
            new DeviceTelemetry(
                "G703 LIGHTSPEED Wireless Gaming Mouse",
                0xC539,
                1,
                0x1004,
                73,
                null,
                PowerState.Discharging,
                0x01),
            new DeviceTelemetry(
                "K380 Wireless Keyboard",
                0xB342,
                2,
                0x1004,
                15,
                null,
                PowerState.Discharging,
                0x01)));
        var store = new TelemetryStatusStore(reader);
        await store.RefreshAsync();
        var devices = store.GetStatus().Devices;
        var settingsPath = Path.Combine(
            Path.GetTempPath(),
            $"GHubTray.CmdPal.{Guid.NewGuid():N}.json");
        var settingsStore = new GHubTraySettingsStore(settingsPath);

        try
        {
            settingsStore.Save(new GHubTraySettings
            {
                DockDeviceId = devices[1].Id
            });
            var dock = new GHubTrayDockBand(store, settingsStore);
            var currentItem = Assert.Single(dock.Items);
            var contextItems = currentItem.MoreCommands
                .OfType<CommandContextItem>()
                .ToArray();

            Assert.Equal(3, contextItems.Length);
            Assert.All(
                contextItems.Where(item => item.Title != "更新"),
                item => Assert.IsType<DockDeviceSelectionCommand>(item.Command));
            Assert.Contains(contextItems, item => item.Title == "G703");
            Assert.Contains(contextItems, item => item.Title == "K380");
            Assert.Equal(string.Empty, Assert.IsType<TelemetryDeviceInfoPage>(currentItem.Command).Name);
        }
        finally
        {
            if (File.Exists(settingsPath))
            {
                File.Delete(settingsPath);
            }
        }
    }

    [Fact]
    public async Task EstimatedVoltageUsesPlainPercentageInCommandPalette()
    {
        var reader = new FakeReader(Snapshot(new DeviceTelemetry(
            "G703 LIGHTSPEED Wireless Gaming Mouse",
            0xC539,
            1,
            0x1001,
            71,
            3927,
            PowerState.Discharging,
            0x01)
        {
            Source = TelemetrySource.NativeVoltageEstimate
        }));
        var store = new TelemetryStatusStore(reader);
        var page = new GHubTrayPage(store);

        await store.RefreshAsync();

        var deviceItem = Assert.Single(page.GetItems(), item => item.Title == "G703");
        Assert.Equal("71% / 接続中", deviceItem.Subtitle);
        Assert.DoesNotContain("（推定）", deviceItem.Subtitle, StringComparison.Ordinal);
        Assert.Single(deviceItem.MoreCommands);
    }

    [Fact]
    public async Task EmptyReceiverResultDoesNotRemainInLoadingState()
    {
        var store = new TelemetryStatusStore(new FakeReader(new LogitechReadSnapshot(1, [])
        {
            ReceiverProductIds = [0xC539]
        }));
        using var provider = new GHubTrayCommandsProvider(store);

        await store.RefreshAsync();

        var dock = Assert.IsType<GHubTrayDockBand>(Assert.Single(provider.GetDockBands()));
        Assert.Contains(dock.Items, item => item.Subtitle == "デバイス応答なし");
    }

    [Fact]
    public async Task RefreshCommandWaitsForNewSnapshotAndPublishesOneUpdate()
    {
        var reader = new MutableReader(Snapshot(new DeviceTelemetry(
            "G703 LIGHTSPEED Wireless Gaming Mouse",
            0xC539,
            1,
            0x1004,
            28,
            null,
            PowerState.Discharging,
            0x01)));
        var store = new TelemetryStatusStore(reader);
        await store.RefreshAsync();

        reader.Snapshot = Snapshot(new DeviceTelemetry(
            "G703 LIGHTSPEED Wireless Gaming Mouse",
            0xC539,
            1,
            0x1004,
            15,
            null,
            PowerState.Discharging,
            0x01));
        using var subscription = store.Subscribe();
        var before = await subscription.Updates.ReadAsync();
        var command = new TelemetryRefreshCommand(store);

        command.Invoke();

        var after = await subscription.Updates.ReadAsync();
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.False(subscription.Updates.TryRead(out _));
        Assert.Equal(15, Assert.Single(store.GetStatus().Devices).Percent);
    }

    [Fact]
    public async Task DockRefreshReplacesTheItemAndNotifiesTheExistingPage()
    {
        var reader = new MutableReader(Snapshot(new DeviceTelemetry(
            "G703 LIGHTSPEED Wireless Gaming Mouse",
            0xC539,
            1,
            0x1004,
            28,
            null,
            PowerState.Discharging,
            0x01)));
        var store = new TelemetryStatusStore(reader);
        await store.RefreshAsync();
        var dock = new GHubTrayDockBand(store);

        var originalItem = Assert.Single(dock.Items, item => item.Title == "28%");
        var originalPage = Assert.IsAssignableFrom<IListPage>(dock.Command);
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        originalPage.ItemsChanged += (_, _) =>
        {
            if (originalPage.GetItems()[0].Title == "15%") updated.TrySetResult();
        };
        reader.Snapshot = Snapshot(new DeviceTelemetry(
            "G703 LIGHTSPEED Wireless Gaming Mouse",
            0xC539,
            1,
            0x1004,
            15,
            null,
            PowerState.Discharging,
            0x01));

        await store.RefreshAsync();
        dock.RefreshItems();

        var refreshedItem = Assert.Single(dock.Items, item => item.Title == "15%");
        Assert.NotSame(originalItem, refreshedItem);
        Assert.Same(originalPage, dock.Command);
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("G703", refreshedItem.Subtitle);
        Assert.NotNull(refreshedItem.Icon);
    }

    [Fact]
    public async Task DockRefreshesAutomaticallyFromTheProviderTimer()
    {
        var reader = new MutableReader(Snapshot(new DeviceTelemetry(
            "G703 LIGHTSPEED Wireless Gaming Mouse",
            0xC539,
            1,
            0x1004,
            28,
            null,
            PowerState.Discharging,
            0x01)));
        var store = new TelemetryStatusStore(reader);
        var provider = new GHubTrayCommandsProvider(
            store,
            new GHubTraySettingsStore(),
            TimeSpan.FromMilliseconds(20));

        try
        {
            await store.RefreshAsync();
            var dock = Assert.IsType<GHubTrayDockBand>(Assert.Single(provider.GetDockBands()));
            var originalItem = Assert.Single(dock.Items, item => item.Title == "28%");

            reader.Snapshot = Snapshot(new DeviceTelemetry(
                "G703 LIGHTSPEED Wireless Gaming Mouse",
                0xC539,
                1,
                0x1004,
                15,
                null,
                PowerState.Discharging,
                0x01));

            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (DateTime.UtcNow < deadline
                && dock.Items.FirstOrDefault()?.Title != "15%")
            {
                await Task.Delay(10);
            }

            var refreshedItem = Assert.Single(dock.Items, item => item.Title == "15%");
            Assert.NotSame(originalItem, refreshedItem);
        }
        finally
        {
            provider.Dispose();
        }
    }

    [Theory]
    [InlineData(unchecked((int)0x80010108))]
    [InlineData(unchecked((int)0x800706BA))]
    [InlineData(unchecked((int)0x80004005))]
    public async Task DockRecoversFromNoResponseDespiteAFailingEarlierHost(int hresult)
    {
        var reader = new MutableReader(new LogitechReadSnapshot(1, []));
        var store = new TelemetryStatusStore(reader);
        await store.RefreshAsync();
        using var provider = new GHubTrayCommandsProvider(store);
        var dock = Assert.IsType<GHubTrayDockBand>(Assert.Single(provider.GetDockBands()));
        Assert.Equal("デバイス応答なし", Assert.Single(dock.Items).Subtitle);
        var page = Assert.IsAssignableFrom<IListPage>(dock.Command);
        var failedCalls = 0;
        var receivedTitles = new ConcurrentQueue<string>();
        page.ItemsChanged += (_, _) =>
        {
            Interlocked.Increment(ref failedCalls);
            throw new COMException("Previous host callback failed", hresult);
        };
        page.ItemsChanged += (_, _) => receivedTitles.Enqueue(Assert.Single(page.GetItems()).Title);
        var providerNotifications = 0;
        provider.ItemsChanged += (_, _) => providerNotifications++;

        foreach (var percent in new[] { 51, 50 })
        {
            reader.Snapshot = Snapshot(new DeviceTelemetry(
                "G703 LIGHTSPEED Wireless Gaming Mouse", 0xC539, 1, 0x1004,
                percent, null, PowerState.Discharging, 0x01));
            await store.RefreshAsync();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!receivedTitles.Contains($"{percent}%") && DateTime.UtcNow < deadline)
                await Task.Delay(10);
            Assert.Contains($"{percent}%", receivedTitles);
        }

        if (hresult != unchecked((int)0x80004005)) Assert.Equal(1, failedCalls);
        else Assert.True(failedCalls >= 1);
        Assert.Equal(0, providerNotifications);
        Assert.Equal("G703", Assert.Single(page.GetItems()).Subtitle);
        Assert.NotNull(Assert.Single(page.GetItems()).Icon);
    }

    private static LogitechReadSnapshot Snapshot(params DeviceTelemetry[] devices) =>
        new(1, devices)
        {
            ReceiverProductIds = [0xC539],
            ReceiverKeys = ["pid:C539"]
        };

    private sealed class FakeReader : ITelemetryReader
    {
        private readonly LogitechReadSnapshot _snapshot;

        public FakeReader(LogitechReadSnapshot snapshot) => _snapshot = snapshot;

        public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default) => _snapshot;
    }

    private sealed class MutableReader : ITelemetryReader
    {
        public MutableReader(LogitechReadSnapshot snapshot) => Snapshot = snapshot;

        public LogitechReadSnapshot Snapshot { get; set; }

        public LogitechReadSnapshot ReadSnapshot(CancellationToken cancellationToken = default) => Snapshot;
    }
}
