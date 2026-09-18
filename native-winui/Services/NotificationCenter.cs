using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

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
    private readonly Flyout _historyFlyout;
    private readonly StackPanel _historyList;

    public NotificationOverlay()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        IsHitTestVisible = true;

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(16)
        };

        _title = new TextBlock { FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        _message = new TextBlock { Opacity = 0.82, TextWrapping = TextWrapping.Wrap, MaxWidth = 340 };
        var body = new StackPanel { Spacing = 3 };
        body.Children.Add(_title);
        body.Children.Add(_message);
        _toast = new Border
        {
            Padding = new Thickness(14, 11, 14, 11),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["PanelSurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["PanelStrokeBrush"],
            BorderThickness = new Thickness(1),
            Child = body,
            MaxWidth = 390,
            Opacity = 0,
            Visibility = Visibility.Collapsed
        };

        _historyButton = new Button
        {
            Content = new FontIcon { Glyph = "\uE7F4", FontSize = 17 },
            Width = 42,
            Height = 42,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(_historyButton, "Центр уведомлений");

        _historyList = new StackPanel { Spacing = 10 };
        _historyFlyout = new Flyout
        {
            Placement = FlyoutPlacementMode.TopEdgeAlignedRight,
            ShouldConstrainToRootBounds = true,
            Content = new Border
            {
                Width = 390,
                MaxHeight = 460,
                Padding = new Thickness(16),
                CornerRadius = new CornerRadius(8),
                Background = (Brush)Application.Current.Resources["PanelSurfaceBrush"],
                BorderBrush = (Brush)Application.Current.Resources["PanelStrokeBrush"],
                BorderThickness = new Thickness(1),
                Child = new ScrollViewer
                {
                    Content = _historyList,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                }
            }
        };
        _historyButton.Click += (_, _) =>
        {
            BuildHistoryList();
            if (_historyFlyout.IsOpen) _historyFlyout.Hide();
            else _historyFlyout.ShowAt(_historyButton);
        };

        stack.Children.Add(_toast);
        stack.Children.Add(_historyButton);
        Children.Add(stack);

        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(5);
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => HideToast();
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
            AnimateToast(0, 1, null);
            _timer.Start();
        });
    }

    private void HideToast() => AnimateToast(1, 0, () => _toast.Visibility = Visibility.Collapsed);

    private void AnimateToast(double from, double to, Action? completed)
    {
        var storyboard = new Storyboard();
        var animation = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(180)),
            EnableDependentAnimation = true
        };
        animation.Completed += (_, _) => completed?.Invoke();
        Storyboard.SetTarget(animation, _toast);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }

    private void BuildHistoryList()
    {
        _historyList.Children.Clear();
        var items = NotificationCenter.History;
        if (items.Count == 0)
        {
            _historyList.Children.Add(new TextBlock { Text = "Уведомлений пока нет.", Opacity = 0.8 });
            return;
        }

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var entry = new StackPanel { Spacing = 3 };
            entry.Children.Add(new TextBlock { Text = $"{item.CreatedAt:HH:mm}  {item.Title}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            entry.Children.Add(new TextBlock { Text = item.Message, TextWrapping = TextWrapping.Wrap, Opacity = 0.82 });
            _historyList.Children.Add(entry);
            if (index < items.Count - 1)
                _historyList.Children.Add(new Border { Height = 1, Background = (Brush)Application.Current.Resources["PanelStrokeBrush"] });
        }
    }
}
