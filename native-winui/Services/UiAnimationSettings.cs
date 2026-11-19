using System.Globalization;
using Microsoft.UI.Xaml;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

internal static class UiAnimationSettings
{
    private static double? _appliedSpeed;
    private static readonly (string Key, double Milliseconds)[] Durations =
    [
        ("ControlNormalAnimationDuration", 250),
        ("ControlFastAnimationDuration", 167),
        ("ControlFastAnimationAfterDuration", 168),
        ("ControlFasterAnimationDuration", 83),
        ("ComboBoxItemScaleAnimationDuration", 167),
        ("ScrollBarExpandDuration", 167),
        ("ScrollBarOpacityChangeDuration", 83),
        ("ScrollBarColorChangeDuration", 83),
        ("ScrollBarContractDuration", 167),
        ("ScrollViewerSeparatorExpandDuration", 100),
        ("ScrollViewerSeparatorContractDuration", 100),
        ("SplitViewPaneAnimationOpenDuration", 200),
        ("SplitViewPaneAnimationOpenPreDuration", 199.99),
        ("SplitViewPaneAnimationCloseDuration", 100)
    ];

    internal static void Apply(AppPreferences preferences)
    {
        var enabled = preferences.AnimationsEnabled && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        var speed = enabled ? InterfaceOptions.Speed(preferences.AnimationSpeed) : 0;
        if (_appliedSpeed == speed) return;
        _appliedSpeed = speed;
        foreach (var (key, milliseconds) in Durations)
            Application.Current.Resources[key] = (enabled
                ? InterfaceOptions.Duration(TimeSpan.FromMilliseconds(milliseconds), speed) : TimeSpan.Zero)
                .ToString("c", CultureInfo.InvariantCulture);
    }
}
