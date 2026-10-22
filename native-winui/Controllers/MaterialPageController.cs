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
    private readonly NumberBox _wasteAllowance;
    private readonly ComboBox _priceMode;
    private readonly NumberBox _rollLength;
    private string _pricingUnit = "";
    private readonly NumberBox _sheetLength;
    private readonly NumberBox _sheetWidth;
    private readonly CheckBox _texture;
    private readonly CheckBox _edgeBanding;
    private readonly ComboBox _sort;
    private MaterialChoice? _selectedMaterial;

    public MaterialPageController(MaterialService materials, ListView categories, ListView items, ListView groupedItems,
        UIElement listEmpty, UIElement parametersEmpty, UIElement parameters, TextBox type, TextBox name,
        ComboBox unit, NumberBox cost, NumberBox wasteAllowance, ComboBox priceMode, NumberBox rollLength, NumberBox sheetLength, NumberBox sheetWidth, CheckBox texture, CheckBox edgeBanding, ComboBox sort)
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
        _wasteAllowance = wasteAllowance;
        _priceMode = priceMode;
        _rollLength = rollLength;
        _sheetLength = sheetLength;
        _sheetWidth = sheetWidth;
        _texture = texture;
        _edgeBanding = edgeBanding;
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
        _wasteAllowance.Value = choice?.Material.WasteAllowancePercent ?? 0;
        _sheetLength.Value = choice?.Material.SheetLength ?? 0;
        _sheetWidth.Value = choice?.Material.SheetWidth ?? 0;
        _texture.IsChecked = choice?.Material.TextureDirection ?? false;
        _edgeBanding.IsChecked = choice?.Material.EdgeBandingEnabled ?? false;
        var unit = choice?.Material.Unit ?? "pc";
        _unit.SelectedIndex = unit == "m2" ? 0 : unit == "lm" ? 1 : 2;
        _parametersEmpty.Visibility = choice is null ? Visibility.Visible : Visibility.Collapsed;
        _parameters.Visibility = choice is null ? Visibility.Collapsed : Visibility.Visible;
        if (choice is null && SelectedCategory is { } category) _type.Text = category;
        UpdateSheetFields();
        _rollLength.Value = choice?.Material.RollLength ?? 0;
        _priceMode.SelectedItem = _priceMode.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == (choice?.Material.PriceMode ?? "unit"));
        if (_priceMode.SelectedItem is null && _priceMode.Items.Count > 0) _priceMode.SelectedIndex = 0;
        UpdatePriceModeFields();
    }

    public void ApplyEditor(Material material)
    {
        material.Name = string.IsNullOrWhiteSpace(_name.Text) ? "Новый материал" : _name.Text.Trim();
        material.Cost = EditorValue(_cost);
        material.WasteAllowancePercent = (_unit.SelectedItem as ComboBoxItem)?.Tag?.ToString() is "m2" or "lm" ? EditorValue(_wasteAllowance) : 0;
        material.Unit = _unit.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() ?? "pc" : "pc";
        material.PriceMode = material.Unit == "pc" ? "unit" : (_priceMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "unit";
        material.RollLength = material.Unit == "lm" ? EditorValue(_rollLength) : 0;
        material.SheetLength = material.Unit == "m2" ? EditorValue(_sheetLength) : 0;
        material.SheetWidth = material.Unit == "m2" ? EditorValue(_sheetWidth) : 0;
        material.TextureDirection = material.Unit == "m2" && _texture.IsChecked == true;
        material.Texture = SelectedMaterial?.Material.Texture ?? string.Empty;
        material.EdgeBandingEnabled = _edgeBanding.IsChecked == true;
    }

    public void UpdateSheetFields()
    {
        var isSheet = _unit.SelectedItem is ComboBoxItem item && item.Tag?.ToString() == "m2";
        var visibility = isSheet ? Visibility.Visible : Visibility.Collapsed;
        _sheetLength.Visibility = visibility;
        _sheetWidth.Visibility = visibility;
        _texture.Visibility = visibility;
        _wasteAllowance.Visibility = (_unit.SelectedItem as ComboBoxItem)?.Tag?.ToString() is "m2" or "lm" ? Visibility.Visible : Visibility.Collapsed;
        UpdatePricingFields();
    }

    private void UpdatePricingFields()
    {
        var unit = (_unit.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "pc";
        if (_pricingUnit != unit)
        {
            _pricingUnit = unit;
            _priceMode.Items.Clear();
            _priceMode.Items.Add(new ComboBoxItem { Content = unit == "m2" ? UiText.Get("За квадратный метр") : unit == "lm" ? UiText.Get("За погонный метр") : UiText.Get("За штуку"), Tag = "unit" });
            if (unit == "m2") _priceMode.Items.Add(new ComboBoxItem { Content = UiText.Get("За лист"), Tag = "sheet" });
            if (unit == "lm") _priceMode.Items.Add(new ComboBoxItem { Content = UiText.Get("За бухту"), Tag = "roll" });
            _priceMode.SelectedIndex = 0;
        }
        _priceMode.Visibility = unit == "pc" ? Visibility.Collapsed : Visibility.Visible;
        UpdatePriceModeFields();
    }

    public void UpdatePriceModeFields()
    {
        var mode = (_priceMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "unit";
        _rollLength.Visibility = _pricingUnit == "lm" && mode == "roll" ? Visibility.Visible : Visibility.Collapsed;
        _cost.Header = mode == "sheet" ? UiText.Get("Цена за лист") : mode == "roll" ? UiText.Get("Цена за бухту") :
            _pricingUnit == "m2" ? UiText.Get("Цена за м²") : _pricingUnit == "lm" ? UiText.Get("Цена за погонный метр") : UiText.Get("Цена за единицу");
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
