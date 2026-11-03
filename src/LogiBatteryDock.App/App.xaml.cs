using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using LogiBatteryDock.Core;
using System.Runtime.InteropServices;

namespace LogiBatteryDock.App;

public sealed partial class App : Application
{
    private const string MutexName = @"Local\LogiBatteryDock.SingleInstance";
    private static Mutex? _singleInstanceMutex;
    private TaskbarIcon? _trayIcon;
    private TrayViewModel? _viewModel;
    private DispatcherTimer? _timer;
    private WindowsResumeNotification? _resumeNotification;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly LogiBatteryDockSettingsStore _settingsStore = new();
    private readonly WindowsStartupRegistration _startupRegistration = new();
    private LogiBatteryDockSettings _settings = new();
    private SettingsWindow? _settingsWindow;
    private DispatcherQueue? _dispatcherQueue;
    private TelemetryPipeServer? _pipeServer;
    private bool _initialized;
    private bool _exiting;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (_initialized)
        {
            return;
        }
        _initialized = true;
        if (!TryAcquireSingleInstance())
        {
            Environment.Exit(0);
            return;
        }

        _viewModel = (TrayViewModel)Resources["TrayViewModel"];
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _settings = LogiBatteryDockSettingsValidator.Normalize(_settingsStore.Load());
        var lifetimeToken = _lifetimeCancellation.Token;

        var refresh = (XamlUICommand)Resources["RefreshCommand"];
        refresh.ExecuteRequested += async (_, _) => await _viewModel.RefreshAsync(lifetimeToken);

        var settingsCommand = (XamlUICommand)Resources["SettingsCommand"];
        settingsCommand.ExecuteRequested += (_, _) => ShowSettings();

        var exit = (XamlUICommand)Resources["ExitCommand"];
        exit.ExecuteRequested += (_, _) => ExitApplication();

        _trayIcon = (TaskbarIcon)Resources["TrayIcon"];
        // This Single-valued property fails to initialize from a XAML attribute.
        ((GeneratedIconSource)_trayIcon.IconSource!).BorderThickness = 5;
        _trayIcon.ForceCreate();

        _timer = new DispatcherTimer { Interval = _settings.RefreshInterval };
        _timer.Tick += async (_, _) => await _viewModel.RefreshAsync(lifetimeToken);
        _timer.Start();
        _ = _viewModel.RefreshAsync(lifetimeToken);
        _resumeNotification = WindowsResumeNotification.TryCreate(() =>
            _dispatcherQueue.TryEnqueue(async () =>
            {
                if (!_exiting) await _viewModel.RefreshAfterResumeAsync(lifetimeToken);
            }));

        var viewModel = _viewModel;
        _pipeServer = new(
            viewModel.GetStatus,
            RefreshFromPipeAsync,
            OpenTrayFromPipe);
        _pipeServer.Start();
    }

    private async void ExitApplication()
    {
        if (_exiting)
        {
            return;
        }
        _exiting = true;
        _timer?.Stop();
        _lifetimeCancellation.Cancel();
        _resumeNotification?.Dispose();
        try
        {
            if (_pipeServer is not null)
            {
                await _pipeServer.StopAsync();
                _pipeServer = null;
            }
            if (_viewModel is not null)
            {
                await _viewModel.WaitForRefreshAsync();
            }
        }
        catch (Exception)
        {
            // Exit still owns cleanup if a provider fails while cancellation is propagating.
        }
        finally
        {
            _trayIcon?.Dispose();
            _settingsWindow?.Close();
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            _lifetimeCancellation.Dispose();
            Environment.Exit(0);
        }
    }

    private void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, SaveSettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    private void SaveSettings(LogiBatteryDockSettings settings)
    {
        var normalized = LogiBatteryDockSettingsValidator.Normalize(settings);
        _settingsStore.Save(normalized);
        _startupRegistration.Apply(normalized.StartWithWindows);
        _settings = normalized;
        if (_timer is not null)
        {
            _timer.Interval = normalized.RefreshInterval;
        }
    }

    private async Task RefreshFromPipeAsync(CancellationToken cancellationToken)
    {
        async Task RefreshOnUiThread()
        {
            if (_viewModel is not null)
            {
                await _viewModel.RefreshAsync(cancellationToken);
                await _viewModel.WaitForRefreshAsync();
            }
        }

        if (_dispatcherQueue?.HasThreadAccess == true)
        {
            await RefreshOnUiThread();
            return;
        }

        if (_dispatcherQueue is null)
        {
            throw new InvalidOperationException("UI dispatcher is unavailable.");
        }

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await RefreshOnUiThread();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }))
        {
            throw new InvalidOperationException("UI dispatcher rejected the refresh request.");
        }

        await completion.Task.WaitAsync(cancellationToken);
    }

    private void OpenTrayFromPipe()
    {
        if (_dispatcherQueue?.HasThreadAccess == true)
        {
            ShowTrayPopup();
            return;
        }

        _dispatcherQueue?.TryEnqueue(ShowTrayPopup);
    }

    private void ShowTrayPopup()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.ShowTrayPopup(GetCursorPosition());
    }

    private static System.Drawing.Point GetCursorPosition() =>
        GetCursorPos(out var point)
            ? new System.Drawing.Point(point.X, point.Y)
            : new System.Drawing.Point();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private static bool TryAcquireSingleInstance()
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            return true;
        }

        _singleInstanceMutex.Dispose();
        _singleInstanceMutex = null;
        return false;
    }
}
