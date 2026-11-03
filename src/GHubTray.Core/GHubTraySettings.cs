using System.Text.Json;

namespace GHubTray.Core;

public sealed record GHubTraySettings
{
    public const int CurrentSchemaVersion = 1;
    public const int DefaultRefreshIntervalSeconds = 30;
    public const int MinRefreshIntervalSeconds = 10;
    public const int MaxRefreshIntervalSeconds = 3600;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public int RefreshIntervalSeconds { get; init; } = DefaultRefreshIntervalSeconds;
    public bool StartWithWindows { get; init; }
    public string? DockDeviceId { get; init; }

    public TimeSpan RefreshInterval =>
        TimeSpan.FromSeconds(RefreshIntervalSeconds);
}

public static class GHubTraySettingsValidator
{
    public static GHubTraySettings Normalize(GHubTraySettings? settings)
    {
        var source = settings ?? new GHubTraySettings();
        return source with
        {
            SchemaVersion = GHubTraySettings.CurrentSchemaVersion,
            RefreshIntervalSeconds = Math.Clamp(
                source.RefreshIntervalSeconds,
                GHubTraySettings.MinRefreshIntervalSeconds,
                GHubTraySettings.MaxRefreshIntervalSeconds)
        };
    }
}

public sealed class GHubTraySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public GHubTraySettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GHubTray",
            "settings.json");
    }

    public string FilePath { get; }
    public bool HadLoadError { get; private set; }

    public GHubTraySettings Load()
    {
        HadLoadError = false;
        if (!File.Exists(FilePath))
        {
            return new GHubTraySettings();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<GHubTraySettings>(
                File.ReadAllText(FilePath),
                JsonOptions);
            if (parsed is null || parsed.SchemaVersion != GHubTraySettings.CurrentSchemaVersion)
            {
                HadLoadError = true;
                return new GHubTraySettings();
            }

            return GHubTraySettingsValidator.Normalize(parsed);
        }
        catch (JsonException)
        {
            HadLoadError = true;
            return new GHubTraySettings();
        }
        catch (IOException)
        {
            HadLoadError = true;
            return new GHubTraySettings();
        }
        catch (UnauthorizedAccessException)
        {
            HadLoadError = true;
            return new GHubTraySettings();
        }
    }

    public void Save(GHubTraySettings settings)
    {
        var normalized = GHubTraySettingsValidator.Normalize(settings);
        var directory = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Settings path must include a directory.");
        }

        Directory.CreateDirectory(directory);
        var temporaryPath = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(normalized, JsonOptions));
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
