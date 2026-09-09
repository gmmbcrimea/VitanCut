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

    public string? SelectedCounterparty => _counterparties.SelectedItem as string;

    public void Refresh(string? selected = null)
    {
        selected ??= SelectedCounterparty;
        var counterparties = _catalog.GetCounterparties();
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
        var counterparty = SelectedCounterparty;
        var products = string.IsNullOrWhiteSpace(counterparty)
            ? new List<CustomerProductItem>()
            : _catalog.GetProducts(counterparty)
                .OrderBy(product => product.Name)
                .Select(product => new CustomerProductItem(counterparty, null, product))
                .ToList();

        _products.ItemsSource = null;
        _products.ItemsSource = products;
        _title.Text = string.IsNullOrWhiteSpace(counterparty) ? "Изделия заказчика" : counterparty;
        _subtitle.Text = products.Count == 0
            ? "Изделий у этого заказчика пока нет."
            : $"{products.Count} изделие(й)";
    }
}