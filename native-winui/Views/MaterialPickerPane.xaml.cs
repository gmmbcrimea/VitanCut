using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;
using VirtualKey = Windows.System.VirtualKey;

namespace VitanCut.WinUI.Views;

public sealed partial class MaterialPickerPane : UserControl
{
    private readonly MaterialPickerBrowse _browse;
    private readonly ProductEditorMotion _motion;
    private readonly Func<MaterialChoice, string?> _apply;
    private readonly Action<string, string?, int?> _trace;
    private readonly DispatcherQueueTimer _filter;
    private readonly UiRequestGate _scrollRequests = new();
    private MaterialPickerRow? _pressedRow;
    private bool _filterPending;
    private bool _stopped;

    internal event Action? SizeDemandChanged;
    internal event Action<string, string?>? CloseRequested;

    internal double RequiredHeight => MaterialPickerLayout.ContentHeight(_browse.Rows.Count,
        Math.Max(0, ActualHeight - Results.ActualHeight) + 2);

    internal MaterialPickerPane(MaterialPickerBrowse browse, ProductEditorMotion motion, Func<MaterialChoice, string?> apply,
        Action<string, string?, int?> trace)
    {
        _browse = browse;
        _motion = motion;
        _apply = apply;
        _trace = trace;
        InitializeComponent();
        OptionsList.ItemContainerTransitions = new TransitionCollection();

        var rowStyle = new Style(typeof(ListViewItem));
        rowStyle.Setters.Add(new Setter(HeightProperty, MaterialPickerLayout.RowHeight));
        rowStyle.Setters.Add(new Setter(MinHeightProperty, 0d));
        rowStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        rowStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        rowStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        OptionsList.ItemContainerStyle = rowStyle;
        OptionsList.ItemsSource = _browse.Rows;
        AutomationProperties.SetName(SearchBox, UiText.Get("Поиск материала по названию и категории"));
        AutomationProperties.SetName(OptionsList, UiText.Get("Категории материалов"));

        _filter = DispatcherQueue.CreateTimer();
        _filter.Interval = TimeSpan.FromMilliseconds(120);
        _filter.IsRepeating = false;
        _filter.Tick += (_, _) => ApplyFilter();
        SearchBox.TextChanged += (_, _) =>
        {
            _trace("picker.search-edited", string.IsNullOrEmpty(SearchBox.Text) ? "empty" : "nonempty", null);
            _filterPending = true;
            if (_pressedRow is null) RestartFilterTimer();
        };
        SearchBox.PreviewKeyDown += SearchKeyDown;
        OptionsList.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is not MaterialPickerRow row) return;
            if (_pressedRow is not null && !ReferenceEquals(_pressedRow, row))
            {
                _trace("option.click", "recycled-press", null);
                return;
            }
            Activate(row);
        };
        OptionsList.PreviewKeyDown += (_, args) =>
        {
            if (args.Key is not (VirtualKey.Enter or VirtualKey.Space) || ItemAt(args.OriginalSource) is not { } row) return;
            args.Handled = true;
            Activate(row);
        };
        OptionsList.AddHandler(PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            _scrollRequests.Begin();
            _filter.Stop();
            _pressedRow = ItemAt(args.OriginalSource);
            _trace("picker.pointer-press", RowKind(_pressedRow), null);
        }), true);
        OptionsList.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, args) =>
        {
            _trace("picker.pointer-release", RowKind(ItemAt(args.OriginalSource)), null);
            EndPointer();
        }), true);
        OptionsList.AddHandler(PointerCaptureLostEvent, new PointerEventHandler((_, _) => EndPointer()), true);
        Footer.SizeChanged += (_, _) => SizeDemandChanged?.Invoke();
        CancelButton.Click += (_, _) => Finish("cancel-button");
        AddHandler(KeyDownEvent, new KeyEventHandler((_, args) =>
        {
            if (args.Key != VirtualKey.Escape) return;
            args.Handled = true;
            Finish("escape");
        }), true);
        Loaded += (_, _) =>
        {
            if (_stopped) return;
            SizeDemandChanged?.Invoke();
            SearchBox.Focus(FocusState.Programmatic);
            _motion.ShowPopup(this);
        };
        EmptyText.Visibility = _browse.MatchCount == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void Stop()
    {
        if (_stopped) return;
        _stopped = true;
        _filter.Stop();
        _scrollRequests.Begin();
        ProductEditorMotion.Reset(this);
        ProductEditorMotion.Reset(OptionsList);
    }

    private void EndPointer()
    {
        var pressed = _pressedRow;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_pressedRow, pressed)) return;
            _pressedRow = null;
            if (!_stopped && _filterPending) RestartFilterTimer();
        });
    }

    private MaterialPickerRow? ItemAt(object source)
    {
        for (var node = source as DependencyObject; node is not null && node != OptionsList;
             node = VisualTreeHelper.GetParent(node))
            if (node is ListViewItem item) return OptionsList.ItemFromContainer(item) as MaterialPickerRow;
        return null;
    }

    private static string RowKind(MaterialPickerRow? row) => row?.Key.Kind.ToString() ?? "list";

    private void RestartFilterTimer()
    {
        _filter.Stop();
        _filter.Start();
    }

    private void ApplyFilter()
    {
        if (_stopped || !_filterPending) return;
        using var measurement = App.UiPerformance.Measure(UiWork.MaterialPicker);
        _filterPending = false;
        _scrollRequests.Begin();
        _browse.Filter(SearchBox.Text);
        _trace("picker.filter", _browse.Searching ? "search" : "all", _browse.MatchCount);
        EmptyText.Visibility = _browse.MatchCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        SizeDemandChanged?.Invoke();
        if (_browse.Rows.Count != 0) ScrollTo(_browse.Rows[0]);
    }

    private void SearchKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is not (VirtualKey.Down or VirtualKey.Enter)) return;
        _filter.Stop();
        ApplyFilter();
        if (args.Key == VirtualKey.Down && _browse.Rows.Count != 0)
        {
            var first = (_browse.Searching ? _browse.Rows.FirstOrDefault(row => row.Option is not null) : null) ?? _browse.Rows[0];
            ScrollTo(first, focus: true);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Enter && _browse.Searching &&
            _browse.Rows.FirstOrDefault(row => row.Option is not null) is { } match)
        {
            _trace("option.search-enter", null, null);
            Activate(match);
            args.Handled = true;
        }
    }

    private void Activate(MaterialPickerRow row)
    {
        if (_stopped || !_browse.Rows.Contains(row)) return;
        _trace("option.click", RowKind(row), null);
        switch (row.Key.Kind)
        {
            case MaterialPickerRowKind.Category:
            case MaterialPickerRowKind.Group:
                using (App.UiPerformance.Measure(UiWork.MaterialPickerExpand))
                {
                    var expanded = _browse.Toggle(row);
                    _trace(expanded ? "picker.expand" : "picker.collapse", RowKind(row), _browse.Rows.Count);
                    SizeDemandChanged?.Invoke();
                    if (expanded)
                    {
                        _motion.ExpandList(OptionsList);
                        ScrollTo(row, instant: true);
                    }
                    else _scrollRequests.Begin();
                }
                break;
            case MaterialPickerRowKind.Add:
                Finish("create-material", row.Key.Category);
                break;
            case MaterialPickerRowKind.Material when row.Option is { } option:
                if (_apply(option.Choice) is { } error)
                {
                    _trace("option.rejected", null, null);
                    ErrorText.Text = error;
                    ErrorText.Visibility = Visibility.Visible;
                }
                else Finish("applied");
                break;
        }
    }

    private void ScrollTo(MaterialPickerRow row, bool focus = false, bool instant = false)
    {
        var request = _scrollRequests.Begin();
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (_stopped || !_scrollRequests.IsCurrent(request) || !_browse.Rows.Contains(row)) return;
            if (focus || MaterialPickerLayout.ContentHeight(_browse.Rows.Count, 0) > OptionsList.ActualHeight)
            {
                if (instant && OptionsList.ContainerFromItem(row) is UIElement element)
                    ProductEditorMotion.BringIntoView(element, 0);
                else OptionsList.ScrollIntoView(row, ScrollIntoViewAlignment.Leading);
                _trace("picker.align-header", RowKind(row), _browse.Rows.IndexOf(row));
            }
            if (!focus) return;
            OptionsList.UpdateLayout();
            if (OptionsList.ContainerFromItem(row) is Control container) container.Focus(FocusState.Keyboard);
            else OptionsList.Focus(FocusState.Keyboard);
        });
    }

    private void Finish(string reason, string? category = null)
    {
        if (_stopped) return;
        Stop();
        CloseRequested?.Invoke(reason, category);
    }
}
