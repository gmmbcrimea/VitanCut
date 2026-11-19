using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI;

public sealed partial class MainWindow
{
    private static void SelectOption(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == tag);

    private void LoadAppearanceOptions()
    {
        var preferences = App.Preferences.Current;
        MoreTransparencySwitch.IsOn = preferences.MoreTransparency;
        SelectOption(AnimationSpeedBox, InterfaceOptions.Speed(preferences.AnimationSpeed).ToString(CultureInfo.InvariantCulture));
        SelectOption(CursorRevealRadiusBox, InterfaceOptions.Radius(preferences.CursorRevealRadius).ToString(CultureInfo.InvariantCulture));
        SelectOption(CutFileNameFormatBox, InterfaceOptions.FileNameFormat(preferences.CutFileNameFormat));
        SelectOption(DetailingFileNameFormatBox, InterfaceOptions.FileNameFormat(preferences.DetailingFileNameFormat));
        ContrastCutLinesSwitch.IsOn = preferences.ContrastCutLines;
        RefreshAppearanceControlStates();
        RefreshRemainderColors();
    }

    private void RefreshAppearanceControlStates()
    {
        MoreTransparencySwitch.IsEnabled = App.Preferences.Current.AcrylicBackgroundEnabled;
        AnimationSpeedBox.IsEnabled = App.Preferences.Current.AnimationsEnabled;
        CursorRevealRadiusBox.IsEnabled = App.Preferences.Current.CursorRevealEnabled;
    }

    private void MoreTransparencyToggled(object sender, RoutedEventArgs args)
    {
        if (!_loadingSettings) App.Preferences.SetMoreTransparency(MoreTransparencySwitch.IsOn);
    }

    private void AnimationSpeedChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_loadingSettings || AnimationSpeedBox.SelectedItem is not ComboBoxItem { Tag: string value }) return;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
            App.Preferences.SetAnimationSpeed(speed);
    }

    private void CursorRevealRadiusChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_loadingSettings || CursorRevealRadiusBox.SelectedItem is not ComboBoxItem { Tag: string value }) return;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius))
            App.Preferences.SetCursorRevealRadius(radius);
    }

    private void CutFileNameFormatChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_loadingSettings && CutFileNameFormatBox.SelectedItem is ComboBoxItem { Tag: string format })
            App.Preferences.SetCutFileNameFormat(format);
    }

    private void DetailingFileNameFormatChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_loadingSettings && DetailingFileNameFormatBox.SelectedItem is ComboBoxItem { Tag: string format })
            App.Preferences.SetDetailingFileNameFormat(format);
    }

    private void RemainderColorClick(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: string color }) return;
        App.Preferences.SetRemainderColor(color);
        RefreshRemainderColors();
    }

    private void RefreshRemainderColors()
    {
        var preferences = App.Preferences.Current;
        var selected = InterfaceOptions.Remainder(preferences.CutPalette, preferences.RemainderColor);
        GreenRemainderButton.IsEnabled = preferences.CutPalette != "green";
        foreach (var button in new[] { GreenRemainderButton, RedRemainderButton })
        {
            button.BorderThickness = new Thickness(button.Tag?.ToString() == selected ? 3 : 1);
            button.BorderBrush = (Brush)Application.Current.Resources[button.Tag?.ToString() == selected ? "AppAccentBrush" : "PanelStrokeBrush"];
        }
    }

    private void ContrastCutLinesToggled(object sender, RoutedEventArgs args)
    {
        if (!_loadingSettings) App.Preferences.SetContrastCutLines(ContrastCutLinesSwitch.IsOn);
    }
}
