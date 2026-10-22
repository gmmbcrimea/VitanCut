using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Views;

public sealed class RecoveryWindow : Window
{
    private readonly Func<RecoveryWindow, Task> _retry;
    private readonly Func<Task<bool>> _rollback;
    private readonly TextBox _errorDetails;
    private readonly TextBlock _rollbackHint;
    private readonly Button _rollbackButton;
    private readonly Button _retryButton;
    private bool _busy;

    public RecoveryWindow(Exception error, Func<RecoveryWindow, Task> retry, Func<Task<bool>> rollback, bool canRollback)
    {
        _retry = retry;
        _rollback = rollback;
        Title = UiText.Get("Восстановление VitanCut");
        SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();

        var layout = new Grid { Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PanelSurfaceBrush"], Padding = new Thickness(24) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var logo = new Image { Width = 48, Height = 48, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform, Source = new BitmapImage(new Uri("ms-appx:///Assets/logo.png")) };
        var title = new TextBlock { Text = UiText.Get("Проверка запуска"), FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var subtitle = new TextBlock { Text = UiText.Get("VitanCut не удалось запустить обычным способом"), Opacity = 0.72 };
        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        titleStack.Children.Add(title);
        titleStack.Children.Add(subtitle);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(logo);
        header.Children.Add(titleStack);
        layout.Children.Add(header);

        var description = new TextBlock { Text = UiText.Get("Описание ошибки"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 8) };
        Grid.SetRow(description, 1);
        layout.Children.Add(description);

        _errorDetails = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120 };
        Grid.SetRow(_errorDetails, 2);
        layout.Children.Add(_errorDetails);

        _rollbackHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.72, Margin = new Thickness(0, 12, 0, 16) };
        Grid.SetRow(_rollbackHint, 3);
        layout.Children.Add(_rollbackHint);

        _rollbackButton = new Button { Content = UiText.Get("Откатить обновление") };
        _rollbackButton.Click += RollbackClick;
        _retryButton = new Button { Content = UiText.Get("Попробовать запустить снова") };
        _retryButton.Click += RetryClick;
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 10 };
        actions.Children.Add(_rollbackButton);
        actions.Children.Add(_retryButton);
        Grid.SetRow(actions, 4);
        layout.Children.Add(actions);
        Content = layout;
        App.Preferences.Apply(this);

        ShowError(error,canRollback);
        AppWindow.Resize(new Windows.Graphics.SizeInt32(620, 440));
    }

    public void ShowError(Exception error, bool canRollback)
    {
        _errorDetails.Text = UiText.Get(Describe(error));
        _rollbackButton.IsEnabled = canRollback && !_busy;
        _rollbackHint.Text = canRollback
            ? UiText.Get("Можно восстановить резервную копию версии, сохранённую перед последним обновлением.")
            : UiText.Get("Резервная копия предыдущей версии не найдена. Повторите запуск после проверки причины ошибки.");
    }

    private async void RetryClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        _retryButton.IsEnabled = false;
        _rollbackButton.IsEnabled = false;
        _retryButton.Content = UiText.Get("Запускаем...");
        await _retry(this);
        _busy = false;
        _retryButton.Content = UiText.Get("Попробовать запустить снова");
        _retryButton.IsEnabled = true;
        _rollbackButton.IsEnabled = App.Updates.CanRollback;
    }

    private async void RollbackClick(object sender, RoutedEventArgs e)
    {
        if (_busy || !App.Updates.CanRollback) return;
        _busy = true;
        _retryButton.IsEnabled = false;
        _rollbackButton.IsEnabled = false;
        _rollbackButton.Content = UiText.Get("Восстанавливаем...");
        var started = await _rollback();
        if (started)
        {
            Close();
            return;
        }
        _rollbackHint.Text = UiText.Get("Не удалось подготовить откат. Проверьте, что папка приложения доступна для записи.");
        _busy = false;
        _retryButton.IsEnabled = true;
        _rollbackButton.Content = UiText.Get("Откатить обновление");
        _rollbackButton.IsEnabled = App.Updates.CanRollback;
    }

    private static string Describe(Exception error)
    {
        var details = new List<string>();
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            if (!string.IsNullOrWhiteSpace(current.Message) && !details.Contains(current.Message))
                details.Add(current.Message);
        }
        return string.Join(Environment.NewLine + Environment.NewLine, details);
    }
}
