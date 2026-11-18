using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace VitanCut.WinUI.Views;

public sealed partial class ProductWindow
{
    private enum DetailFocus { Name, Material }

    private bool FocusEditorAnchor()
    {
        BackgroundFocusTarget.IsTabStop = true;
        if (BackgroundFocusTarget.Focus(FocusState.Programmatic)) return true;
        BackgroundFocusTarget.IsTabStop = false;
        return false;
    }

    private bool TryFocusMaterial(DetailRow row) =>
        row is { Container: { } container, MaterialButton: { IsLoaded: true } button } &&
        ReferenceEquals(container.Tag, row) && button.Focus(FocusState.Programmatic);

    private void RevealDetail(DetailRow row, DetailFocus focus)
    {
        var request = _detailViewRequests.Begin();
        bool IsCurrent() => !_editorClosed && Root.XamlRoot is not null && _detailViewRequests.IsCurrent(request) &&
            _detailEditor.Contains(row.Detail);

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsCurrent()) return;
            var index = _detailRows.IndexOf(row);
            if (index < 0) return;
            if (focus == DetailFocus.Name && DetailsHorizontalScroll.Value > 0)
                DetailsHorizontalScroll.Value = 0;
            DetailsRowsPanel.UpdateLayout();
            if (DetailsRowsPanel.GetOrCreateElement(index) is not FrameworkElement host) return;
            void BringAndFocus()
            {
                if (!IsCurrent()) return;
                DetailsRowsPanel.UpdateLayout();
                var activeIndex = _detailRows.IndexOf(row);
                // A host captured before layout may already have been leased to another row.
                if (activeIndex < 0 || DetailsRowsPanel.TryGetElement(activeIndex) is not FrameworkElement currentHost ||
                    !ReferenceEquals(row.Container?.Tag, row)) return;
                if (!IsDetailVisible(currentHost))
                    ProductEditorMotion.BringIntoView(currentHost, 0.5);

                DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                {
                    if (!IsCurrent()) return;
                    var currentIndex = _detailRows.IndexOf(row);
                    if (currentIndex < 0) return;
                    if (DetailsRowsPanel.TryGetElement(currentIndex) is FrameworkElement { IsLoaded: true } realized &&
                        ReferenceEquals(row.Container?.Tag, row) && !IsDetailVisible(realized))
                        ProductEditorMotion.BringIntoView(realized, 0.5);
                    if (focus == DetailFocus.Name)
                    {
                        if (ReferenceEquals(row.Container?.Tag, row) && row.NameBox is { IsLoaded: true } nameBox &&
                            nameBox.Focus(FocusState.Programmatic))
                            nameBox.SelectAll();
                        return;
                    }
                    var focused = TryFocusMaterial(row);
                    TraceMaterial("picker.restore-focus", row.Detail, focused ? "material-field" : "editor-fallback");
                    TraceMaterial("option.post-layout", row.Detail, row.MaterialName is not { } label ? "no-view" :
                        label.Text == row.SelectedMaterial?.Material.Name ? "label-matches" : "label-mismatch");
                });
            }
            if (host.IsLoaded) BringAndFocus();
            else
            {
                RoutedEventHandler? loaded = null;
                loaded = (_, _) =>
                {
                    host.Loaded -= loaded;
                    DispatcherQueue.TryEnqueue(BringAndFocus);
                };
                host.Loaded += loaded;
            }
        });
    }

    private bool IsDetailVisible(FrameworkElement host)
    {
        if (!host.IsLoaded || host.ActualHeight <= 0) return false;
        var bounds = host.TransformToVisual(ProductScrollViewer).TransformBounds(
            new Rect(0, 0, host.ActualWidth, host.ActualHeight));
        var top = StickyDetailsToolbar.Visibility == Visibility.Visible ? StickyDetailsToolbar.ActualHeight : 0;
        var bottom = ProductScrollViewer.ActualHeight;
        return bounds.Top >= top - 1 && (bounds.Bottom <= bottom + 1 || bounds.Height > bottom - top);
    }
}
