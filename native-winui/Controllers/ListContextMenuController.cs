using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Keeps right-click selection consistent for every application list.
/// </summary>
public sealed class ListContextMenuController
{
    public ListView? CurrentList { get; private set; }

    public void Configure(params ListView[] lists)
    {
        foreach (var list in lists)
        {
            list.ContextFlyout = null;
        }
    }

    public void Show(object sender, RightTappedRoutedEventArgs args, Func<ListView, object?, MenuFlyout> buildMenu)
    {
        if (sender is not ListView list) return;

        CurrentList = list;
        var source = args.OriginalSource as DependencyObject;
        var item = FindAncestor<ListViewItem>(source);
        var data = (args.OriginalSource as FrameworkElement)?.DataContext ?? item?.Content ?? item?.DataContext ?? list.SelectedItem;
        list.SelectedItem = data;

        if (item is not null)
        {
            list.Focus(FocusState.Programmatic);
        }

        buildMenu(list, data).ShowAt(list, args.GetPosition(list));
        args.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = VisualTreeHelper.GetParent(node);
        }

        return null;
    }
}