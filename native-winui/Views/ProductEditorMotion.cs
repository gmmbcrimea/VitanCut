using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace VitanCut.WinUI.Views;

internal sealed class ProductEditorMotion
{
    private static readonly TimeSpan PopupDuration = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PickerDuration = TimeSpan.FromMilliseconds(145);
    private static readonly TimeSpan ExpansionDuration = TimeSpan.FromMilliseconds(115);
    private readonly UISettings _system = new();

    internal bool AllowsAnimations => App.Preferences.Current.AnimationsEnabled && _system.AnimationsEnabled;

    internal void ShowPopup(UIElement element) => Fade(element, PopupDuration, 0.9f);

    internal void ShowMaterialPicker(UIElement element) => Fade(element, PickerDuration, 0.22f);

    internal void ExpandList(UIElement element) => Fade(element, ExpansionDuration, 0.45f);

    private void Fade(UIElement element, TimeSpan duration, float initialOpacity)
    {
        if (!AllowsAnimations) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        using var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = duration;
        fade.InsertKeyFrame(0, initialOpacity);
        fade.InsertKeyFrame(1, 1f);
        visual.StartAnimation("Opacity", fade);
    }

    internal static void Reset(UIElement element)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1;
    }

    internal static void BringIntoView(UIElement element, double verticalAlignment) =>
        element.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = false,
            VerticalAlignmentRatio = verticalAlignment
        });
}
