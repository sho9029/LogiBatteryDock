using System.Text.Json;

namespace LogiBatteryDock.Core;

public sealed record LogiBatteryDockSettings
{
    public const int CurrentSchemaVersion = 1;
    public const int DefaultRefreshIntervalSeconds = 60;
    public const int MinRefreshIntervalSeconds = 10;
    public const int MaxRefreshIntervalSeconds = 3600;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public int RefreshIntervalSeconds { get; init; } = DefaultRefreshIntervalSeconds;
    public bool StartWithWindows { get; init; }
    public string? DockDeviceId { get; init; }

    public TimeSpan RefreshInterval =>
        TimeSpan.FromSeconds(RefreshIntervalSeconds);
}

public static class LogiBatteryDockSettingsValidator
{
    public static LogiBatteryDockSettings Normalize(LogiBatteryDockSettings? settings)
    {
        var source = settings ?? new LogiBatteryDockSettings();
        return source with
        {
            SchemaVersion = LogiBatteryDockSettings.CurrentSchemaVersion,
            RefreshIntervalSeconds = Math.Clamp(
                source.RefreshIntervalSeconds,
                LogiBatteryDockSettings.MinRefreshIntervalSeconds,
                LogiBatteryDockSettings.MaxRefreshIntervalSeconds)
        };
    }
}

public sealed class LogiBatteryDockSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public LogiBatteryDockSettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LogiBatteryDock",
            "settings.json");
    }

    public string FilePath { get; }
    public bool HadLoadError { get; private set; }

    public LogiBatteryDockSettings Load()
    {
        HadLoadError = false;
        if (!File.Exists(FilePath))
        {
            return new LogiBatteryDockSettings();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<LogiBatteryDockSettings>(
                File.ReadAllText(FilePath),
                JsonOptions);
            if (parsed is null || parsed.SchemaVersion != LogiBatteryDockSettings.CurrentSchemaVersion)
            {
                HadLoadError = true;
                return new LogiBatteryDockSettings();
            }

            return LogiBatteryDockSettingsValidator.Normalize(parsed);
        }
        catch (JsonException)
        {
            HadLoadError = true;
            return new LogiBatteryDockSettings();
        }
        catch (IOException)
        {
            HadLoadError = true;
            return new LogiBatteryDockSettings();
        }
        catch (UnauthorizedAccessException)
        {
            HadLoadError = true;
            return new LogiBatteryDockSettings();
        }
    }

    public void Save(LogiBatteryDockSettings settings)
    {
        var normalized = LogiBatteryDockSettingsValidator.Normalize(settings);
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
