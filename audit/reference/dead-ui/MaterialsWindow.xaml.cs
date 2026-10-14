using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Views;

public sealed partial class MaterialsWindow : Window
{
    private MaterialChoice? SelectedChoice => MaterialsList.SelectedItem as MaterialChoice;
    private bool _loading;

    public MaterialsWindow()
    {
        InitializeComponent();
        Title = "Vitan-Cut · Материалы";
        App.Preferences.Apply(this);
        Refresh();
    }

    private void Refresh(MaterialChoice? select = null)
    {
        _loading = true;
        MaterialsList.ItemsSource = null;
        MaterialsList.ItemsSource = App.State.MaterialChoices().ToList();
        if (select is not null)
        {
            MaterialsList.SelectedItem = MaterialsList.Items.OfType<MaterialChoice>()
                .FirstOrDefault(item => item.Type == select.Type && item.Material.Id == select.Material.Id);
        }
        if (MaterialsList.SelectedItem is null && MaterialsList.Items.Count > 0) MaterialsList.SelectedIndex = 0;
        Fill();
        _loading = false;
    }

    private void Fill()
    {
        var choice = SelectedChoice;
        TypeBox.Text = choice?.Type ?? "Прочее";
        NameBox.Text = choice?.Material.Name ?? "";
        CostBox.Value = choice?.Material.Cost ?? 0;
        SheetLengthBox.Value = choice?.Material.SheetLength ?? 0;
        SheetWidthBox.Value = choice?.Material.SheetWidth ?? 0;
        var unit = choice?.Material.Unit ?? "pc";
        UnitBox.SelectedIndex = unit == "m2" ? 0 : unit == "lm" ? 1 : 2;
    }

    private void AddClick(object sender, RoutedEventArgs e)
    {
        var type = Empty(TypeBox.Text, "Прочее");
        var material = new Material { Name = Empty(NameBox.Text, "Новый материал") };
        Apply(material);
        App.State.Database.Materials.TryAdd(type, []);
        App.State.Database.Materials[type].Add(material);
        App.State.Save();
        Refresh(new MaterialChoice(type, material));
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (SelectedChoice is not { } choice) return;
        var newType = Empty(TypeBox.Text, choice.Type);
        var material = choice.Material;
        Apply(material);
        if (newType != choice.Type)
        {
            App.State.Database.Materials[choice.Type].Remove(material);
            App.State.Database.Materials.TryAdd(newType, []);
            App.State.Database.Materials[newType].Add(material);
        }
        App.State.Save();
        Refresh(new MaterialChoice(newType, material));
    }

    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (SelectedChoice is not { } choice) return;
        App.State.Database.Materials[choice.Type].Remove(choice.Material);
        App.State.Save();
        Refresh();
    }

    private void MaterialsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        Fill();
    }

    private void Apply(Material material)
    {
        material.Name = Empty(NameBox.Text, "Новый материал");
        material.Cost = EditorValue(CostBox);
        material.Unit = UnitBox.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() ?? "pc" : "pc";
        material.SheetLength = material.Unit == "m2" ? EditorValue(SheetLengthBox) : 0;
        material.SheetWidth = material.Unit == "m2" ? EditorValue(SheetWidthBox) : 0;
    }

    private static string Empty(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    private static double EditorValue(NumberBox box) => NumberBoxInput.Read(box);
}
