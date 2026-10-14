using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;
using Windows.Foundation;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Keeps right-click selection consistent for every application list.
/// </summary>
public sealed class ListContextMenuController
{
    public ListView? CurrentList { get; private set; }
    private Func<ListView, object?, MenuFlyout>? _buildMenu;
    private readonly HashSet<ListView> _keyboardLists = [];

    public void Configure(Func<ListView, object?, MenuFlyout> buildMenu, params ListView[] lists)
    {
        _buildMenu = buildMenu;
        Configure(lists);
        foreach (var list in lists)
            if (_keyboardLists.Add(list)) list.KeyDown += KeyboardContextMenu;
    }

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
        if (!list.SelectedItems.Contains(data)) list.SelectedItem = data;

        if (item is not null)
        {
            list.Focus(FocusState.Programmatic);
        }

        buildMenu(list, data).ShowAt(list, args.GetPosition(list));
        args.Handled = true;
    }

    private void KeyboardContextMenu(object sender, KeyRoutedEventArgs args)
    {
        if (args.Handled || sender is not ListView list || _buildMenu is null) return;
        var shift = (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) != 0;
        if (args.Key != VirtualKey.Application && !(args.Key == VirtualKey.F10 && shift)) return;
        CurrentList = list;
        var data = list.SelectedItem;
        var container = data is null ? null : list.ContainerFromItem(data) as FrameworkElement;
        var position = container is null ? new Point(0, 0)
            : container.TransformToVisual(list).TransformPoint(new Point(0, container.ActualHeight));
        _buildMenu(list, data).ShowAt(list, position);
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
