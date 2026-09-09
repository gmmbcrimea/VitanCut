using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Switches top-level pages and applies consistent top-navigation visual states.
/// </summary>
public sealed class NavigationController
{
    private readonly (string Tag, Grid Page, Button Button, bool IsSettings)[] _tabs;

    public NavigationController(params (string Tag, Grid Page, Button Button, bool IsSettings)[] tabs) => _tabs = tabs;

    public void Show(string tag, bool animationsEnabled)
    {
        var active = _tabs.FirstOrDefault(tab => tab.Tag == tag);
        if (active.Page is null) active = _tabs[0];

        foreach (var tab in _tabs)
        {
            var selected = tab.Tag == active.Tag;
            tab.Page.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            tab.Button.Style = (Style)Application.Current.Resources[selected
                ? tab.IsSettings ? "AccentSettingsTopNavButtonStyle" : "AccentTopNavButtonStyle"
                : tab.IsSettings ? "SettingsTopNavButtonStyle" : "TopNavButtonStyle"];
            tab.Button.CornerRadius = new CornerRadius(8);
        }

        if (!animationsEnabled)
        {
            active.Page.Opacity = 1;
            return;
        }

        active.Page.Opacity = 0;
        var animation = new DoubleAnimation { To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(180)) };
        Storyboard.SetTarget(animation, active.Page);
        Storyboard.SetTargetProperty(animation, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
}
