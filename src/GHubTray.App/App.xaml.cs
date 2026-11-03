using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace GHubTray.App;

public sealed partial class App : Application
{
    private const string MutexName = @"Local\GHubTray.SingleInstance";
    private static Mutex? _singleInstanceMutex;
    private TaskbarIcon? _trayIcon;
    private TrayViewModel? _viewModel;
    private DispatcherTimer? _timer;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
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
        var lifetimeToken = _lifetimeCancellation.Token;

        var refresh = (XamlUICommand)Resources["RefreshCommand"];
        refresh.ExecuteRequested += async (_, _) => await _viewModel.RefreshAsync(lifetimeToken);

        var exit = (XamlUICommand)Resources["ExitCommand"];
        exit.ExecuteRequested += (_, _) => ExitApplication();

        _trayIcon = (TaskbarIcon)Resources["TrayIcon"];
        _trayIcon.ForceCreate();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += async (_, _) => await _viewModel.RefreshAsync(lifetimeToken);
        _timer.Start();
        _ = _viewModel.RefreshAsync(lifetimeToken);
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
        try
        {
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
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            _lifetimeCancellation.Dispose();
            Environment.Exit(0);
        }
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
