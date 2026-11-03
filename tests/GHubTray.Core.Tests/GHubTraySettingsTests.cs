using GHubTray.Core;
using Xunit;

namespace GHubTray.Core.Tests;

public sealed class GHubTraySettingsTests
{
    [Fact]
    public void MissingSettingsUseSafeDefaults()
    {
        using var directory = TemporaryDirectory.Create();
        var store = new GHubTraySettingsStore(Path.Combine(directory.Path, "settings.json"));

        var settings = store.Load();

        Assert.Equal(30, settings.RefreshIntervalSeconds);
        Assert.False(settings.StartWithWindows);
        Assert.False(store.HadLoadError);
    }

    [Fact]
    public void SaveNormalizesIntervalAndRoundTrips()
    {
        using var directory = TemporaryDirectory.Create();
        var store = new GHubTraySettingsStore(Path.Combine(directory.Path, "settings.json"));

        store.Save(new GHubTraySettings
        {
            RefreshIntervalSeconds = 1,
            StartWithWindows = true,
            DockDeviceId = "device:test"
        });
        var settings = store.Load();

        Assert.Equal(GHubTraySettings.MinRefreshIntervalSeconds, settings.RefreshIntervalSeconds);
        Assert.True(settings.StartWithWindows);
        Assert.Equal("device:test", settings.DockDeviceId);
        Assert.Equal(TimeSpan.FromSeconds(10), settings.RefreshInterval);
        Assert.False(store.HadLoadError);
    }

    [Fact]
    public void CorruptSettingsFallBackWithoutOverwritingTheFile()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, "{ not valid json");
        var store = new GHubTraySettingsStore(path);

        var settings = store.Load();

        Assert.Equal(30, settings.RefreshIntervalSeconds);
        Assert.True(store.HadLoadError);
        Assert.Equal("{ not valid json", File.ReadAllText(path));
    }

    [Fact]
    public void UnknownSchemaFallsBackToDefaults()
    {
        using var directory = TemporaryDirectory.Create();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path, """{"SchemaVersion":99,"RefreshIntervalSeconds":120}""");
        var store = new GHubTraySettingsStore(path);

        var settings = store.Load();

        Assert.Equal(GHubTraySettings.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(30, settings.RefreshIntervalSeconds);
        Assert.True(store.HadLoadError);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"GHubTray.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
