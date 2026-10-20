using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Controllers;

public sealed class MaterialPageController
{
    private readonly MaterialService _materials;
    private readonly ListView _categories;
    private readonly ListView _items;
    private readonly ListView _groupedItems;
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
    private readonly ComboBox _sort;
    private MaterialChoice? _selectedMaterial;

    public MaterialPageController(MaterialService materials, ListView categories, ListView items, ListView groupedItems,
        UIElement listEmpty, UIElement parametersEmpty, UIElement parameters, TextBox type, TextBox name,
        ComboBox unit, NumberBox cost, NumberBox sheetLength, NumberBox sheetWidth, CheckBox texture, ComboBox sort)
    {
        _materials = materials;
        _categories = categories;
        _items = items;
        _groupedItems = groupedItems;
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
        _sort = sort;
    }

    public string? SelectedCategory => _categories.SelectedItem as string;
    public MaterialChoice? SelectedMaterial => _selectedMaterial;
    public IReadOnlyList<MaterialChoice> ChoicesInViewOrder => SortChoices(_materials.GetChoices(SelectedCategory));
    public string EditorName => string.IsNullOrWhiteSpace(_name.Text) ? "Новый материал" : _name.Text.Trim();
    public string EditorGroup => string.IsNullOrWhiteSpace(_type.Text) ? "Прочее" : _type.Text.Trim();

    public void Refresh(MaterialChoice? select = null)
    {
        var category = select?.Type ?? SelectedCategory;
        _categories.ItemsSource = null;
        _categories.ItemsSource = _materials.GetGroups().ToList();
        if (category is not null) _categories.SelectedItem = category;
        if (_categories.SelectedItem is null && _categories.Items.Count > 0) _categories.SelectedIndex = 0;
        _selectedMaterial = select;
        BindItems();
        RenderSelected();
    }

    public void RefreshCurrentCategory()
    {
        _selectedMaterial = null;
        BindItems();
        RenderSelected();
    }

    public void OnCategoryChanged()
    {
        _selectedMaterial = null;
        BindItems();
        RenderSelected();
    }

    public void RenderSelected()
    {
        var choice = SelectedMaterial;
        _type.Text = choice?.Type ?? SelectedCategory ?? "Прочее";
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
        material.Texture = SelectedMaterial?.Material.Texture ?? string.Empty;
    }

    public void UpdateSheetFields()
    {
        var isSheet = _unit.SelectedItem is ComboBoxItem item && item.Tag?.ToString() == "m2";
        var visibility = isSheet ? Visibility.Visible : Visibility.Collapsed;
        _sheetLength.Visibility = visibility;
        _sheetWidth.Visibility = visibility;
        _texture.Visibility = visibility;
    }

    private void BindItems()
    {
        using var measurement = App.UiPerformance.Measure(UiWork.Materials);
        var expansion = (_groupedItems.ItemsSource as IEnumerable<MaterialGroupView>)?
            .ToDictionary(group => group.Name, group => group.IsExpanded);
        _items.ItemsSource = null;
        _groupedItems.ItemsSource = null;
        var choices = ChoicesInViewOrder.ToList();
        var hasGroups = choices.Any(choice => !string.IsNullOrWhiteSpace(choice.Material.Group));
        _items.Visibility = hasGroups ? Visibility.Collapsed : Visibility.Visible;
        _groupedItems.Visibility = hasGroups ? Visibility.Visible : Visibility.Collapsed;
        if (hasGroups)
        {
            _groupedItems.ItemsSource = choices
                .GroupBy(choice => string.IsNullOrWhiteSpace(choice.Material.Group) ? "" : choice.Material.Group.Trim(), StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new MaterialGroupView(group.Key, group.ToList())
                {
                    IsExpanded = expansion?.GetValueOrDefault(group.Key, true) ?? true
                }).ToList();
        }
        else
        {
            _items.ItemsSource = choices;
        }
        _listEmpty.Visibility = choices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SelectMaterial(MaterialChoice choice)
    {
        _selectedMaterial = choice;
        RenderSelected();
    }

    private List<MaterialChoice> SortChoices(IEnumerable<MaterialChoice> choices) => (_sort.SelectedItem as ComboBoxItem)?.Tag?.ToString() switch
    {
        "name-desc" => choices.OrderByDescending(choice => choice.Material.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        "date" => choices.OrderByDescending(choice => choice.Material.CreatedAt).ThenBy(choice => choice.Material.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
        _ => choices.OrderBy(choice => choice.Material.Name, StringComparer.CurrentCultureIgnoreCase).ToList()
    };

    private static double EditorValue(NumberBox box) => NumberBoxInput.Read(box);
}

public sealed class MaterialGroupView(string name, IReadOnlyList<MaterialChoice> items)
{
    public bool IsExpanded { get; set; } = true;
    public Visibility GroupVisibility => Name.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    public Visibility PlainVisibility => Name.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    public string Name { get; } = name;
    public IReadOnlyList<MaterialChoice> Items { get; } = items;
}
