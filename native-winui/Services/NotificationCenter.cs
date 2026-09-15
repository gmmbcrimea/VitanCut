using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VitanCut.WinUI.Services;

public sealed record AppNotification(string Title, string Message, DateTime CreatedAt);

public static class NotificationCenter
{
    private static readonly List<AppNotification> HistoryItems = [];
    public static event Action<AppNotification>? Published;
    public static IReadOnlyList<AppNotification> History => HistoryItems.ToArray();

    public static void Publish(string title, string message)
    {
        var item = new AppNotification(title, message, DateTime.Now);
        HistoryItems.Insert(0, item);
        if (HistoryItems.Count > 10) HistoryItems.RemoveAt(HistoryItems.Count - 1);
        Published?.Invoke(item);
    }
}

public sealed class NotificationOverlay : Grid
{
    private readonly Button _historyButton;
    private readonly Border _toast;
    private readonly TextBlock _title;
    private readonly TextBlock _message;
    private readonly DispatcherQueueTimer _timer;

    public NotificationOverlay()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16) };
        _title = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        _message = new TextBlock { Opacity = 0.82, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 };
        var body = new StackPanel { Spacing = 3 };
        body.Children.Add(_title);
        body.Children.Add(_message);
        _toast = new Border { Padding = new Thickness(14, 11, 14, 11), CornerRadius = new CornerRadius(6), Background = (Brush)Application.Current.Resources["PanelSurfaceBrush"], BorderBrush = (Brush)Application.Current.Resources["PanelStrokeBrush"], BorderThickness = new Thickness(1), Child = body, MaxWidth = 390, Visibility = Visibility.Collapsed };
        _historyButton = new Button { Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = "\uE7F4", FontSize = 15 }, new TextBlock { Text = "Уведомления" } } }, HorizontalAlignment = HorizontalAlignment.Right, IsHitTestVisible = true };
        _historyButton.Click += async (_, _) => await ShowHistoryAsync();
        stack.Children.Add(_toast);
        stack.Children.Add(_historyButton);
        Children.Add(stack);
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(10);
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => _toast.Visibility = Visibility.Collapsed;
        NotificationCenter.Published += OnPublished;
        Unloaded += (_, _) => NotificationCenter.Published -= OnPublished;
    }

    private void OnPublished(AppNotification item)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _title.Text = item.Title;
            _message.Text = item.Message;
            _toast.Visibility = Visibility.Visible;
            _timer.Stop();
            _timer.Start();
        });
    }

    private async Task ShowHistoryAsync()
    {
        var items = NotificationCenter.History;
        var list = new StackPanel { Spacing = 10 };
        if (items.Count == 0) list.Children.Add(new TextBlock { Text = "Уведомлений пока нет." });
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var entry = new StackPanel { Spacing = 2 };
            entry.Children.Add(new TextBlock { Text = $"{item.CreatedAt:HH:mm}  {item.Title}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            entry.Children.Add(new TextBlock { Text = item.Message, TextWrapping = TextWrapping.Wrap, Opacity = 0.82 });
            list.Children.Add(entry);
            if (index < items.Count - 1) list.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.Resources["PanelStrokeBrush"] });
        }
        var dialog = new ContentDialog { XamlRoot = XamlRoot, Title = "Последние уведомления", Content = new ScrollViewer { Content = list, MaxHeight = 460, Width = 420 }, CloseButtonText = "Закрыть", DefaultButton = ContentDialogButton.Close };
        await dialog.ShowAsync();
    }
}
