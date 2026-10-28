using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace VitanCut.WinUI.Services;

internal sealed class CursorReveal : IDisposable
{
    private const float Radius = 100;
    private readonly Window _window;
    private readonly FrameworkElement _root;
    private readonly PointerEventHandler _moved;
    private readonly PointerEventHandler _scrolled;
    private readonly CompositionRadialGradientBrush _brush;
    private readonly ContainerVisual _host;
    private readonly CompositionPropertySet _light;
    private readonly ExpressionAnimation _position;
    private readonly Dictionary<FrameworkElement, LightMask> _masks = [];
    private readonly HashSet<ScrollViewer> _scrollViewers = [];
    private readonly HashSet<ScrollViewer> _scrolling = [];
    private bool _enabled;
    private bool _visible;
    private bool _layoutDirty = true;
    private bool _refreshQueued;
    private object? _pointerSource;

    public CursorReveal(Window window, FrameworkElement root)
    {
        _window = window;
        _root = root;
        _moved = OnMoved;
        _scrolled = OnScrolled;
        var compositor = ElementCompositionPreview.GetElementVisual(root).Compositor;
        _host = compositor.CreateContainerVisual();
        _host.Opacity = 0;
        _light = compositor.CreatePropertySet();
        _light.InsertVector3("Position", Vector3.Zero);
        _position = compositor.CreateExpressionAnimation("light.Position");
        _position.SetReferenceParameter("light", _light);
        _brush = compositor.CreateRadialGradientBrush();
        _brush.ColorStops.Add(compositor.CreateColorGradientStop(0, Microsoft.UI.Colors.White));
        _brush.ColorStops.Add(compositor.CreateColorGradientStop(1, Microsoft.UI.Colors.Transparent));
        _brush.EllipseCenter = new Vector2(.5f);
        _brush.EllipseRadius = new Vector2(.5f);
    }

    public void Update(bool enabled)
    {
        var accent = ((Microsoft.UI.Xaml.Media.SolidColorBrush)Application.Current.Resources["AppAccentBrush"]).Color;
        _brush.ColorStops[0].Color = _root.ActualTheme == ElementTheme.Dark
            ? Color.FromArgb(110, 230, 243, 255) : Color.FromArgb(90, accent.R, accent.G, accent.B);
        if (_enabled == enabled) return;
        if (enabled && ElementCompositionPreview.GetElementChildVisual(_root) is not null) return;
        _enabled = enabled;
        if (enabled)
        {
            ElementCompositionPreview.SetElementChildVisual(_root, _host);
            _root.AddHandler(UIElement.PointerMovedEvent, _moved, true);
            _root.AddHandler(UIElement.PointerWheelChangedEvent, _scrolled, true);
            _root.PointerExited += OnExited;
            _root.KeyDown += OnKeyDown;
            _root.SizeChanged += OnSizeChanged;
            _root.LayoutUpdated += OnLayoutUpdated;
            _window.Activated += OnActivated;
        }
        else
        {
            _root.RemoveHandler(UIElement.PointerMovedEvent, _moved);
            _root.RemoveHandler(UIElement.PointerWheelChangedEvent, _scrolled);
            _root.PointerExited -= OnExited;
            _root.KeyDown -= OnKeyDown;
            _root.SizeChanged -= OnSizeChanged;
            _root.LayoutUpdated -= OnLayoutUpdated;
            _window.Activated -= OnActivated;
            foreach (var scroll in _scrollViewers) UnwatchScroll(scroll);
            _scrollViewers.Clear();
            _scrolling.Clear();
            Hide();
            ClearMasks();
            if (Equals(ElementCompositionPreview.GetElementChildVisual(_root), _host))
                ElementCompositionPreview.SetElementChildVisual(_root, null);
        }
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_root);
        if (!_enabled || point.IsInContact || !UiLayout.ContainsPoint(point.Position.X, point.Position.Y, _root.ActualWidth, _root.ActualHeight))
        {
            Hide();
            return;
        }
        if (!ReferenceEquals(_pointerSource, e.OriginalSource))
        {
            _pointerSource = e.OriginalSource;
            _layoutDirty = true;
        }
        // One property drives every light sample. Hover targets never move or reparent the light.
        _light.InsertVector3("Position", new Vector3((float)point.Position.X - Radius, (float)point.Position.Y - Radius, 0));
        _visible = true;
        if (!_layoutDirty && _scrolling.Count == 0) _host.Opacity = 1;
        if (_layoutDirty) QueueMaskRefresh();
    }

    private void QueueMaskRefresh()
    {
        if (_refreshQueued || !_enabled || !_visible || _scrolling.Count > 0) return;
        _refreshQueued = true;
        if (!_root.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _refreshQueued = false;
            if (_enabled && _visible && _layoutDirty && _scrolling.Count == 0) RefreshMasks();
        })) _refreshQueued = false;
    }

    private void RefreshMasks()
    {
        using var measurement = App.UiPerformance.Measure(UiWork.CursorRevealMasks);
        var active = new HashSet<FrameworkElement>();
        var scrolls = new HashSet<ScrollViewer>();
        var backdrop = _root.ActualTheme == ElementTheme.Dark ? Color.FromArgb(255, 32, 32, 32) : Microsoft.UI.Colors.White;
        CollectMasks(_root, new Rect(0, 0, _root.ActualWidth, _root.ActualHeight), backdrop, active, scrolls);
        foreach (var scroll in _scrollViewers.Where(scroll => !scrolls.Contains(scroll)).ToArray())
        {
            UnwatchScroll(scroll);
            _scrollViewers.Remove(scroll);
            _scrolling.Remove(scroll);
        }
        foreach (var surface in _masks.Keys.Where(surface => !active.Contains(surface)).ToArray())
        {
            var mask = _masks[surface];
            _host.Children.Remove(mask.Visual);
            mask.Dispose();
            _masks.Remove(surface);
        }
        _layoutDirty = false;
        if (_enabled && _visible && _scrolling.Count == 0) _host.Opacity = 1;
    }

    private void CollectMasks(DependencyObject node, Rect viewport, Color backdrop, HashSet<FrameworkElement> active, HashSet<ScrollViewer> scrolls)
    {
        if (node is FrameworkElement { Visibility: Visibility.Collapsed } or UIElement { Opacity: <= 0 } or Canvas or Control { IsEnabled: false }) return;
        if (node is ScrollViewer scroll)
        {
            scrolls.Add(scroll);
            if (_scrollViewers.Add(scroll))
            {
                scroll.ViewChanging += OnViewChanging;
                scroll.ViewChanged += OnViewChanged;
            }
            viewport = Intersect(viewport, Bounds(scroll));
        }
        if (node is ScrollContentPresenter presenter)
            viewport = Intersect(viewport, Bounds(presenter));
        if (node is FrameworkElement element && element.Clip is Microsoft.UI.Xaml.Media.RectangleGeometry clip)
            viewport = Intersect(viewport, element.TransformToVisual(_root).TransformBounds(clip.Rect));
        if (viewport.Width <= 0 || viewport.Height <= 0) return;
        if (node is FrameworkElement { ActualWidth: > 0, ActualHeight: > 0 } surface && TryVisibleSurface(surface, backdrop, out var radius))
        {
                var bounds = Bounds(surface);
                var visible = Intersect(viewport, bounds);
                if (visible.Width <= 0 || visible.Height <= 0) return;
                active.Add(surface);
                if (!_masks.TryGetValue(surface, out var mask))
                {
                    mask = new LightMask(_brush, _light, _position);
                    _masks.Add(surface, mask);
                    _host.Children.InsertAtTop(mask.Visual);
                }
                mask.SetBounds(bounds, viewport, radius);
        }
        // Carry the nearest painted background down once instead of searching
        // the complete ancestor chain for every border in a large editor.
        var background = node switch { Border border => border.Background, Panel panel => panel.Background,
            Control control => control.Background, ContentPresenter content => content.Background, _ => null };
        if (PaintColor(background) is { A: > 8 } color) backdrop = color;
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var index = 0; index < count; index++)
            CollectMasks(VisualTreeHelper.GetChild(node, index), viewport, backdrop, active, scrolls);
    }

    private static bool TryVisibleSurface(FrameworkElement element, Color backdrop, out float radius)
    {
        Brush? fill, stroke;
        double width;
        switch (element)
        {
            case Border border:
                fill = border.Background; stroke = border.BorderBrush;
                width = Math.Max(Math.Max(border.BorderThickness.Left, border.BorderThickness.Right), Math.Max(border.BorderThickness.Top, border.BorderThickness.Bottom));
                radius = (float)border.CornerRadius.TopLeft;
                break;
            case ContentPresenter presenter:
                fill = presenter.Background; stroke = presenter.BorderBrush;
                width = Math.Max(Math.Max(presenter.BorderThickness.Left, presenter.BorderThickness.Right), Math.Max(presenter.BorderThickness.Top, presenter.BorderThickness.Bottom));
                radius = (float)presenter.CornerRadius.TopLeft;
                break;
            case Rectangle rectangle:
                fill = rectangle.Fill; stroke = rectangle.Stroke; width = rectangle.StrokeThickness;
                radius = (float)Math.Min(rectangle.RadiusX, rectangle.RadiusY);
                break;
            default:
                radius = 0;
                return false;
        }
        var fillColor = PaintColor(fill) ?? Microsoft.UI.Colors.Transparent;
        var borderColor = PaintColor(stroke) ?? Microsoft.UI.Colors.Transparent;
        return RevealGeometry.HasVisibleEdge(borderColor.A, width, Pack(fillColor), Pack(backdrop));
    }

    private static Color? PaintColor(Brush? brush) => brush switch
    {
        SolidColorBrush solid => Color.FromArgb((byte)(solid.Color.A * solid.Opacity), solid.Color.R, solid.Color.G, solid.Color.B),
        AcrylicBrush acrylic => acrylic.FallbackColor,
        _ => null
    };
    private static uint Pack(Color color) => (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);

    private Rect Bounds(FrameworkElement surface) => surface.TransformToVisual(_root).TransformBounds(
        new Rect(0, 0, surface.ActualWidth, surface.ActualHeight));

    private static Rect Intersect(Rect first, Rect second)
    {
        var clipped = RevealGeometry.Clip(new RevealBounds(first.X, first.Y, first.Width, first.Height),
            new RevealBounds(second.X, second.Y, second.Width, second.Height));
        return new Rect(clipped.X, clipped.Y, clipped.Width, clipped.Height);
    }

    private void OnExited(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_root).Position;
        // PointerExited can bubble from children. Only leaving the window extinguishes the light.
        if (!UiLayout.ContainsPoint(point.X, point.Y, _root.ActualWidth, _root.ActualHeight)) Hide();
    }
    private void OnScrolled(object sender, PointerRoutedEventArgs e)
    {
        InvalidateScrollMasks();
        QueueMaskRefresh();
    }
    private void OnViewChanging(object? sender, ScrollViewerViewChangingEventArgs e)
    {
        if (sender is ScrollViewer scroll) _scrolling.Add(scroll);
        InvalidateScrollMasks();
    }
    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroll) return;
        if (e.IsIntermediate) _scrolling.Add(scroll);
        else _scrolling.Remove(scroll);
        InvalidateScrollMasks();
        QueueMaskRefresh();
    }
    private void InvalidateScrollMasks()
    {
        // Scrolling is compositor-driven; root-space masks are stale until the final view arrives.
        _layoutDirty = true;
        _host.Opacity = 0;
    }
    private void UnwatchScroll(ScrollViewer scroll)
    {
        scroll.ViewChanging -= OnViewChanging;
        scroll.ViewChanged -= OnViewChanged;
    }
    private void OnKeyDown(object sender, KeyRoutedEventArgs e) => Hide();
    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => _layoutDirty = true;
    private void OnLayoutUpdated(object? sender, object e)
    {
        _layoutDirty = true;
        QueueMaskRefresh();
    }
    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated) Hide();
    }
    private void Hide() { _visible = false; _host.Opacity = 0; }
    private void ClearMasks()
    {
        _host.Children.RemoveAll();
        foreach (var mask in _masks.Values) mask.Dispose();
        _masks.Clear();
        _layoutDirty = true;
    }

    public void Dispose()
    {
        Update(false);
        ClearMasks();
        _position.Dispose();
        _light.Dispose();
        _host.Dispose();
        _brush.Dispose();
    }

    private sealed class LightMask : IDisposable
    {
        private readonly CompositionRoundedRectangleGeometry _bounds;
        private readonly CompositionSpriteShape _edge;
        private readonly ShapeVisual _edgeVisual;
        private readonly CompositionVisualSurface _surface;
        private readonly CompositionSurfaceBrush _edgeBrush;
        private readonly CompositionColorBrush _edgeColor;
        private readonly CompositionMaskBrush _maskBrush;
        private readonly CompositionRectangleGeometry _viewportBounds;
        private readonly CompositionGeometricClip _viewportClip;
        private readonly SpriteVisual _sample;
        private readonly CompositionPropertySet _geometry;
        private readonly ExpressionAnimation _clipPosition;
        private Rect _lastBounds;
        private Rect _lastViewport;
        private float _lastRadius = -1;
        public ContainerVisual Visual { get; }

        public LightMask(CompositionRadialGradientBrush brush, CompositionPropertySet light, ExpressionAnimation position)
        {
            var compositor = brush.Compositor;
            _geometry = compositor.CreatePropertySet();
            _geometry.InsertVector2("Origin", Vector2.Zero);
            _bounds = compositor.CreateRoundedRectangleGeometry();
            _clipPosition = compositor.CreateExpressionAnimation("geometry.Origin - Vector2(light.Position.X, light.Position.Y)");
            _clipPosition.SetReferenceParameter("geometry", _geometry);
            _clipPosition.SetReferenceParameter("light", light);
            _bounds.StartAnimation("Offset", _clipPosition);
            _edgeColor = compositor.CreateColorBrush(Microsoft.UI.Colors.White);
            _edge = compositor.CreateSpriteShape(_bounds);
            _edge.StrokeBrush = _edgeColor;
            _edge.StrokeThickness = 1.5f;
            _edgeVisual = compositor.CreateShapeVisual();
            _edgeVisual.Size = new Vector2(Radius * 2);
            _edgeVisual.Shapes.Add(_edge);
            _surface = compositor.CreateVisualSurface();
            _surface.SourceVisual = _edgeVisual;
            _surface.SourceSize = _edgeVisual.Size;
            _edgeBrush = compositor.CreateSurfaceBrush(_surface);
            _maskBrush = compositor.CreateMaskBrush();
            _maskBrush.Source = brush;
            _maskBrush.Mask = _edgeBrush;
            _sample = compositor.CreateSpriteVisual();
            _sample.Size = new Vector2(Radius * 2);
            _sample.Brush = _maskBrush;
            _sample.StartAnimation("Offset", position);
            _viewportBounds = compositor.CreateRectangleGeometry();
            _viewportClip = compositor.CreateGeometricClip(_viewportBounds);
            Visual = compositor.CreateContainerVisual();
            Visual.Clip = _viewportClip;
            Visual.Children.InsertAtTop(_sample);
        }

        public void SetBounds(Rect rect, Rect viewport, float radius)
        {
            if (rect.Equals(_lastBounds) && viewport.Equals(_lastViewport) && radius == _lastRadius) return;
            _lastBounds = rect;
            _lastViewport = viewport;
            _lastRadius = radius;
            const float inset = .75f;
            _geometry.InsertVector2("Origin", new Vector2((float)rect.X + inset, (float)rect.Y + inset));
            _bounds.Size = new Vector2(Math.Max(0, (float)rect.Width - inset * 2), Math.Max(0, (float)rect.Height - inset * 2));
            _bounds.CornerRadius = new Vector2(Math.Max(0, Math.Min(radius - inset, (float)Math.Min(rect.Width, rect.Height) / 2)));
            // Keep the real control's corners; the scroll viewport cuts them with a separate straight edge.
            _viewportBounds.Offset = new Vector2((float)viewport.X, (float)viewport.Y);
            _viewportBounds.Size = new Vector2((float)viewport.Width, (float)viewport.Height);
        }

        public void Dispose()
        {
            Visual.Dispose();
            _sample.Dispose();
            _viewportClip.Dispose();
            _viewportBounds.Dispose();
            _maskBrush.Dispose();
            _edgeBrush.Dispose();
            _surface.Dispose();
            _edgeVisual.Dispose();
            _edge.Dispose();
            _edgeColor.Dispose();
            _bounds.Dispose();
            _clipPosition.Dispose();
            _geometry.Dispose();
        }
    }
}
