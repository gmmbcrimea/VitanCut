using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Views;

public sealed partial class SettingsWindow : Window
{
    private bool _loading = true;

    public SettingsWindow()
    {
        InitializeComponent();
        AccentPicker.Populate(AccentBox);
        Title = "Vitan-Cut · Настройки";
        App.Preferences.Apply(this);
        Load();
    }

    private void Load()
    {
        _loading = true;
        ThemeBox.SelectedIndex = App.Preferences.Current.Theme switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0
        };
        AccentPicker.Select(AccentBox, App.Preferences.Current.AccentColor);
        _loading = false;
    }

    private void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeBox.SelectedItem is not ComboBoxItem item) return;
        App.Preferences.SetTheme(item.Tag?.ToString() ?? "auto");
        App.Preferences.Apply(this);
    }

    private void AccentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AccentBox.SelectedItem is not ComboBoxItem item) return;
        App.Preferences.SetAccent(item.Tag?.ToString() ?? "system");
        App.Preferences.Apply(this);
    }

    private void SettingsSectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString() ?? "appearance";
        AppearancePanel.Visibility = tag == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        CuttingPanel.Visibility = tag == "cutting" ? Visibility.Visible : Visibility.Collapsed;
        DetailingPanel.Visibility = tag == "detailing" ? Visibility.Visible : Visibility.Collapsed;
    }
}
