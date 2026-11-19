using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;
using VitanCut.WinUI.Controls;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Coordinates the counterparty list and the selected counterparty's catalog products.
/// </summary>
public sealed class CatalogPageController
{
    private readonly CatalogService _catalog;
    private readonly ListView _counterparties;
    private readonly ListView _products;
    private readonly CounterpartyLabel _title;
    private readonly TextBlock _subtitle;
    private readonly TextBlock _sidebarCount;
    private readonly UiRequestGate _restoreRequests = new();

    public CatalogPageController(
        CatalogService catalog,
        ListView counterparties,
        ListView products,
        CounterpartyLabel title,
        TextBlock subtitle,
        TextBlock sidebarCount)
    {
        _catalog = catalog;
        _counterparties = counterparties;
        _products = products;
        _title = title;
        _subtitle = subtitle;
        _sidebarCount = sidebarCount;
        _products.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => _restoreRequests.Begin()), true);
        _products.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, _) => _restoreRequests.Begin()), true);
        _products.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, _) => _restoreRequests.Begin()), true);
    }

    public bool GroupMode { get; set; }
    public string SearchText { get; set; } = "";
    public string? SelectedCounterparty => GroupMode ? null : _counterparties.SelectedItem as string;
    public string SelectedGroup => (_counterparties.SelectedItem as string) == UiText.Get("Без группы") ? "" : _counterparties.SelectedItem as string ?? "";

    public void Refresh(string? selected = null)
    {
        selected ??= _counterparties.SelectedItem as string;
        var counterparties = GroupMode ? _catalog.GetProductGroups().Select(group => group.Length == 0 ? UiText.Get("Без группы") : group).ToList() : _catalog.GetCounterparties();
        _sidebarCount.Text = GroupMode ? UiText.Format($"Разделов: {counterparties.Count}") : UiText.Format($"Заказчиков: {counterparties.Count}");
        _counterparties.ItemsSource = null;
        _counterparties.ItemsSource = counterparties;

        if (selected is not null && counterparties.Contains(selected))
            _counterparties.SelectedItem = selected;
        else
            _counterparties.SelectedIndex = counterparties.Count > 0 ? 0 : -1;

        RenderProducts();
    }

    public void SelectCounterparty(string name) => _counterparties.SelectedItem = name;

    public void RenderProducts() => RenderProducts(preserveViewport: false);

    public void RefreshSavedProducts() => RenderProducts(preserveViewport: true);

    private void RenderProducts(bool preserveViewport)
    {
        using var measurement = App.UiPerformance.Measure(UiWork.Catalog);
        var request = _restoreRequests.Begin();
        var viewer = preserveViewport ? FindScrollViewer(_products) : null;
        var horizontal = viewer?.HorizontalOffset ?? 0;
        var vertical = viewer?.VerticalOffset ?? 0;
        var selectedKeys = _products.SelectedItems.OfType<CustomerProductItem>().Select(CatalogListUpdates.Key).ToHashSet();
        var counterparty = SelectedCounterparty;
        var searching = !string.IsNullOrWhiteSpace(SearchText);
        var products = _catalog.Browse(SearchText).Where(item => searching || (GroupMode
            ? string.Equals(item.Product.Group?.Trim() ?? "", SelectedGroup, StringComparison.CurrentCultureIgnoreCase)
            : item.Counterparty == counterparty)).ToList();

        if (preserveViewport && _products.ItemsSource is ObservableCollection<CustomerProductItem> items)
        {
            CatalogListUpdates.Apply(items, products);
            foreach (var item in items.Where(item => selectedKeys.Contains(CatalogListUpdates.Key(item))))
                if (!_products.SelectedItems.Contains(item)) _products.SelectedItems.Add(item);
        }
        else _products.ItemsSource = new ObservableCollection<CustomerProductItem>(products);
        _title.Text = searching ? UiText.Get("Результаты поиска") : GroupMode ? (SelectedGroup.Length == 0 ? UiText.Get("Без группы") : SelectedGroup)
            : string.IsNullOrWhiteSpace(counterparty) ? UiText.Get("Изделия заказчика") : counterparty;
        _title.ShowLogo = !searching && !GroupMode && !string.IsNullOrWhiteSpace(counterparty);
        _title.Counterparty = counterparty;
        ToolTipService.SetToolTip(_title, _title.Text);
        _subtitle.Text = products.Count == 0
            ? UiText.Get("Изделий пока нет")
            : UiText.Format($"{products.Count} изделие(й)");
        if (viewer is null) return;
        void Restore()
        {
            if (!_restoreRequests.IsCurrent(request) || !viewer.IsLoaded) return;
            _products.UpdateLayout();
            viewer.ChangeView(horizontal, Math.Min(vertical, viewer.ScrollableHeight), null, disableAnimation: true);
        }
        Restore();
        _products.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, Restore);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer) return viewer;
            if (FindScrollViewer(child) is { } nested) return nested;
        }
        return null;
    }
}
