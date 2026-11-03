using GHubTray.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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
            PlaceholderText = "30"
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_refreshIntervalBox, "更新間隔（秒）");
        _startWithWindowsToggle = new ToggleSwitch
        {
            Header = "Windows起動時に開始",
            OnContent = "オン",
            OffContent = "オフ",
            IsOn = initialSettings.StartWithWindows
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_startWithWindowsToggle, "Windows起動時に開始");
        _errorText = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
            Foreground = GetResourceBrush("SystemFillColorCriticalBrush")
        };

        var root = new Grid
        {
            Width = 420,
            Padding = new Thickness(28),
            RowSpacing = 18
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "GHubTray 設定",
            FontSize = 22,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        root.Children.Add(title);

        var intervalPanel = new StackPanel { Spacing = 6 };
        intervalPanel.Children.Add(new TextBlock
        {
            Text = "更新間隔（秒）",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        intervalPanel.Children.Add(_refreshIntervalBox);
        intervalPanel.Children.Add(new TextBlock
        {
            Text = "10〜3600秒。短くすると確認回数が増えます。",
            FontSize = 12,
            Foreground = GetResourceBrush("TextFillColorSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetRow(intervalPanel, 1);
        root.Children.Add(intervalPanel);

        Grid.SetRow(_startWithWindowsToggle, 2);
        root.Children.Add(_startWithWindowsToggle);

        Grid.SetRow(_errorText, 4);
        root.Children.Add(_errorText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 10
        };
        var cancelButton = new Button { Content = "キャンセル" };
        cancelButton.Click += CancelButton_Click;
        var saveButton = new Button { Content = "保存" };
        saveButton.Click += SaveButton_Click;
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);
        Grid.SetRow(buttons, 5);
        root.Children.Add(buttons);

        Content = root;
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
