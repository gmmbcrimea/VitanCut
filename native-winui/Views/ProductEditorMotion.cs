using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace VitanCut.WinUI.Views;

internal sealed class ProductEditorMotion
{
    private static readonly TimeSpan PopupDuration = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ExpansionDuration = TimeSpan.FromMilliseconds(85);
    private readonly UISettings _system = new();

    internal bool AllowsAnimations => App.Preferences.Current.AnimationsEnabled && _system.AnimationsEnabled;

    internal void ShowPopup(UIElement element) => Fade(element, PopupDuration);

    internal void ExpandList(UIElement element) => Fade(element, ExpansionDuration);

    private void Fade(UIElement element, TimeSpan duration)
    {
        if (!AllowsAnimations) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        using var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.Duration = duration;
        fade.InsertKeyFrame(0, 0.9f);
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
