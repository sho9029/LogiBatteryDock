using Microsoft.Win32;

namespace GHubTray.App;

internal sealed class WindowsStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GHubTray";

    public void Apply(bool enabled)
    {
        if (!enabled)
        {
            using var existingRunKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            existingRunKey?.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windowsの自動起動設定を開けません。");
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            throw new InvalidOperationException("実行ファイルのパスを取得できません。");
        }

        runKey.SetValue(ValueName, Quote(processPath), RegistryValueKind.String);
    }

    private static string Quote(string path) => $"\"{path.Replace("\"", "\\\"")}\"";
}
