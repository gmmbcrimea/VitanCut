using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Views;

public sealed partial class ProjectWindow : Window
{
    private readonly Project _project;
    private Product? SelectedProduct => ProductsList.SelectedItem as Product;
    private Detail? SelectedDetail => DetailsList.SelectedItem as Detail;
    private bool _loading;

    public ProjectWindow(Project project)
    {
        _project = project;
        InitializeComponent();
        Title = $"Vitan-Cut · {_project.Name}";
        ProjectTitle.Text = _project.Name;
        ProjectSubtitle.Text = $"{_project.Counterparty} · {_project.Address}";
        App.Preferences.Apply(this);
        Refresh();
    }

    private void Refresh(Product? selectProduct = null, Detail? selectDetail = null)
    {
        _loading = true;
        var product = selectProduct ?? SelectedProduct;
        var detail = selectDetail ?? SelectedDetail;

        ProductsList.ItemsSource = null;
        ProductsList.ItemsSource = _project.Products;
        ProductsList.SelectedIndex = product is null ? (_project.Products.Count > 0 ? 0 : -1) : _project.Products.IndexOf(product);

        FillProduct();
        if (SelectedProduct is { } current)
        {
            DetailsList.ItemsSource = current.Details;
            DetailsList.SelectedIndex = detail is null ? (current.Details.Count > 0 ? 0 : -1) : current.Details.IndexOf(detail);
        }
        else
        {
            DetailsList.ItemsSource = null;
        }
        FillDetail();

        var totals = Calculator.Project(App.State, _project);
        SummaryBar.Title = $"{Calculator.Number(totals.Area)} м² · {Calculator.Money(totals.Cost)}";
        SummaryBar.Message = $"{_project.Products.Count} изд. · {_project.Counterparty}";
        _loading = false;
    }

    private void FillProduct()
    {
        var product = SelectedProduct;
        ProductNameBox.Text = product?.Name ?? "";
        ProductLengthBox.Value = product?.Length ?? 0;
        ProductDepthBox.Value = product?.Depth ?? 0;
        ProductHeightBox.Value = product?.Height ?? 0;
        ProductQtyBox.Value = product?.Qty ?? 1;
    }

    private void FillDetail()
    {
        var detail = SelectedDetail;
        DetailNameBox.Text = detail?.Name ?? "";
        DetailLengthBox.Text = detail?.LengthExpr ?? "";
        DetailWidthBox.Text = detail?.WidthExpr ?? "";
        DetailQtyBox.Value = detail?.Qty ?? 1;
        MaterialBox.ItemsSource = App.State.MaterialChoices().ToList();
        MaterialBox.SelectedItem = detail is null
            ? null
            : MaterialBox.Items.OfType<MaterialChoice>().FirstOrDefault(item => item.Type == detail.Type && item.Material.Id == detail.MaterialId);
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        SaveProduct();
        SaveDetail();
        App.State.Save();
        Refresh(SelectedProduct, SelectedDetail);
        SummaryBar.Severity = InfoBarSeverity.Success;
        SummaryBar.Message = "Изменения сохранены";
    }

    private void AddProductClick(object sender, RoutedEventArgs e)
    {
        var product = new Product();
        _project.Products.Add(product);
        TouchProject();
        App.State.Save();
        Refresh(product);
    }

    private void DeleteProductClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProduct is not { } product) return;
        _project.Products.Remove(product);
        TouchProject();
        App.State.Save();
        Refresh();
    }

    private void AddDetailClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProduct is not { } product) return;
        var choice = App.State.MaterialChoices().FirstOrDefault();
        var detail = new Detail
        {
            Type = choice?.Type ?? "ЛДСП",
            MaterialId = choice?.Material.Id ?? ""
        };
        product.Details.Add(detail);
        TouchProject();
        App.State.Save();
        Refresh(product, detail);
    }

    private void DeleteDetailClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProduct is not { } product || SelectedDetail is not { } detail) return;
        product.Details.Remove(detail);
        TouchProject();
        App.State.Save();
        Refresh(product);
    }

    private void ProductsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        FillProduct();
        Refresh(SelectedProduct);
    }

    private void DetailsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        FillDetail();
    }

    private void SaveProduct()
    {
        if (SelectedProduct is not { } product) return;
        product.Name = Empty(ProductNameBox.Text, "Новое изделие");
        product.Length = ProductLengthBox.Value;
        product.Depth = ProductDepthBox.Value;
        product.Height = ProductHeightBox.Value;
        product.Qty = ProductQtyBox.Value <= 0 ? 1 : ProductQtyBox.Value;
        TouchProject();
    }

    private void SaveDetail()
    {
        if (SelectedDetail is not { } detail) return;
        detail.Name = Empty(DetailNameBox.Text, "Новая деталь");
        detail.LengthExpr = Empty(DetailLengthBox.Text, "0");
        detail.WidthExpr = Empty(DetailWidthBox.Text, "0");
        detail.Qty = DetailQtyBox.Value <= 0 ? 1 : DetailQtyBox.Value;
        if (MaterialBox.SelectedItem is MaterialChoice choice)
        {
            detail.Type = choice.Type;
            detail.MaterialId = choice.Material.Id;
        }
        TouchProject();
    }

    private void TouchProject()
    {
        _project.UpdatedAt = DateTimeOffset.Now;
    }

    private static string Empty(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
