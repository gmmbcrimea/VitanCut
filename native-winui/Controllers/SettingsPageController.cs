using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Renders persisted application preferences and settings sections.
/// </summary>
public sealed class SettingsPageController
{
    private readonly Grid _contentHost;
    private readonly ComboBox _theme;
    private readonly ComboBox _accent;
    private readonly ToggleSwitch _compact;
    private readonly ToggleSwitch _animations;
    private readonly ComboBox _cutExportFormat;
    private readonly ComboBox _detailingExportFormat;
    private readonly NumberBox _cutTrim;
    private readonly NumberBox _cutGap;
    private readonly UIElement _appearance;
    private readonly UIElement _cutting;
    private readonly UIElement _detailing;
    private readonly UIElement _database;
    private readonly UIElement[] _pages;

    public SettingsPageController(
        Grid contentHost,
        ComboBox theme,
        ComboBox accent,
        ToggleSwitch compact,
        ToggleSwitch animations,
        ComboBox cutExportFormat,
        ComboBox detailingExportFormat,
        NumberBox cutTrim,
        NumberBox cutGap,
        UIElement appearance,
        UIElement cutting,
        UIElement detailing,
        UIElement database,
        params UIElement[] pages)
    {
        _contentHost = contentHost;
        _theme = theme;
        _accent = accent;
        AccentPicker.Populate(_accent);
        _compact = compact;
        _animations = animations;
        _cutExportFormat = cutExportFormat;
        _detailingExportFormat = detailingExportFormat;
        _cutTrim = cutTrim;
        _cutGap = cutGap;
        _appearance = appearance;
        _cutting = cutting;
        _detailing = detailing;
        _database = database;
        _pages = pages;
    }

    public void Load(AppPreferences preferences)
    {
        _theme.SelectedIndex = preferences.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        AccentPicker.Select(_accent, preferences.AccentColor);
        _compact.IsOn = preferences.CompactMode;
        _animations.IsOn = preferences.AnimationsEnabled;
        _cutExportFormat.SelectedIndex = preferences.CutExportFormat == "xlsx" ? 1 : 0;
        _detailingExportFormat.SelectedIndex = preferences.DetailingExportFormat == "xlsx" ? 1 : 0;
        _cutTrim.Value = preferences.CutTrim;
        _cutGap.Value = preferences.CutGap;
    }

    public void ApplyCompactMode(bool enabled) => _contentHost.Padding = enabled ? new Thickness(8, 4, 8, 8) : new Thickness(16, 8, 16, 16);

    public void ApplyAnimations(bool enabled)
    {
        foreach (var page in _pages)
            page.Transitions = enabled ? new TransitionCollection { new EntranceThemeTransition() } : new TransitionCollection();
    }

    public void CommitCutLayout(PreferencesService preferences) =>
        preferences.SetCutLayoutOptions(NumberBoxInput.Read(_cutTrim), NumberBoxInput.Read(_cutGap));

    public void ShowSection(string tag)
    {
        _appearance.Visibility = tag == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        _cutting.Visibility = tag == "cutting" ? Visibility.Visible : Visibility.Collapsed;
        _detailing.Visibility = tag == "detailing" ? Visibility.Visible : Visibility.Collapsed;
        _database.Visibility = tag == "database" ? Visibility.Visible : Visibility.Collapsed;
    }
}
