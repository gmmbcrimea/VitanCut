using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VitanCut.WinUI.Models;
using Windows.UI;

namespace VitanCut.WinUI.Services;

internal static class AccentPicker
{
    public static void Populate(ComboBox picker)
    {
        picker.Items.Clear();
        picker.MinWidth = 210;
        picker.MaxDropDownHeight = 360;
        foreach (var option in AccentPalette.Options)
        {
            var swatch = new Border
            {
                Width = 18, Height = 18, CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["PanelStrokeBrush"],
                Background = option.Id == "system" ? (Brush)Application.Current.Resources["AppAccentBrush"]
                    : new SolidColorBrush(Color.FromArgb(255, option.R, option.G, option.B))
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            content.Children.Add(swatch);
            content.Children.Add(new TextBlock { Text = option.Name, VerticalAlignment = VerticalAlignment.Center });
            var item = new ComboBoxItem { Tag = option.Id, Content = content };
            AutomationProperties.SetName(item, option.Name);
            picker.Items.Add(item);
        }
    }

    public static void Select(ComboBox picker, string id) => picker.SelectedItem = picker.Items
        .OfType<ComboBoxItem>().First(item => Equals(item.Tag, AccentPalette.Find(id).Id));
}
