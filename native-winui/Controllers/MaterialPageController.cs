using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Owns the material-page selection and editor rendering; persistence stays in MaterialService.
/// </summary>
public sealed class MaterialPageController
{
    private readonly MaterialService _materials;
    private readonly ListView _categories;
    private readonly ListView _items;
    private readonly UIElement _listEmpty;
    private readonly UIElement _parametersEmpty;
    private readonly UIElement _parameters;
    private readonly TextBox _type;
    private readonly TextBox _name;
    private readonly ComboBox _unit;
    private readonly NumberBox _cost;
    private readonly NumberBox _sheetLength;
    private readonly NumberBox _sheetWidth;
    private readonly CheckBox _texture;

    public MaterialPageController(
        MaterialService materials,
        ListView categories,
        ListView items,
        UIElement listEmpty,
        UIElement parametersEmpty,
        UIElement parameters,
        TextBox type,
        TextBox name,
        ComboBox unit,
        NumberBox cost,
        NumberBox sheetLength,
        NumberBox sheetWidth,
        CheckBox texture)
    {
        _materials = materials;
        _categories = categories;
        _items = items;
        _listEmpty = listEmpty;
        _parametersEmpty = parametersEmpty;
        _parameters = parameters;
        _type = type;
        _name = name;
        _unit = unit;
        _cost = cost;
        _sheetLength = sheetLength;
        _sheetWidth = sheetWidth;
        _texture = texture;
    }

    public string? SelectedCategory => _categories.SelectedItem as string;
    public MaterialChoice? SelectedMaterial => _items.SelectedItem as MaterialChoice;
    public string EditorName => string.IsNullOrWhiteSpace(_name.Text) ? "Новый материал" : _name.Text.Trim();
    public string EditorGroup => string.IsNullOrWhiteSpace(_type.Text) ? "Прочее" : _type.Text.Trim();

    public void Refresh(MaterialChoice? select = null)
    {
        var category = select?.Type ?? SelectedCategory;
        _categories.ItemsSource = null;
        _categories.ItemsSource = _materials.GetGroups().ToList();
        if (category is not null) _categories.SelectedItem = category;
        if (_categories.SelectedItem is null && _categories.Items.Count > 0) _categories.SelectedIndex = 0;

        BindItems(select);
        RenderSelected();
    }

    public void RefreshCurrentCategory()
    {
        BindItems(select: null);
        RenderSelected();
    }

    public void OnCategoryChanged()
    {
        BindItems(select: null);
        RenderSelected();
    }

    public void RenderSelected()
    {
        var choice = SelectedMaterial;
        _type.Text = choice?.Type ?? "Прочее";
        _name.Text = choice?.Material.Name ?? string.Empty;
        _cost.Value = choice?.Material.Cost ?? 0;
        _sheetLength.Value = choice?.Material.SheetLength ?? 0;
        _sheetWidth.Value = choice?.Material.SheetWidth ?? 0;
        _texture.IsChecked = choice?.Material.TextureDirection ?? false;
        var unit = choice?.Material.Unit ?? "pc";
        _unit.SelectedIndex = unit == "m2" ? 0 : unit == "lm" ? 1 : 2;
        _parametersEmpty.Visibility = choice is null ? Visibility.Visible : Visibility.Collapsed;
        _parameters.Visibility = choice is null ? Visibility.Collapsed : Visibility.Visible;
        if (choice is null && SelectedCategory is { } category) _type.Text = category;
        UpdateSheetFields();
    }

    public void ApplyEditor(Material material)
    {
        material.Name = string.IsNullOrWhiteSpace(_name.Text) ? "Новый материал" : _name.Text.Trim();
        material.Cost = EditorValue(_cost);
        material.Unit = _unit.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() ?? "pc" : "pc";
        material.SheetLength = material.Unit == "m2" ? EditorValue(_sheetLength) : 0;
        material.SheetWidth = material.Unit == "m2" ? EditorValue(_sheetWidth) : 0;
        material.TextureDirection = material.Unit == "m2" && _texture.IsChecked == true;
    }

    public void UpdateSheetFields()
    {
        var isSheet = _unit.SelectedItem is ComboBoxItem item && item.Tag?.ToString() == "m2";
        var visibility = isSheet ? Visibility.Visible : Visibility.Collapsed;
        _sheetLength.Visibility = visibility;
        _sheetWidth.Visibility = visibility;
        _texture.Visibility = visibility;
    }

    private void BindItems(MaterialChoice? select)
    {
        _items.ItemsSource = null;
        _items.ItemsSource = _materials.GetChoices(SelectedCategory).ToList();
        if (select is not null)
        {
            _items.SelectedItem = _items.Items.OfType<MaterialChoice>()
                .FirstOrDefault(item => item.Type == select.Type && item.Material.Id == select.Material.Id);
        }
        else
        {
            _items.SelectedIndex = -1;
        }
        _listEmpty.Visibility = _items.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static double EditorValue(NumberBox box) => NumberBoxInput.Read(box);
}
