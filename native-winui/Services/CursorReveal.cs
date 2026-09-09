using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace VitanCut.WinUI.Services;

internal sealed class CursorReveal : IDisposable
{
    private readonly Window _window;
    private readonly FrameworkElement _root;
    private readonly PointerEventHandler _moved;
    private readonly PointerEventHandler _hidePointer;
    private SpriteVisual? _glow;
    private readonly CompositionRadialGradientBrush _brush;
    private readonly CompositionRoundedRectangleGeometry _bounds;
    private readonly CompositionGeometricClip _clip;
    private readonly HashSet<Style> _surfaceStyles;
    private FrameworkElement? _target;
    private bool _enabled;

    public CursorReveal(Window window, FrameworkElement root)
    {
        _window = window;
        _root = root;
        _moved = OnMoved;
        _hidePointer = OnExited;
        _surfaceStyles = SurfaceStyles.Select(key => (Style)Application.Current.Resources[key]).ToHashSet();
        var compositor = ElementCompositionPreview.GetElementVisual(root).Compositor;
        _brush = compositor.CreateRadialGradientBrush();
        _brush.ColorStops.Add(compositor.CreateColorGradientStop(0, Microsoft.UI.Colors.White));
        _brush.ColorStops.Add(compositor.CreateColorGradientStop(1, Microsoft.UI.Colors.Transparent));
        _brush.EllipseCenter = new Vector2(.5f);
        _brush.EllipseRadius = new Vector2(.5f);
        _bounds = compositor.CreateRoundedRectangleGeometry();
        _clip = compositor.CreateGeometricClip(_bounds);
    }

    public void Update(bool enabled)
    {
        Hide();
        var accent = ((SolidColorBrush)Application.Current.Resources["AppAccentBrush"]).Color;
        var dark = _root.ActualTheme == ElementTheme.Dark;
        _brush.ColorStops[0].Color = dark ? Color.FromArgb(34, 230, 243, 255)
            : Color.FromArgb(30, accent.R, accent.G, accent.B);
        if (_enabled == enabled) return;
        _enabled = enabled;
        if (enabled)
        {
            _root.AddHandler(UIElement.PointerMovedEvent, _moved, true);
            _root.PointerExited += OnExited;
            _root.AddHandler(UIElement.PointerWheelChangedEvent, _hidePointer, true);
            _root.AddHandler(UIElement.PointerPressedEvent, _hidePointer, true);
            _root.KeyDown += OnKeyDown;
            _root.SizeChanged += OnSizeChanged;
            _window.Activated += OnActivated;
        }
        else
        {
            _root.RemoveHandler(UIElement.PointerMovedEvent, _moved);
            _root.PointerExited -= OnExited;
            _root.RemoveHandler(UIElement.PointerWheelChangedEvent, _hidePointer);
            _root.RemoveHandler(UIElement.PointerPressedEvent, _hidePointer);
            _root.KeyDown -= OnKeyDown;
            _root.SizeChanged -= OnSizeChanged;
            _window.Activated -= OnActivated;
        }
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_enabled || e.GetCurrentPoint(_root).IsInContact) { Hide(); return; }
        var target = FindSurface(e.OriginalSource as DependencyObject);
        if (target is null || target.ActualWidth <= 0 || target.ActualHeight <= 0) { Hide(); return; }
        if (_target != target)
        {
            Hide();
            // Never replace another feature's composition visual.
            if (ElementCompositionPreview.GetElementChildVisual(target) is not null) return;
            // A handed-in visual must not be reused across different XAML hosts.
            _glow = _brush.Compositor.CreateSpriteVisual();
            _glow.Size = new Vector2(160);
            _glow.Brush = _brush;
            _glow.Clip = _clip;
            _target = target;
            _target.Unloaded += OnTargetUnloaded;
            ElementCompositionPreview.SetElementChildVisual(target, _glow);
        }
        var point = e.GetCurrentPoint(target).Position;
        var offset = new Vector2((float)point.X - 80, (float)point.Y - 80);
        _glow!.Offset = new Vector3(offset, 0);
        _bounds.Offset = -offset;
        _bounds.Size = new Vector2((float)target.ActualWidth, (float)target.ActualHeight);
        var radius = target switch
        {
            Border border => border.CornerRadius.TopLeft,
            Control control => control.CornerRadius.TopLeft,
            _ => 0
        };
        _bounds.CornerRadius = new Vector2((float)radius);
    }

    private FrameworkElement? FindSurface(DependencyObject? element)
    {
        while (element is not null && element != _root)
        {
            if (element is Canvas) return null;
            if (element is ButtonBase or ListViewItem or ComboBox or TextBox or ToggleSwitch)
                return element is Control { IsEnabled: true } control ? control : null;
            if (element is Border { Style: { } style } border && _surfaceStyles.Contains(style)) return border;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private static readonly string[] SurfaceStyles =
        ["PaneSurfaceStyle", "SidebarSurfaceStyle", "SettingsSectionStyle", "MetricSurfaceStyle", "ListSurfaceStyle"];

    private void OnExited(object sender, PointerRoutedEventArgs e) => Hide();
    private void OnKeyDown(object sender, KeyRoutedEventArgs e) => Hide();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Hide();
    private void OnTargetUnloaded(object sender, RoutedEventArgs e) => Hide();
    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated) Hide();
    }

    private void Hide()
    {
        if (_target is null || _glow is null) return;
        _glow.Opacity = 0;
        _target.Unloaded -= OnTargetUnloaded;
        if (Equals(ElementCompositionPreview.GetElementChildVisual(_target), _glow))
            ElementCompositionPreview.SetElementChildVisual(_target, null);
        _target = null;
        _glow.Dispose();
        _glow = null;
    }

    public void Dispose()
    {
        Update(false);
        _clip.Dispose();
        _bounds.Dispose();
        _brush.Dispose();
    }
}
