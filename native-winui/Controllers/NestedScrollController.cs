using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Transfers wheel input from a nested list to its parent scroll view at list boundaries.
/// </summary>
public sealed class NestedScrollController(ScrollViewer parent)
{
    public void Attach(params ListView[] lists)
    {
        foreach (var list in lists)
            list.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(OnPointerWheelChanged), true);
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs args)
    {
        if (sender is not ListView list) return;
        var child = FindDescendant<ScrollViewer>(list);
        var delta = args.GetCurrentPoint(list).Properties.MouseWheelDelta;

        if (child is null || child.ScrollableHeight <= 0 || ReachedBoundary(child, delta))
        {
            var nextOffset = Math.Clamp(parent.VerticalOffset - delta, 0, parent.ScrollableHeight);
            parent.ChangeView(null, nextOffset, null, true);
            args.Handled = true;
        }
    }

    private static bool ReachedBoundary(ScrollViewer viewer, int wheelDelta)
    {
        var atTop = viewer.VerticalOffset <= 0.5;
        var atBottom = viewer.VerticalOffset >= viewer.ScrollableHeight - 0.5;
        return wheelDelta < 0 ? atBottom : atTop;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }

        return null;
    }
}