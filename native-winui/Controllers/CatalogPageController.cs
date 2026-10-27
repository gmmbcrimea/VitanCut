using Microsoft.UI.Xaml.Controls;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Coordinates the counterparty list and the selected counterparty's catalog products.
/// </summary>
public sealed class CatalogPageController
{
    private readonly CatalogService _catalog;
    private readonly ListView _counterparties;
    private readonly ListView _products;
    private readonly TextBlock _title;
    private readonly TextBlock _subtitle;

    public CatalogPageController(
        CatalogService catalog,
        ListView counterparties,
        ListView products,
        TextBlock title,
        TextBlock subtitle)
    {
        _catalog = catalog;
        _counterparties = counterparties;
        _products = products;
        _title = title;
        _subtitle = subtitle;
    }

    public bool GroupMode { get; set; }
    public string SearchText { get; set; } = "";
    public string? SelectedCounterparty => GroupMode ? null : _counterparties.SelectedItem as string;
    public string SelectedGroup => (_counterparties.SelectedItem as string) == UiText.Get("Без группы") ? "" : _counterparties.SelectedItem as string ?? "";

    public void Refresh(string? selected = null)
    {
        selected ??= _counterparties.SelectedItem as string;
        var counterparties = GroupMode ? _catalog.GetProductGroups().Select(group => group.Length == 0 ? UiText.Get("Без группы") : group).ToList() : _catalog.GetCounterparties();
        _counterparties.ItemsSource = null;
        _counterparties.ItemsSource = counterparties;

        if (selected is not null && counterparties.Contains(selected))
            _counterparties.SelectedItem = selected;
        else
            _counterparties.SelectedIndex = counterparties.Count > 0 ? 0 : -1;

        RenderProducts();
    }

    public void SelectCounterparty(string name) => _counterparties.SelectedItem = name;

    public void RenderProducts()
    {
        using var measurement = App.UiPerformance.Measure(UiWork.Catalog);
        var counterparty = SelectedCounterparty;
        var searching = !string.IsNullOrWhiteSpace(SearchText);
        var products = _catalog.Browse(SearchText).Where(item => searching || (GroupMode
            ? string.Equals(item.Product.Group?.Trim() ?? "", SelectedGroup, StringComparison.CurrentCultureIgnoreCase)
            : item.Counterparty == counterparty)).ToList();

        _products.ItemsSource = null;
        _products.ItemsSource = products;
        _title.Text = searching ? UiText.Get("Результаты поиска") : GroupMode ? (SelectedGroup.Length == 0 ? UiText.Get("Без группы") : SelectedGroup)
            : string.IsNullOrWhiteSpace(counterparty) ? UiText.Get("Изделия заказчика") : counterparty;
        _subtitle.Text = products.Count == 0
            ? UiText.Get("Изделий пока нет")
            : UiText.Format($"{products.Count} изделие(й)");
    }
}
