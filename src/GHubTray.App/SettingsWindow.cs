using GHubTray.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace GHubTray.App;

public sealed class SettingsWindow : Window
{
    private readonly Action<GHubTraySettings> _save;
    private readonly TextBox _refreshIntervalBox;
    private readonly ToggleSwitch _startWithWindowsToggle;
    private readonly TextBlock _errorText;

    public SettingsWindow()
        : this(new GHubTraySettings(), static _ => { })
    {
    }

    public SettingsWindow(GHubTraySettings initialSettings, Action<GHubTraySettings> save)
    {
        _save = save;
        Title = "GHubTray 設定";

        _refreshIntervalBox = new TextBox
        {
            Text = initialSettings.RefreshIntervalSeconds.ToString(),
            PlaceholderText = "30",
            Width = 108,
            MaxLength = 4,
            HorizontalContentAlignment = HorizontalAlignment.Right
        };
        AutomationProperties.SetName(_refreshIntervalBox, "更新間隔（秒）");

        _startWithWindowsToggle = new ToggleSwitch
        {
            OnContent = "オン",
            OffContent = "オフ",
            IsOn = initialSettings.StartWithWindows,
            MinWidth = 0,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(_startWithWindowsToggle, "Windows起動時に開始");

        _errorText = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
            Foreground = GetResourceBrush("SystemFillColorCriticalBrush"),
            Margin = new Thickness(2, 2, 2, 0)
        };

        var root = new Grid
        {
            Width = 500,
            MinHeight = 420,
            Padding = new Thickness(24, 20, 24, 16),
            RowSpacing = 14,
            Background = GetResourceBrush("ApplicationPageBackgroundThemeBrush")
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Spacing = 3 };
        header.Children.Add(new TextBlock
        {
            Text = "GHubTray 設定",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = "通知領域に表示する機器の確認方法を調整できます。",
            FontSize = 13,
            Foreground = GetResourceBrush("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(header);

        var settings = new StackPanel { Spacing = 12 };
        settings.Children.Add(CreateRefreshCard());
        settings.Children.Add(CreateStartupCard());
        settings.Children.Add(new TextBlock
        {
            Text = "設定はこのPCの現在のユーザーにだけ適用されます。",
            FontSize = 12,
            Foreground = GetResourceBrush("TextFillColorTertiaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 0, 2, 0)
        });
        settings.Children.Add(_errorText);

        var scrollViewer = new ScrollViewer
        {
            Content = settings,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scrollViewer, 1);
        root.Children.Add(scrollViewer);

        var footer = new Border
        {
            Padding = new Thickness(0, 14, 0, 0),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = GetResourceBrush("CardStrokeColorDefaultBrush")
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        var cancelButton = new Button
        {
            Content = "キャンセル",
            MinWidth = 96
        };
        cancelButton.Click += CancelButton_Click;
        var saveButton = new Button
        {
            Content = "保存",
            MinWidth = 96
        };
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accentStyle)
            && accentStyle is Style style)
        {
            saveButton.Style = style;
        }
        saveButton.Click += SaveButton_Click;
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);
        footer.Child = buttons;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        ConfigureWindow();
    }

    private Border CreateRefreshCard()
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "状態の確認",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });

        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var description = new StackPanel { Spacing = 3 };
        description.Children.Add(new TextBlock
        {
            Text = "更新間隔（秒）",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        description.Children.Add(new TextBlock
        {
            Text = "10〜3600秒。短くすると確認回数が増えます。",
            FontSize = 12,
            Foreground = GetResourceBrush("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        row.Children.Add(description);
        Grid.SetColumn(_refreshIntervalBox, 1);
        row.Children.Add(_refreshIntervalBox);
        content.Children.Add(row);

        return CreateCard(content);
    }

    private Border CreateStartupCard()
    {
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var description = new StackPanel { Spacing = 3 };
        description.Children.Add(new TextBlock
        {
            Text = "Windows起動時に開始",
            FontSize = 16,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        description.Children.Add(new TextBlock
        {
            Text = "サインイン後に通知領域で自動的に監視を始めます。",
            FontSize = 12,
            Foreground = GetResourceBrush("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        row.Children.Add(description);
        Grid.SetColumn(_startWithWindowsToggle, 1);
        row.Children.Add(_startWithWindowsToggle);

        return CreateCard(row);
    }

    private static Border CreateCard(UIElement child) => new()
    {
        Padding = new Thickness(16),
        Background = GetResourceBrush("CardBackgroundFillColorDefaultBrush"),
        BorderBrush = GetResourceBrush("CardStrokeColorDefaultBrush"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Child = child
    };

    private void ConfigureWindow()
    {
        var hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow?.Resize(new SizeInt32(540, 500));
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _errorText.Visibility = Visibility.Collapsed;
        if (!int.TryParse(_refreshIntervalBox.Text, out var interval))
        {
            ShowError("更新間隔は整数で入力して。");
            return;
        }

        try
        {
            var settings = GHubTraySettingsValidator.Normalize(new GHubTraySettings
            {
                RefreshIntervalSeconds = interval,
                StartWithWindows = _startWithWindowsToggle.IsOn
            });
            _save(settings);
            Close();
        }
        catch (Exception exception)
        {
            ShowError($"設定を保存できません: {exception.Message}");
        }
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorText.Visibility = Visibility.Visible;
    }

    private static Brush? GetResourceBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value)
            ? value as Brush
            : null;
}
