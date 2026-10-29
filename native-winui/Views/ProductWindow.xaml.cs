using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Pickers;
using System.Runtime.InteropServices;
using System.Collections.ObjectModel;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;
using Windows.Graphics;
using WinRT.Interop;

namespace VitanCut.WinUI.Views;

public sealed partial class ProductWindow : Window
{
    private const int MinimumWindowWidth = 760;
    private const int MinimumWindowHeight = 640;
    private const int WmGetMinMaxInfo = 0x0024;

    private readonly WindowSubclassProc _minSizeSubclassProc;
    private readonly Project _project;
    private readonly Product _sourceProduct;
    private readonly bool _isNew;
    private readonly CatalogService? _catalog;
    private readonly bool _catalogEditor;
    private readonly List<TextBox> _fixedNameBoxes = [];
    private readonly UiRequestGate _imagePreviewRequests = new();
    private readonly UiRequestGate _materialFocusRequests = new();
    private readonly UiRequestGate _detailBringRequests = new();
    private MaterialPickerOption[]? _materialPickerOptions;
    private Database? _materialPickerDatabase;
    private long _materialPickerRevision = -1;
    private string _materialPickerLanguage = "";
    private string _formulaNamesSignature = "";
    private long _formulaNamesVersion;
    private readonly List<NumberBox> _fixedValueBoxes = [];
    private readonly Dictionary<TextBox, Flyout> _formulaFlyouts = [];
    private readonly Dictionary<TextBox, ListView> _formulaSuggestionLists = [];
    private readonly Dictionary<TextBox, FormulaQuery> _formulaQueries = [];
    private sealed record FormulaQuery(string Text, int Start, int Caret);
    private readonly Dictionary<TextBox, FormulaField> _formulaFields = [];
    private bool _refreshingFormulaText;
    private sealed record FormulaField(Detail Detail, bool Width, FormulaPresentation Presentation)
    {
        public string Expression => Width ? Detail.WidthExpr : Detail.LengthExpr;
        public string? DisplayedExpression { get; set; }
        public string? InputText { get; set; }
        public long DisplayedNamesVersion { get; set; } = -1;
    }
    private ObservableCollection<DetailRow> _detailRows = [];
    private sealed record DetailRowView(Border Container, Action<DetailRow> Bind, Action Unbind)
    {
        public bool CompactMode { get; set; } = App.Preferences.Current.CompactMode;
        public bool AnimationsEnabled { get; set; } = App.Preferences.Current.AnimationsEnabled;
    }
    private DetailRow? _selectedDetailRow;
    private bool _loading = true;
    private bool _insertingReference;
    private bool _updatingMaterialView;
    private bool _updatingStickyDetailActions;

    private void ProductScrollViewChanged(object sender, ScrollViewerViewChangedEventArgs e) => UpdateStickyDetailActions();

    private IDisposable? _pendingScrollFrame;
    private void MeasureScrollFrame(object sender, PointerRoutedEventArgs e)
    {
        _detailBringRequests.Begin();
        if (!App.UiPerformance.Enabled || _pendingScrollFrame is not null) return;
        _pendingScrollFrame = App.UiPerformance.Measure(UiWork.ProductScrollFrame, asynchronous: true);
        CompositionTarget.Rendering += CompleteScrollFrame;
    }

    private void CompleteScrollFrame(object? sender, object e)
    {
        CompositionTarget.Rendering -= CompleteScrollFrame;
        _pendingScrollFrame?.Dispose();
        _pendingScrollFrame = null;
    }

    private void DetailsToolbarSizeChanged(object sender, SizeChangedEventArgs e) => UpdateStickyDetailActions();

    private void StickyDetailsToolbarSizeChanged(object sender, SizeChangedEventArgs e) => UpdateStickyDetailClip();

    private void UpdateStickyDetailClip()
    {
        if (StickyDetailsToolbar.Visibility != Visibility.Visible)
        {
            ProductScrollViewer.Clip = null;
            return;
        }
        // The reveal overlay also reads this clip, so hidden controls cannot shine through the toolbar.
        var height = Math.Min(ProductScrollViewer.ActualHeight,
            Math.Max(StickyDetailsToolbar.ActualHeight, DetailsActionsPanel.ActualHeight + 18));
        var rect = new Windows.Foundation.Rect(0, height, ProductScrollViewer.ActualWidth,
            Math.Max(0, ProductScrollViewer.ActualHeight - height));
        if (ProductScrollViewer.Clip is RectangleGeometry clip)
        {
            if (clip.Rect != rect) clip.Rect = rect;
        }
        else ProductScrollViewer.Clip = new RectangleGeometry { Rect = rect };
    }

    private void UpdateStickyDetailActions()
    {
        if (_updatingStickyDetailActions || DetailsActionsPanel is null || StickyDetailsActionsSlot is null ||
            !DetailsToolbarHeader.IsLoaded || ProductScrollViewer.ActualHeight <= 0) return;
        var top = DetailsToolbarHeader.TransformToVisual(ProductScrollViewer).TransformPoint(new Windows.Foundation.Point()).Y;
        var pin = top < 0;
        var pinned = StickyDetailsActionsSlot.Children.Contains(DetailsActionsPanel);
        if (pin == pinned) { UpdateStickyDetailClip(); return; }
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot) as Control;
        var restoreFocus = false;
        for (DependencyObject? parent = focused; parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (ReferenceEquals(parent, DetailsActionsPanel)) { restoreFocus = true; break; }
        _updatingStickyDetailActions = true;
        try
        {
            if (pin)
            {
                DetailsActionsSlot.Width = DetailsActionsPanel.ActualWidth;
                DetailsActionsSlot.Height = DetailsActionsPanel.ActualHeight;
                DetailsActionsSlot.Children.Remove(DetailsActionsPanel);
                StickyDetailsActionsSlot.Children.Add(DetailsActionsPanel);
                StickyDetailsToolbar.Visibility = Visibility.Visible;
            }
            else
            {
                StickyDetailsActionsSlot.Children.Remove(DetailsActionsPanel);
                DetailsActionsSlot.Children.Add(DetailsActionsPanel);
                StickyDetailsToolbar.Visibility = Visibility.Collapsed;
                DetailsActionsSlot.Width = double.NaN;
                DetailsActionsSlot.Height = double.NaN;
            }
        }
        finally { _updatingStickyDetailActions = false; }
        if (restoreFocus) focused?.Focus(FocusState.Programmatic);
        UpdateStickyDetailClip();
    }

    private void DetailsTableViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var contentWidth = Math.Max(DetailTableLayout.MinimumWidth + 28, e.NewSize.Width);
        DetailsTable.Width = contentWidth;
        DetailsTable.HorizontalAlignment = HorizontalAlignment.Left;
        var bounds = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
        if (DetailsTableViewport.Clip is RectangleGeometry clip) clip.Rect = bounds;
        else DetailsTableViewport.Clip = new RectangleGeometry { Rect = bounds };
        DetailsHorizontalScroll.Maximum = Math.Max(0, contentWidth - e.NewSize.Width);
        DetailsHorizontalScroll.ViewportSize = e.NewSize.Width;
        DetailsHorizontalScroll.LargeChange = e.NewSize.Width;
        DetailsHorizontalScroll.Visibility = DetailsHorizontalScroll.Maximum > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void DetailsHorizontalScrollChanged(object sender, RangeBaseValueChangedEventArgs e) =>
        DetailsHorizontalOffset.X = -e.NewValue;

    private void DetailsHeaderSizeChanged(object sender, SizeChangedEventArgs e) => UpdateDetailColumns();

    private void UpdateDetailColumns()
    {
        if (DetailsHeaderGrid is null) return;
        var widths = DetailTableLayout.ColumnWidths(DetailsHeaderGrid.ActualWidth);
        ApplyDetailColumns(DetailsHeaderGrid, widths);
        foreach (var row in _detailRows)
            if (row.Container?.Child is Grid grid) ApplyDetailColumns(grid, widths);
    }

    private static void ApplyDetailColumns(Grid grid, double[] widths)
    {
        for (var i = 0; i < widths.Length; i++)
        {
            var width = new GridLength(widths[i]);
            var column = grid.ColumnDefinitions[i];
            if (!column.Width.Equals(width)) column.Width = width;
        }
    }

    public event Action<Product, bool>? ProductSaved;

    public ProductWindow(Project project, Product? product = null, CatalogService? catalog = null, bool catalogEditor = false)
    {
        _minSizeSubclassProc = MinSizeSubclassProc;
        _project = project;
        _isNew = product is null;
        _sourceProduct = CloneProduct(product ?? new Product());
        _catalog = catalog;
        _catalogEditor = catalogEditor;

        InitializeComponent();
        App.UiPerformance.Trace(UiLifecycle.ProductOpened);
        Closed += (_, _) =>
        {
            App.UiPerformance.Trace(UiLifecycle.ProductClosed);
            CompositionTarget.Rendering -= CompleteScrollFrame;
            _pendingScrollFrame = null;
            App.UiPerformance.Flush();
        };
        Root.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(MeasureScrollFrame), true);
        Root.GotFocus += (_, _) => CloseInactiveFormulaSuggestions();
        BackgroundFocusTarget.LostFocus += (_, _) => BackgroundFocusTarget.IsTabStop = false;
        Root.AddHandler(UIElement.TappedEvent, new TappedEventHandler(ProductBackgroundTapped), true);
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => { _materialFocusRequests.Begin(); _detailBringRequests.Begin(); }), true);
        Root.KeyDown += (_, _) => { _materialFocusRequests.Begin(); _detailBringRequests.Begin(); };
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
                foreach (var target in _formulaFlyouts.Keys.ToList()) HideFormulaSuggestions(target);
        };
        Title = _isNew ? UiText.Get("Vitan-Cut - Добавить изделие") : $"Vitan-Cut - {_sourceProduct.Name}";
        TitleText.Text = _isNew ? UiText.Get("Добавить изделие") : _sourceProduct.Name;
        SubtitleText.Text = $"{_project.Name} · {_project.Counterparty}";
        App.Preferences.Apply(this);
        ConfigureMinimumWindowSize();

        BuildFixedSizeRows();
        LoadCatalogProducts();
        FillProduct();
        RefreshDetails();
        UpdateResponsiveLayout();
        UpdateSummary();
    }

    private void ConfigureMinimumWindowSize()
    {
        AppWindow.Resize(new SizeInt32(980, 760));
        SetWindowSubclass(WindowNative.GetWindowHandle(this), _minSizeSubclassProc, new UIntPtr(1), UIntPtr.Zero);
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
    }

    private void RootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveLayout();
        DispatcherQueue.TryEnqueue(UpdateStickyDetailActions);
    }

    private void UpdateResponsiveLayout()
    {
        if (ProductSectionsGrid is null) return;
        var stacked = Root.ActualWidth > 0 && Root.ActualWidth < 980;
        ProductSectionsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        ProductSectionsGrid.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(FixedSizesCard, stacked ? 0 : 1);
        Grid.SetRow(FixedSizesCard, stacked ? 1 : 0);
    }

    private void BuildFixedSizeRows()
    {
        FixedSizesGrid.RowDefinitions.Clear();
        FixedSizesGrid.Children.Clear();
        _fixedNameBoxes.Clear();
        _fixedValueBoxes.Clear();

        while (_sourceProduct.FixedSizes.Count < 6)
        {
            _sourceProduct.FixedSizes.Add(new FixedSize());
        }

        for (var i = 0; i < 6; i++)
        {
            FixedSizesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var panel = new Grid { ColumnSpacing = 8 };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });

            var nameBox = new TextBox { Header = UiText.Format($"Размер {i + 1}"), PlaceholderText = UiText.Get("Название") };
            var valueBox = new NumberBox { Header = UiText.Get("Значение"), Minimum = 0 };
            nameBox.TextChanged += ProductFieldChanged;
            valueBox.ValueChanged += ProductNumberChanged;
            Grid.SetColumn(valueBox, 1);

            panel.Children.Add(nameBox);
            panel.Children.Add(valueBox);
            Grid.SetRow(panel, i);
            FixedSizesGrid.Children.Add(panel);

            _fixedNameBoxes.Add(nameBox);
            _fixedValueBoxes.Add(valueBox);
        }
    }

    private void LoadCatalogProducts()
    {
        CatalogProductBox.ItemsSource = App.State.Database.Catalog.TryGetValue(_project.Counterparty, out var products)
            ? products
            : [];
        CatalogPickerCard.Visibility = _isNew ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FillProduct()
    {
        _loading = true;
        ProductNameBox.Text = _sourceProduct.Name;
        ProductLengthBox.Value = _sourceProduct.Length;
        ProductDepthBox.Value = _sourceProduct.Depth;
        ProductHeightBox.Value = _sourceProduct.Height;
        ProductQtyBox.Value = _sourceProduct.Qty <= 0 ? 1 : _sourceProduct.Qty;
        HasLegsBox.IsChecked = _sourceProduct.HasLegs;
        LegHeightBox.Value = _sourceProduct.LegHeight;
        UpdateLegHeightVisibility();
        UpdateImagePreview();

        for (var i = 0; i < _fixedNameBoxes.Count; i++)
        {
            var row = _sourceProduct.FixedSizes.ElementAtOrDefault(i) ?? new FixedSize();
            _fixedNameBoxes[i].Text = row.Name;
            _fixedValueBoxes[i].Value = row.Value;
        }
        _loading = false;
    }

    private async void PickImageClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        var resized = await ImageResizeService.FromFileAsync(file.Path, 300, 300);
        if (resized is null)
        {
            await new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = UiText.Get("Не удалось загрузить изображение"), Content = UiText.Get("Выберите корректное PNG или JPEG-изображение."), CloseButtonText = UiText.Get("Понятно") }.ShowAsync();
            return;
        }
        _sourceProduct.Image = resized;
        UpdateImagePreview();
    }

    private async void UpdateImagePreview()
    {
        var request = _imagePreviewRequests.Begin();
        var path = _sourceProduct.Image;
        if (path.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && ProductImageData.Read(path) is { } original)
        {
            var resized = await ImageResizeService.ResizeAsync(original, 300, 300);
            if (!_imagePreviewRequests.IsCurrent(request) || _sourceProduct.Image != path) return;
            if (!string.IsNullOrWhiteSpace(resized) && resized != path)
            {
                _sourceProduct.Image = resized;
                path = resized;
            }
        }
        await ImagePreviewService.SetVisibleAsync(ProductImagePreview, path, loaded =>
        {
            if (!_imagePreviewRequests.IsCurrent(request) || _sourceProduct.Image != path) return;
            ProductImageStatusText.Text = loaded ? (path.StartsWith("data:") ? "Изображение изделия" : Path.GetFileName(path)) : UiText.Get("Изображение не выбрано или недоступно");
        }, () => _imagePreviewRequests.IsCurrent(request) && _sourceProduct.Image == path);
    }
    private void RefreshDetails(Detail? select = null)
    {
        EdgeBandingService.Refresh(App.State, _sourceProduct);
        if (_catalogEditor) ProjectDetailPricing.Clear(_sourceProduct);
        else ProjectDetailPricing.Capture(App.State, _sourceProduct);
        using var measurement = App.UiPerformance.Measure(UiWork.ProductRows);
        var selectedId = select?.Id ?? _selectedDetailRow?.Detail.Id;
        foreach (var flyout in _formulaFlyouts.Values)
        {
            if (flyout.IsOpen) flyout.Hide();
        }
        _formulaQueries.Clear();
        _formulaFields.Clear();
        using (App.UiPerformance.Measure(UiWork.ProductRowData))
        {
            var materialChoices = App.State.MaterialChoices().ToList();
            _detailRows = new ObservableCollection<DetailRow>(_sourceProduct.Details.Select(detail => CreateDetailRow(detail, materialChoices)));
        }
        using (App.UiPerformance.Measure(UiWork.ProductRowControls))
        {
            DetailsRowsPanel.ItemsSource = _detailRows;
        }
        using (App.UiPerformance.Measure(UiWork.ProductRowLayout))
        {
            _selectedDetailRow = _detailRows.FirstOrDefault(row => row.Detail.Id == selectedId) ?? _detailRows.FirstOrDefault();
            UpdateDetailSelectionVisuals();
            UpdateDetailHeaders();
            if (DetailsRowsPanel.Layout is StackLayout layout)
                layout.Spacing = App.Preferences.Current.CompactMode ? UiLayout.CompactGap(4) : 4;
            UpdateDetailColumns();
        }
    }

    private DetailRow CreateDetailRow(Detail detail, List<MaterialChoice> materialChoices)
    {
        var row = new DetailRow(detail, materialChoices);
        UpdateDetailRowCalculation(row);
        return row;
    }

    private void UseCatalogProductClick(object sender, RoutedEventArgs e)
    {
        if (CatalogProductBox.SelectedItem is not Product product) return;
        var copy = CloneProduct(product);
        _sourceProduct.Name = copy.Name;
        _sourceProduct.Image = copy.Image;
        _sourceProduct.Length = copy.Length;
        _sourceProduct.Depth = copy.Depth;
        _sourceProduct.Height = copy.Height;
        _sourceProduct.Qty = 1;
        _sourceProduct.HasLegs = copy.HasLegs;
        _sourceProduct.LegHeight = copy.LegHeight;
        _sourceProduct.FixedSizes = copy.FixedSizes;
        _sourceProduct.Details = copy.Details;
        _sourceProduct.SourceCatalogProductId = product.Id;
        FillProduct();
        RefreshDetails();
        UpdateSummary();
    }

    private void AddDetailClick(object sender, RoutedEventArgs e)
    {
        using var measurement = App.UiPerformance.Measure(UiWork.ProductRowAppend);
        if (App.UiPerformance.Enabled)
        {
            var frame = App.UiPerformance.Measure(UiWork.ProductRowAppendFrame, asynchronous: true);
            EventHandler<object>? rendered = null;
            rendered = (_, _) => { CompositionTarget.Rendering -= rendered; frame.Dispose(); };
            CompositionTarget.Rendering += rendered;
        }
        foreach (var target in _formulaFlyouts.Keys.ToList()) HideFormulaSuggestions(target);
        SaveProductFields();
        var detail = new Detail
        {
            Name = "Новая деталь",
            Type = "",
            MaterialId = "",
            LengthExpr = "длина изделия",
            WidthExpr = "глубина изделия"
        };
        _sourceProduct.Details.Add(detail);
        var row = CreateDetailRow(detail, _detailRows.FirstOrDefault()?.MaterialChoices ?? App.State.MaterialChoices().ToList());
        _detailRows.Add(row);
        _selectedDetailRow = row;
        UpdateDetailSelectionVisuals();
        UpdateDetailHeaders();
        UpdateDetailColumns();
        UpdateSummary();
        ScrollDetailIntoView(_selectedDetailRow);
    }

    private void ScrollDetailIntoView(DetailRow? row)
    {
        if (row is null) return;
        var request = _detailBringRequests.Begin();
        DetailsRowsPanel.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_detailBringRequests.IsCurrent(request)) return;
            var index = _detailRows.IndexOf(row);
            if (index < 0) return;
            if (DetailsRowsPanel.GetOrCreateElement(index) is not FrameworkElement container) return;
            void BringIntoView()
            {
                if (!_detailBringRequests.IsCurrent(request)) return;
                container.StartBringIntoView(new BringIntoViewOptions
                {
                    AnimationDesired = false,
                    VerticalAlignmentRatio = 0.5
                });
                // Virtualized variable-height rows can change the scroll extent after
                // the initial bring request. Correct only the remaining clipped edge.
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (!_detailBringRequests.IsCurrent(request) || !container.IsLoaded || _selectedDetailRow != row) return;
                    var bounds = container.TransformToVisual(ProductScrollViewer).TransformBounds(
                        new Windows.Foundation.Rect(0, 0, container.ActualWidth, container.ActualHeight));
                    var overflow = bounds.Bottom - (ProductScrollViewer.ActualHeight - 8);
                    if (overflow > 1) ProductScrollViewer.ChangeView(null, ProductScrollViewer.VerticalOffset + overflow, null, true);
                });
            }
            if (container.IsLoaded) BringIntoView();
            else
            {
                RoutedEventHandler? loaded = null;
                loaded = (_, _) => { container.Loaded -= loaded; BringIntoView(); };
                container.Loaded += loaded;
            }
        });
    }

    private void DetailElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        using var measurement = App.UiPerformance.Measure(UiWork.ProductRowRealize);
        if (args.Element is not ContentControl host || args.Index < 0 || args.Index >= _detailRows.Count) return;
        var row = _detailRows[args.Index];
        if (host.Tag is DetailRowView existing)
        {
            using var reuseMeasurement = App.UiPerformance.Measure(UiWork.ProductRowReuse);
            existing.Unbind();
            existing.Bind(row);
            var preferences = App.Preferences.Current;
            if (existing.CompactMode != preferences.CompactMode || existing.AnimationsEnabled != preferences.AnimationsEnabled)
            {
                ThemeService.ApplyLayout(existing.Container, preferences);
                existing.CompactMode = preferences.CompactMode;
                existing.AnimationsEnabled = preferences.AnimationsEnabled;
            }
        }
        else
        {
            using var createMeasurement = App.UiPerformance.Measure(UiWork.ProductRowCreate);
            var view = CreateDetailRowView(row);
            host.Tag = view;
            host.Content = view.Container;
            ThemeService.ApplyLayout(view.Container, App.Preferences.Current);
        }
        if (row.Container?.Child is Grid grid) ApplyDetailColumns(grid, DetailTableLayout.ColumnWidths(DetailsHeaderGrid.ActualWidth));
        UpdateDetailSelectionVisual(row);
    }

    private void DetailElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
    {
        if (args.Element is ContentControl { Tag: DetailRowView view }) view.Unbind();
        // Keep the native control subtree in the recycled template container.
        // ItemsRepeater supplies that same container when another detail enters view.
    }

    private void CloseDetailRowFlyouts(DetailRow row)
    {
        _materialFocusRequests.Begin();
        row.MaterialFlyout?.Hide();
        row.EdgeBandFlyout?.Hide();
        if (row.Container?.ContextFlyout is { IsOpen: true } menu) menu.Hide();
        foreach (var field in row.FormulaBoxes) HideFormulaSuggestions(field);
    }

    private void ReleaseDetailRowView(DetailRow row)
    {
        CloseDetailRowFlyouts(row);
        foreach (var field in row.FormulaBoxes)
        {
            if (_formulaSuggestionLists.TryGetValue(field, out var suggestions)) suggestions.ItemsSource = null;
            _formulaQueries.Remove(field);
            _formulaFields.Remove(field);
            field.Tag = null;
        }
        row.FormulaBoxes.Clear();
        row.Container = null;
        row.MaterialButton = null;
        row.MaterialFlyout = null;
        row.EdgeBandFlyout = null;
        row.CalculationText = null;
        row.EdgeBandButton = null;
    }
    private async void DeleteDetailClick(object sender, RoutedEventArgs e)
    {
        if (_selectedDetailRow is not { } row) return;
        if (!await ConfirmDeleteAsync(UiText.Get("Удалить деталь?"),row.Detail.Name)) return;
        if (row.Detail.IsGeneratedEdgeBand) EdgeBandingService.RemoveMaterial(_sourceProduct, row.Detail.MaterialId);
        _sourceProduct.Details.Remove(row.Detail);
        RefreshDetails();
        UpdateSummary();
        NotificationCenter.Publish(UiText.Get("Деталь удалена"),UiText.Format($"Деталь «{row.Detail.Name}» удалена из изделия."));
    }

    private void DetailRowChanged(object sender, object e)
    {
        if (_loading) return;
        UpdateSummary();
        RefreshOpenFormulaSuggestions();
    }

    private void DetailRowNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading) return;
        UpdateSummary();
    }

    private void ProductFieldChanged(object sender, object e)
    {
        if (_loading) return;
        CommitDetailRows();
        UpdateSummary();
        RefreshOpenFormulaSuggestions();
    }

    private void ProductNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading) return;
        CommitDetailRows();
        UpdateSummary();
        RefreshOpenFormulaSuggestions();
    }

    private void ProductToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        UpdateLegHeightVisibility();
        if (HasLegsBox.IsChecked == true) EnsureLegDetail();
        CommitDetailRows();
        RefreshDetails();
        UpdateSummary();
        RefreshOpenFormulaSuggestions();
    }

    private void UpdateLegHeightVisibility() =>
        LegHeightBox.Visibility = HasLegsBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void EnsureLegDetail()
    {
        var hasLeg = _sourceProduct.Details.Any(detail =>
            App.State.FindMaterial(detail.Type, detail.MaterialId)?.Name.Contains("ножка", StringComparison.CurrentCultureIgnoreCase) == true);
        if (hasLeg) return;

        var match = App.State.Database.Materials
            .SelectMany(group => group.Value.Select(material => new MaterialChoice(group.Key, material)))
            .FirstOrDefault(choice => choice.Material.Name.Contains("ножка пластик", StringComparison.CurrentCultureIgnoreCase));

        if (match is null)
        {
            var group = App.State.Database.Materials.Keys.FirstOrDefault(key => key.Contains("фурнит", StringComparison.CurrentCultureIgnoreCase)) ?? "Фурнитура";
            var material = new Material { Name = "Ножка пластик", Unit = "pc", Cost = 0 };
            App.State.CommitMutation(() =>
            {
                App.State.Database.Materials.TryAdd(group, []);
                App.State.Database.Materials[group].Add(material);
                if (!App.State.Database.MaterialGroups.Contains(group)) App.State.Database.MaterialGroups.Add(group);
            });
            match = new MaterialChoice(group, material);
            _ = ShowLegMaterialNoticeAsync();
        }

        _sourceProduct.Details.Add(new Detail
        {
            Name = "Ножка",
            Qty = 4,
            Type = match.Type,
            MaterialId = match.Material.Id,
            LengthExpr = "высота ножки",
            WidthExpr = "0"
        });
    }

    private async Task ShowLegMaterialNoticeAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.ActualTheme,
            Title = UiText.Get("Материал добавлен"),
            Content = UiText.Get("Материал «Ножка пластик» отсутствовал. Он создан автоматически: шт., 0 руб."),
            CloseButtonText = UiText.Get("Понятно")
        };
        await dialog.ShowAsync();
    }
    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        SaveProductFields();
        if (ProductValidation.Error(App.State, _sourceProduct) is { } error)
        {
            await new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = UiText.Get("Проверьте изделие"), Content = error, CloseButtonText = UiText.Get("Понятно") }.ShowAsync();
            return;
        }
        try
        {

        var savedProduct = CloneProduct(_sourceProduct);
        DetailOrdering.SortForSave(savedProduct);
        if (_isNew && !_catalogEditor && _catalog is not null)
        {
            var exists = _catalog.GetProducts(_project.Counterparty)
                .Any(product => string.Equals(product.Name, _sourceProduct.Name, StringComparison.CurrentCultureIgnoreCase));
            if (!exists)
            {
                var decision = new ContentDialog
                {
                    XamlRoot = Root.XamlRoot,
                    RequestedTheme = Root.ActualTheme,
                    Title = UiText.Get("Добавить в каталог?"),
                    Content = UiText.Get("Изделие с таким названием отсутствует в каталоге этого заказчика."),
                    PrimaryButtonText = UiText.Get("В каталог и проект"),
                    SecondaryButtonText = UiText.Get("Только в проект"),
                    CloseButtonText = UiText.Get("Отмена"),
                    DefaultButton = ContentDialogButton.Primary
                };
                var result = await decision.ShowAsync();
                if (result == ContentDialogResult.None) return;
                if (result == ContentDialogResult.Primary)
                    _catalog.SaveProduct(_project.Counterparty, null, CloneProduct(savedProduct));
            }
        }

        ProductSaved?.Invoke(savedProduct, _isNew);
        Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = UiText.Get("Не удалось сохранить"), Content = ex.Message, CloseButtonText = UiText.Get("Закрыть") }.ShowAsync();
        }
    }
    private void CancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SaveProductFields()
    {
        _sourceProduct.Name = ProductNameBox.Text.Trim();
        _sourceProduct.Length = ProductLengthBox.Value;
        _sourceProduct.Depth = ProductDepthBox.Value;
        _sourceProduct.Height = ProductHeightBox.Value;
        _sourceProduct.Qty = ProductQtyBox.Value;
        _sourceProduct.HasLegs = HasLegsBox.IsChecked == true;
        _sourceProduct.LegHeight = LegHeightBox.Value;
        _sourceProduct.FixedSizes = _fixedNameBoxes.Select((box, index) => new FixedSize
        {
            Name = box.Text.Trim(),
            Value = _fixedValueBoxes[index].Value
        }).ToList();
    }

    private void CommitDetailRows()
    {
        SaveProductFields();
        foreach (var row in _detailRows)
        {
            row.CommitMaterial();
            row.Detail.Name = Empty(row.Detail.Name, "Новая деталь");
            row.Detail.LengthExpr = Empty(row.Detail.LengthExpr, "0");
            row.Detail.WidthExpr = Empty(row.Detail.WidthExpr, "0");
            row.Detail.Qty = row.Detail.Qty <= 0 ? 1 : row.Detail.Qty;
            UpdateDetailRowCalculation(row);
        }
    }

    private void UpdateSummary()
    {
        using var measurement = App.UiPerformance.Measure(UiWork.ProductSummary);
        SaveProductFields();
        SyncEdgeBandRows();
        if (!_catalogEditor) ProjectDetailPricing.Capture(App.State, _sourceProduct);
        var area = 0d;
        var cost = 0d;
        var quantity = _sourceProduct.Qty <= 0 ? 1 : _sourceProduct.Qty;
        foreach (var row in _detailRows)
        {
            var calc = UpdateDetailRowCalculation(row);
            area += calc.Area * quantity;
            cost += calc.Cost * quantity;
        }
        SummaryAreaText.Text = UiText.Format($"{Calculator.Number(area)} м²");
        SummaryCostText.Text = Calculator.Money(cost);
        RefreshFormulaLabels();
    }

    private DetailCalc UpdateDetailRowCalculation(DetailRow row)
    {
        var calc = Calculator.Detail(App.State, _sourceProduct, row.Detail);
        row.CalculationResult = calc;
        var unit = row.SelectedMaterial?.Material.Unit;
        var size = unit == "m2" ? $"{Calculator.Number(calc.Length)} × {Calculator.Number(calc.Width)} мм · "
            : unit == "lm" ? $"{Calculator.Number(calc.Length)} мм · " : "";
        row.Calculation = string.IsNullOrEmpty(calc.Error) ? size + Calculator.Money(calc.Cost) : calc.Error;
        if (row.CalculationText is not null)
        {
            row.CalculationText.Text = row.Calculation;
            row.CalculationText.Foreground = (Brush)Application.Current.Resources[string.IsNullOrEmpty(calc.Error) ? "AppTextBrush" : "UiDangerBrush"];
            ToolTipService.SetToolTip(row.CalculationText,row.Calculation);
        }
        return calc;
    }

    private DetailRowView CreateDetailRowView(DetailRow row)
    {
        var rebinding = false;
        var border = new Border
        {
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Colors.Transparent),
            Tag = row
        };
        // Explicit row bounds let reveal skip offscreen subtrees instead of walking every editor control.
        var rowClip = new RectangleGeometry();
        border.Clip = rowClip;
        border.SizeChanged += (_, _) => rowClip.Rect = new Windows.Foundation.Rect(0, 0, border.ActualWidth, border.ActualHeight);
        border.Tapped += DetailRowTapped;
        border.GotFocus += (_, _) =>
        {
            if (border.Tag is DetailRow focusedRow) SelectDetailRow(focusedRow);
        };
        border.ContextRequested += (_, args) =>
        {
            var menu = border.ContextFlyout ??= CreateDetailContextMenu(row);
            if (args.TryGetPosition(border, out var point))
                menu.ShowAt(border, new FlyoutShowOptions { Position = point });
            else menu.ShowAt(border);
            args.Handled = true;
        };
        row.Container = border;

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        var nameBox = new TextBox { Text = row.Detail.Name, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(nameBox,UiText.Get("Название детали"));
        nameBox.TextChanged += (_, _) =>
        {
            if (rebinding || border.Tag is null || row.Detail.Name == nameBox.Text) return;
            row.Detail.Name = nameBox.Text;
            DetailRowChanged(nameBox, EventArgs.Empty);
        };
        var nameCell = new Grid { ColumnSpacing = 6 };
        nameCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        nameCell.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameCell.Children.Add(nameBox);
        var edgeButton = new Button { Content = UiText.Get("Кромка"), VerticalAlignment = VerticalAlignment.Center };
        row.EdgeBandButton = edgeButton;
        edgeButton.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, args) => row.EdgeBandPointer = args.GetCurrentPoint(Root).Position), true);
        edgeButton.KeyDown += (_, _) => row.EdgeBandPointer = null;
        edgeButton.Click += (_, _) => OpenEdgeBanding(row, edgeButton);
        Grid.SetColumn(edgeButton, 1);
        nameCell.Children.Add(edgeButton);
        grid.Children.Add(nameCell);
        UpdateEdgeBandButton(row);

        var materialContent = new Grid { ColumnSpacing = 8 };
        materialContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        materialContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var materialImage = new Image
        {
            Width = 25,
            Height = 25,
            Stretch = Stretch.UniformToFill,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        materialContent.Children.Add(materialImage);
        var materialName = new TextBlock { Text = row.SelectedMaterial?.Material.Name ?? "Выберите материал", TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(materialName, 1);
        materialContent.Children.Add(materialName);
        _ = LoadMaterialTextureAsync(materialImage, row.SelectedMaterial?.Material.Texture);
        var materialButton = new Button
        {
            Content = materialContent,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        row.MaterialButton = materialButton;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(materialButton,UiText.Get("Материал детали ") + row.Detail.Name);
        materialButton.Click += (_, _) =>
        {
            row.MaterialFlyout ??= CreateMaterialFlyout(row, materialButton);
            row.MaterialFlyout.ShowAt(materialButton);
        };
        ToolTipService.SetToolTip(materialButton,row.SelectedMaterial?.Material.Name ?? "Выберите материал");
        Grid.SetColumn(materialButton, 1);
        grid.Children.Add(materialButton);
        var qtyBox = new NumberBox { Value = row.Detail.Qty <= 0 ? 1 : row.Detail.Qty, Minimum = 1, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(qtyBox,UiText.Get("Количество деталей ") + row.Detail.Name);
        qtyBox.ValueChanged += (_, _) =>
        {
            if (rebinding || border.Tag is null || (row.Detail.Qty <= 0 ? 1 : row.Detail.Qty).Equals(qtyBox.Value)) return;
            row.Detail.Qty = qtyBox.Value;
            DetailRowNumberChanged(qtyBox, null!);
        };
        Grid.SetColumn(qtyBox, 2);
        grid.Children.Add(qtyBox);

        var isSheetMaterial = row.SelectedMaterial?.Material.Unit == "m2";
        var isLinearMaterial = row.SelectedMaterial?.Material.Unit == "lm";
        var lengthBox = CreateFormulaBox(row, false, isSheetMaterial || isLinearMaterial);
        Grid.SetColumn(lengthBox, 3);
        grid.Children.Add(lengthBox);

        var swapDimensions = new Button
        {
            Content = new FontIcon { Glyph = "\uE8AB", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16 },
            Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = isSheetMaterial ? Visibility.Visible : Visibility.Collapsed
        };
        var swapLabel = UiText.Get("Поменять местами длину и ширину");
        ToolTipService.SetToolTip(swapDimensions, swapLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(swapDimensions, swapLabel);
        swapDimensions.Click += (_, _) =>
        {
            foreach (var target in _formulaFlyouts.Keys.ToList()) HideFormulaSuggestions(target);
            if (!DetailDimensions.Swap(_sourceProduct, row.Detail, row.SelectedMaterial?.Material)) return;
            UpdateSummary();
        };
        Grid.SetColumn(swapDimensions, 4);
        grid.Children.Add(swapDimensions);

        var widthBox = CreateFormulaBox(row, true, isSheetMaterial);
        Grid.SetColumn(widthBox, 5);
        grid.Children.Add(widthBox);

        var rotationBox = new ToggleSwitch
        {
            IsOn = DetailRotation.CanRotate(row.Detail, row.SelectedMaterial?.Material),
            IsEnabled = isSheetMaterial,
            OnContent = "",
            OffContent = "",
            MinWidth = 40,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = isSheetMaterial ? Visibility.Visible : Visibility.Collapsed
        };
        var rotationCell = new Grid { Visibility = isSheetMaterial ? Visibility.Visible : Visibility.Collapsed, ColumnSpacing = 2 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(rotationBox,UiText.Get("Разрешить поворот детали ") + row.Detail.Name);
        rotationCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        rotationCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        var rotationLock = new FontIcon { Glyph = "\uE72E", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(rotationLock, 1);
        void UpdateRotationIndicator()
        {
            rotationLock.Visibility = DetailRotation.IsLocked(row.Detail, row.SelectedMaterial?.Material) ? Visibility.Visible : Visibility.Collapsed;
            ToolTipService.SetToolTip(rotationCell,DetailRotation.Reason(row.Detail, row.SelectedMaterial?.Material));
        }
        rotationBox.Toggled += (_, _) =>
        {
            if (_updatingMaterialView || rebinding || border.Tag is null || row.Detail.AllowRotation == rotationBox.IsOn) return;
            row.Detail.AllowRotation = rotationBox.IsOn;
            UpdateRotationIndicator();
            DetailRowChanged(rotationBox, EventArgs.Empty);
        };
        UpdateRotationIndicator();
        rotationCell.Children.Add(rotationBox);
        rotationCell.Children.Add(rotationLock);
        Grid.SetColumn(rotationCell, 6);
        grid.Children.Add(rotationCell);

        var calculationText = new TextBlock
        {
            Text = row.Calculation,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        row.CalculationText = calculationText;
        FrameworkElement calculationCell = calculationText;
        Button? priceButton = null;
        if (!_catalogEditor)
        {
            priceButton = new Button
            {
                Content = calculationText, MinWidth = 0, MinHeight = 0, Padding = new Thickness(4),
                BorderThickness = new Thickness(0), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                HorizontalContentAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center
            };
            priceButton.Click += async (_, _) => await EditProjectDetailPriceAsync(row);
            ToolTipService.SetToolTip(priceButton, UiText.Get("Цена детали в проекте"));
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(priceButton, UiText.Get("Цена детали в проекте") + ": " + row.Detail.Name);
            calculationCell = priceButton;
        }
        Grid.SetColumn(calculationCell, 7);
        grid.Children.Add(calculationCell);

        border.Child = grid;
        if (row.Detail.IsGeneratedEdgeBand)
        {
            nameBox.IsReadOnly = true;
            foreach (var control in grid.Children.OfType<Control>()) control.IsEnabled = false;
            materialButton.IsEnabled = true;
            if (priceButton is not null) priceButton.IsEnabled = true;
            ToolTipService.SetToolTip(border,UiText.Get("Длина кромки рассчитывается по выбранным граням деталей."));
        }
        return new DetailRowView(border, next =>
        {
            rebinding = true;
            var refreshing = _refreshingFormulaText;
            _refreshingFormulaText = true;
            try
            {
                row = next;
                border.Tag = row;
                border.ContextFlyout = null;
                row.Container = border;
                row.MaterialButton = materialButton;
                row.EdgeBandButton = edgeButton;
                row.CalculationText = calculationText;
                nameBox.Text = row.Detail.Name;
                nameBox.IsReadOnly = row.Detail.IsGeneratedEdgeBand;
                qtyBox.Value = row.Detail.Qty <= 0 ? 1 : row.Detail.Qty;
                materialName.Text = row.SelectedMaterial?.Material.Name ?? UiText.Get("Выберите материал");
                materialImage.Source = null;
                materialImage.Visibility = Visibility.Collapsed;
                _ = LoadMaterialTextureAsync(materialImage, row.SelectedMaterial?.Material.Texture);
                foreach (var control in grid.Children.OfType<Control>()) control.IsEnabled = !row.Detail.IsGeneratedEdgeBand;
                materialButton.IsEnabled = true;
                if (priceButton is not null) priceButton.IsEnabled = true;
                BindFormulaBox(lengthBox, row, false);
                BindFormulaBox(widthBox, row, true);
                UpdateMaterialRowView(row, updateHeaders: false);
                calculationText.Text = row.Calculation;
                calculationText.Foreground = (Brush)Application.Current.Resources[string.IsNullOrEmpty(row.CalculationResult?.Error) ? "AppTextBrush" : "UiDangerBrush"];
                ToolTipService.SetToolTip(calculationText, row.Calculation);
                ToolTipService.SetToolTip(border, row.Detail.IsGeneratedEdgeBand ? UiText.Get("Длина кромки рассчитывается по выбранным граням деталей.") : null);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(materialButton, UiText.Get("Материал детали ") + row.Detail.Name);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(qtyBox, UiText.Get("Количество деталей ") + row.Detail.Name);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(rotationBox, UiText.Get("Разрешить поворот детали ") + row.Detail.Name);
                if (priceButton is not null) Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(priceButton, UiText.Get("Цена детали в проекте") + ": " + row.Detail.Name);
            }
            finally { _refreshingFormulaText = refreshing; rebinding = false; }
        }, () =>
        {
            if (border.Tag is null) return;
            ReleaseDetailRowView(row);
            border.Tag = null;
            border.ContextFlyout = null;
            materialImage.Source = null;
            _ = LoadMaterialTextureAsync(materialImage, null);
        });
    }

    private async Task EditProjectDetailPriceAsync(DetailRow row)
    {
        var material = App.State.FindMaterial(row.Detail.Type, row.Detail.MaterialId);
        if (_catalogEditor || material is null) return;
        var editorPrice = ProjectDetailPricing.Editor(row.Detail, material);
        var priceHeader = editorPrice.Mode == "sheet" ? "Цена за лист" : editorPrice.Mode == "roll" ? "Цена за бухту" :
            material.Unit == "m2" ? "Цена за м²" : material.Unit == "lm" ? "Цена за погонный метр" : "Цена за единицу";
        var price = new NumberBox { Header = UiText.Get(priceHeader) + (editorPrice.Mode == "unit" ? "" :
                $" · {Calculator.Number(editorPrice.Quantity, 3)} {UnitLabel(material.Unit)}"),
            Value = editorPrice.Amount, Minimum = 0, MinWidth = 280 };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
            Foreground = (Brush)Application.Current.Resources["UiDangerBrush"] };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock { Text = row.Detail.Name, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(price);
        var wasteAllowance = new NumberBox { Header = UiText.Get("Допуск на обрезки, %"),
            Value = ProjectDetailPricing.AllowancePercent(row.Detail, material), Minimum = 0,
            Visibility = material.Unit is "m2" or "lm" ? Visibility.Visible : Visibility.Collapsed };
        content.Children.Add(wasteAllowance);
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = UiText.Get("Цена детали в проекте"), Content = content,
            MinWidth = Math.Min(540, Root.XamlRoot.Size.Width - 48),
            PrimaryButtonText = UiText.Get("Сохранить"), SecondaryButtonText = UiText.Get("Цена каталога"),
            CloseButtonText = UiText.Get("Отмена"), DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { ProjectDetailPricing.SetEnteredPrice(row.Detail, material, NumberBoxInput.Read(price), NumberBoxInput.Read(wasteAllowance)); }
            catch (InvalidOperationException exception)
            { args.Cancel = true; error.Text = UiText.Get(exception.Message); error.Visibility = Visibility.Visible; }
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Secondary)
        {
            if (MaterialPricing.Error(material).Length > 0) return;
            ProjectDetailPricing.UseCatalogPrice(row.Detail, material);
        }
        if (result != ContentDialogResult.None) UpdateSummary();
    }

    private TextBox CreateFormulaBox(DetailRow row, bool width, bool visible)
    {
        var target = new TextBox
        {
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = visible ? Visibility.Visible : Visibility.Collapsed
        };
        BindFormulaBox(target, row, width);
        target.TextChanged += (_, _) =>
        {
            if (_refreshingFormulaText || target.Tag is not DetailRow current || !_formulaFields.TryGetValue(target, out var field)) return;
            if (field.InputText == target.Text) return;
            field.InputText = target.Text;
            var expression = field.Presentation.Storage(target.Text);
            if (width) current.Detail.WidthExpr = expression;
            else current.Detail.LengthExpr = expression;
            ToolTipService.SetToolTip(target,target.Text);
            DetailRowChanged(target, EventArgs.Empty);
            UpdateFormulaSuggestions(target);
        };
        target.LostFocus += (_, _) => RefreshFormulaLabels();
        target.SelectionChanged += (_, _) => { if (target.FocusState != FocusState.Unfocused) UpdateFormulaSuggestions(target); };
        return target;
    }

    private void BindFormulaBox(TextBox target, DetailRow row, bool width)
    {
        var field = new FormulaField(row.Detail, width, new FormulaPresentation(_sourceProduct));
        _formulaFields[target] = field;
        row.FormulaBoxes.Add(target);
        target.Tag = row;
        var display = field.Presentation.Display(field.Expression);
        field.InputText = display;
        target.Text = display;
        field.DisplayedExpression = field.Expression;
        field.DisplayedNamesVersion = _formulaNamesVersion;
        ToolTipService.SetToolTip(target, display);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(target, (width ? "Ширина или формула: " : "Длина или формула: ") + row.Detail.Name);
    }

    private void RefreshFormulaLabels()
    {
        if (_refreshingFormulaText || _insertingReference) return;
        using var measurement = App.UiPerformance.Measure(UiWork.ProductFormulaLabels);
        var names = string.Join("\0", _sourceProduct.Details.Select(detail => detail.Id + ":" + detail.Name));
        if (names != _formulaNamesSignature) { _formulaNamesSignature = names; _formulaNamesVersion++; }
        _refreshingFormulaText = true;
        try
        {
            foreach (var (target, field) in _formulaFields)
            {
                if (target.FocusState != FocusState.Unfocused) continue;
                if (_formulaFlyouts.TryGetValue(target, out var suggestions) && suggestions.IsOpen) continue;
                if (field.DisplayedExpression == field.Expression && field.DisplayedNamesVersion == _formulaNamesVersion) continue;
                var display = field.Presentation.Display(field.Expression);
                field.InputText = display;
                if (target.Text != display) target.Text = display;
                ToolTipService.SetToolTip(target,display);
                field.DisplayedExpression = field.Expression;
                field.DisplayedNamesVersion = _formulaNamesVersion;
            }
        }
        finally { _refreshingFormulaText = false; }
    }

    private void ConfigureFormulaSuggestions(TextBox target, DetailRow row)
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            OverlayInputPassThroughElement = Root,
            ShowMode = FlyoutShowMode.Transient
        };
        var list = new ListView
        {
            Width = 400, MaxHeight = 320, IsItemClickEnabled = true,
            SelectionMode = ListViewSelectionMode.Single,
            ItemTemplate = (DataTemplate)Application.Current.Resources["FormulaReferenceTemplate"]
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list,UiText.Get("Ссылки на размеры"));
        flyout.Content = list;
        _formulaSuggestionLists[target] = list;
        _formulaFlyouts[target] = flyout;
        target.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Tab) HideFormulaSuggestions(target);
        };
        list.PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Tab) ReturnToFormula(target);
        };
        target.LostFocus += (_, _) => target.DispatcherQueue.TryEnqueue(CloseInactiveFormulaSuggestions);
        list.LostFocus += (_, _) => target.DispatcherQueue.TryEnqueue(CloseInactiveFormulaSuggestions);
        list.ItemClick += (_, args) => InsertFormulaSuggestion(target, (FormulaReference)args.ClickedItem);
        list.KeyDown += (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter && list.SelectedItem is FormulaReference reference)
            { InsertFormulaSuggestion(target, reference); args.Handled = true; }
            else if (args.Key == Windows.System.VirtualKey.Escape)
            { ReturnToFormula(target); args.Handled = true; }
        };
        flyout.Closed += (_, _) =>
        {
            if (!flyout.IsOpen) _formulaQueries.Remove(target);
            RefreshFormulaLabels();
        };
        target.KeyDown += (_, args) =>
        {
            if (!flyout.IsOpen) return;
            if (args.Key == Windows.System.VirtualKey.Escape)
            { ReturnToFormula(target); args.Handled = true; }
            else if (args.Key == Windows.System.VirtualKey.Down && list.Items.Count > 0)
            { list.Focus(FocusState.Keyboard); args.Handled = true; }
        };
    }

    private void ReturnToFormula(TextBox target)
    {
        if (!_formulaFlyouts.TryGetValue(target, out var flyout)) return;
        var caret = _formulaQueries.TryGetValue(target, out var query) ? query.Caret : target.SelectionStart;
        _insertingReference = true;
        try
        {
            HideFormulaSuggestions(target);
            target.Focus(FocusState.Programmatic);
            target.SelectionStart = Math.Clamp(caret, 0, target.Text.Length);
            target.SelectionLength = 0;
        }
        finally { _insertingReference = false; }
    }

    private void InsertFormulaSuggestion(TextBox target, FormulaReference reference)
    {
        if (!_formulaFlyouts.TryGetValue(target, out var suggestions) || !suggestions.IsOpen || !FormulaSuggestionsOwnFocus(target) ||
            !_formulaQueries.TryGetValue(target, out var query) || target.Text != query.Text ||
            !_formulaFields.TryGetValue(target, out var field)) return;
        _insertingReference = true;
        try
        {
            var edit = FormulaSuggestionInsertion.Apply(query.Text, query.Start, query.Caret, field.Presentation.Display(reference.Expression));
            target.Text = edit.Text;
            HideFormulaSuggestions(target);
            target.Focus(FocusState.Programmatic);
            target.SelectionStart = edit.Caret;
            target.SelectionLength = 0;
        }
        finally { _insertingReference = false; }
    }

    private void UpdateFormulaSuggestions(TextBox target)
    {
        if (_loading || _refreshingFormulaText || _insertingReference) return;
        if (!FormulaSuggestionsOwnFocus(target))
        {
            HideFormulaSuggestions(target);
            return;
        }

        var text = target.Text;
        var caret = Math.Clamp(target.SelectionStart, 0, text.Length);
        var queryStart = FormulaEditing.QueryStart(text, caret);
        if (queryStart < 0)
        {
            HideFormulaSuggestions(target);
            return;
        }

        if (!_formulaFlyouts.TryGetValue(target, out var flyout))
        {
            if (target.Tag is not DetailRow row) return;
            ConfigureFormulaSuggestions(target, row);
            flyout = _formulaFlyouts[target];
        }

        var query = FormulaText.NormalizeWhitespace(text[queryStart..caret]);
        var currentRow = target.Tag as DetailRow;
        var references = FormulaReferences(currentRow)
            .Where(reference => UiTextSearch.Matches(query,
                FormulaText.NormalizeWhitespace(reference.Label), FormulaText.NormalizeWhitespace(reference.Expression), reference.Section))
            .ToList();
        if (references.Count == 0)
        {
            HideFormulaSuggestions(target);
            return;
        }

        var list = _formulaSuggestionLists[target];
        var selected = (list.SelectedItem as FormulaReference)?.Expression;
        if (list.ItemsSource is not IEnumerable<FormulaReference> previous || !previous.SequenceEqual(references))
            list.ItemsSource = references;
        list.SelectedItem = references.FirstOrDefault(reference => reference.Expression == selected) ?? references[0];
        _formulaQueries[target] = new FormulaQuery(text, queryStart, caret);
        foreach (var other in _formulaFlyouts.Keys.Where(other => other != target).ToList()) HideFormulaSuggestions(other);
        if (!flyout.IsOpen) flyout.ShowAt(target);
    }

    private void HideFormulaSuggestions(TextBox target)
    {
        _formulaQueries.Remove(target);
        if (_formulaFlyouts.TryGetValue(target, out var flyout) && flyout.IsOpen) flyout.Hide();
    }

    private bool FormulaSuggestionsOwnFocus(TextBox target)
    {
        if (target.XamlRoot is null) return false;
        var focused = FocusManager.GetFocusedElement(target.XamlRoot) as DependencyObject;
        _formulaSuggestionLists.TryGetValue(target, out var list);
        while (focused is not null)
        {
            if (focused == target || focused == list) return true;
            focused = VisualTreeHelper.GetParent(focused);
        }
        return false;
    }

    private void CloseInactiveFormulaSuggestions()
    {
        if (_insertingReference) return;
        foreach (var (target, flyout) in _formulaFlyouts)
            if (flyout.IsOpen && !FormulaSuggestionsOwnFocus(target)) HideFormulaSuggestions(target);
        RefreshFormulaLabels();
    }

    private void RefreshOpenFormulaSuggestions()
    {
        foreach (var (target, flyout) in _formulaFlyouts)
        {
            if (flyout.IsOpen) UpdateFormulaSuggestions(target);
        }
    }

    private IEnumerable<FormulaReference> FormulaReferences(DetailRow? currentRow)
    {
        yield return new FormulaReference(UiText.Get("Изделие"), UiText.Get("Длина"), "длина изделия", UiText.Format($"{Calculator.Number(ProductLengthBox.Value, 0)} мм"));
        yield return new FormulaReference(UiText.Get("Изделие"), UiText.Get("Глубина"), "глубина изделия", UiText.Format($"{Calculator.Number(ProductDepthBox.Value, 0)} мм"));
        yield return new FormulaReference(UiText.Get("Изделие"), UiText.Get("Высота"), "высота изделия", UiText.Format($"{Calculator.Number(ProductHeightBox.Value, 0)} мм"));
        if (HasLegsBox.IsChecked == true)
            yield return new FormulaReference(UiText.Get("Изделие"), UiText.Get("Ножка"), "высота ножки", UiText.Format($"{Calculator.Number(LegHeightBox.Value, 0)} мм"));

        for (var index = 0; index < _fixedNameBoxes.Count; index++)
        {
            var name = _fixedNameBoxes[index].Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            yield return new FormulaReference(UiText.Get("Фиксированные размеры"), name, name, UiText.Format($"{Calculator.Number(_fixedValueBoxes[index].Value, 0)} мм"));
        }

        foreach (var reference in DetailFormulaReferences(currentRow))
        {
            yield return reference;
        }
    }

    private IEnumerable<FormulaReference> DetailFormulaReferences(DetailRow? currentRow)
    {
        foreach (var row in _detailRows.Where(row => row != currentRow))
        {
            if (row.SelectedMaterial?.Material.Unit is not ("m2" or "lm")) continue;
            var detailIndex = _detailRows.IndexOf(row) + 1;
            var detailName = string.IsNullOrWhiteSpace(row.Detail.Name)
                ? UiText.Format($"Деталь {detailIndex}")
                : row.Detail.Name.Trim();
            var calc = row.CalculationResult ?? Calculator.Detail(App.State, _sourceProduct, row.Detail);

            yield return new FormulaReference(
                UiText.Get("Детали"),
                UiText.Format($"{detailName} · длина"),
                $"[[detail:{row.Detail.Id}:length]]",
                UiText.Format($"{Calculator.Number(calc.Length, 0)} мм"));

            if (row.SelectedMaterial?.Material.Unit == "m2")
            {
                yield return new FormulaReference(
                    UiText.Get("Детали"),
                    UiText.Format($"{detailName} · ширина"),
                    $"[[detail:{row.Detail.Id}:width]]",
                    UiText.Format($"{Calculator.Number(calc.Width, 0)} мм"));
            }
        }
    }

    private void UpdateDetailHeaders()
    {
        var hasLinear = _detailRows.Any(row => row.SelectedMaterial?.Material.Unit is "m2" or "lm");
        var hasSheet = _detailRows.Any(row => row.SelectedMaterial?.Material.Unit == "m2");
        DetailLengthHeader.Visibility = hasLinear ? Visibility.Visible : Visibility.Collapsed;
        DetailWidthHeader.Visibility = hasSheet ? Visibility.Visible : Visibility.Collapsed;
        DetailRotationHeader.Visibility = hasSheet ? Visibility.Visible : Visibility.Collapsed;
    }

    public sealed record MaterialPickerOption(MaterialChoice Choice, string Name, string Caption)
    {
        public override string ToString() => $"{Name}, {Caption}";
    }

    private Flyout CreateMaterialFlyout(DetailRow row, Button button)
    {
        var flyout = new Flyout { OverlayInputPassThroughElement = Root };
        var panel = new StackPanel { Spacing = 8, Width = 320 };
        var search = new TextBox { PlaceholderText = UiText.Get("Поиск материала"), MaxLength = 200 };
        var resettingSearch = false;
        var filterTimer = DispatcherQueue.CreateTimer();
        filterTimer.Interval = TimeSpan.FromMilliseconds(120);
        var categories = new StackPanel { Spacing = 4 };
        var scroll = new ScrollViewer
        {
            Content = categories, MaxHeight = 300,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var empty = new TextBlock { Text = UiText.Get("Материалы не найдены"), Visibility = Visibility.Collapsed };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(search,UiText.Get("Поиск материала по названию и категории"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(scroll,UiText.Get("Категории материалов"));
        ListView CreateOptionsList(IReadOnlyList<MaterialPickerOption> options, string label)
        {
            var list = new ListView
            {
                MaxHeight = 240,
                IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.Single,
                ItemTemplate = (DataTemplate)Application.Current.Resources["MaterialPickerTemplate"],
                ItemsSource = options
            };
            ScrollViewer.SetVerticalScrollMode(list, ScrollMode.Enabled);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, label);
            list.SelectedItem = options.FirstOrDefault(option => option.Choice == row.SelectedMaterial);
            list.ItemClick += (_, e) => Select((MaterialPickerOption)e.ClickedItem);
            list.KeyDown += (_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Enter && list.SelectedItem is MaterialPickerOption option)
                { Select(option); e.Handled = true; }
            };
            return list;
        }
        static ListView? FirstExpandedList(StackPanel content)
        {
            foreach (var child in content.Children)
            {
                if (child is ListView list && list.Items.Count > 0) return list;
                if (child is Expander { IsExpanded: true, Content: ListView nested } && nested.Items.Count > 0) return nested;
            }
            return null;
        }
        void Filter()
        {
            using var measurement = App.UiPerformance.Measure(UiWork.MaterialPicker);
            var searching = !string.IsNullOrWhiteSpace(search.Text);
            var choices = MaterialPickerOptions().Where(option => !row.Detail.IsGeneratedEdgeBand ||
                EdgeBandingService.IsEdgeMaterial(option.Choice)).ToArray();
            var matches = choices.Where(option => UiTextSearch.Matches(search.Text, option.Name, option.Choice.Type,
                option.Choice.Material.Group)).ToArray();
            categories.Children.Clear();
            var categoryNames = row.Detail.IsGeneratedEdgeBand ? new[] { "Кромка" }.AsEnumerable() : string.IsNullOrWhiteSpace(search.Text)
                ? App.MaterialLibrary.GetGroups().Concat(choices.Select(option => option.Choice.Type)).Distinct().OrderBy(type => type)
                : matches.Select(option => option.Choice.Type).Distinct().OrderBy(type => type);
            foreach (var categoryName in categoryNames)
            {
                var options = matches.Where(option => option.Choice.Type == categoryName).ToList();
                var add = new Button { Content = UiText.Get("Добавить"), HorizontalAlignment = HorizontalAlignment.Stretch };
                if (row.Detail.IsGeneratedEdgeBand) add.Visibility = Visibility.Collapsed;
                CommandAppearance.Decorate(add);
                add.Click += async (_, _) =>
                {
                    flyout.Hide();
                    var added = await MaterialCreateDialog.ShowAsync(this, button.XamlRoot, categoryName);
                    if (added is null) return;
                    foreach (var materialChoices in _detailRows.Select(detailRow => detailRow.MaterialChoices).Distinct())
                        materialChoices.Add(added);
                    SelectRowMaterial(row, button, added);
                    NotificationCenter.Publish(UiText.Get("Готово"), UiText.Get("Материал сохранён"));
                };
                var categoryContent = new StackPanel { Spacing = 4 };
                categoryContent.Children.Add(add);
                var expander = new Expander
                {
                    Header = categoryName, Content = categoryContent,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch
                };
                var populated = false;
                expander.Expanding += (_, _) =>
                {
                    using var measurement = App.UiPerformance.Measure(UiWork.MaterialPickerExpand);
                    if (populated) return;
                    populated = true;
                    foreach (var materialGroup in options.GroupBy(option => option.Choice.Material.Group.Trim(), StringComparer.CurrentCultureIgnoreCase))
                    {
                        var groupOptions = materialGroup.ToList();
                        if (materialGroup.Key.Length == 0)
                        {
                            categoryContent.Children.Insert(categoryContent.Children.Count - 1, CreateOptionsList(groupOptions, categoryName));
                            continue;
                        }
                        var groupExpander = new Expander
                        {
                            Header = materialGroup.Key, HorizontalAlignment = HorizontalAlignment.Stretch,
                            HorizontalContentAlignment = HorizontalAlignment.Stretch
                        };
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(groupExpander, materialGroup.Key);
                        groupExpander.Expanding += (_, _) =>
                        {
                            groupExpander.Content ??= CreateOptionsList(groupOptions, $"{categoryName}: {materialGroup.Key}");
                        };
                        categoryContent.Children.Insert(categoryContent.Children.Count - 1, groupExpander);
                        groupExpander.IsExpanded = searching;
                    }
                };
                categories.Children.Add(expander);
                expander.IsExpanded = searching;
            }
            empty.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        void Select(MaterialPickerOption option)
        {
            if (!ReferenceEquals(row.MaterialButton, button) || row.Container is null) return;
            flyout.Hide();
            SelectRowMaterial(row, button, option.Choice);
        }
        filterTimer.Tick += (_, _) => { filterTimer.Stop(); if (flyout.IsOpen) Filter(); };
        search.TextChanged += (_, _) =>
        {
            if (resettingSearch || !flyout.IsOpen) return;
            filterTimer.Stop();
            filterTimer.Start();
        };
        search.KeyDown += (_, e) =>
        {
            if (filterTimer.IsRunning && e.Key is Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Enter)
            { filterTimer.Stop(); Filter(); }
            var first = categories.Children.OfType<Expander>().FirstOrDefault();
            if (e.Key == Windows.System.VirtualKey.Down && first is not null)
            {
                if (first.IsExpanded && first.Content is StackPanel content && FirstExpandedList(content) is { } list) list.Focus(FocusState.Keyboard);
                else first.Focus(FocusState.Keyboard);
                e.Handled = true;
            }
            else if (e.Key == Windows.System.VirtualKey.Enter && !string.IsNullOrWhiteSpace(search.Text) && first?.Content is StackPanel group &&
                FirstExpandedList(group) is { } matches &&
                matches.Items.OfType<MaterialPickerOption>().FirstOrDefault() is { } option)
            { Select(option); e.Handled = true; }
        };
        flyout.Opening += (_, _) =>
        {
            filterTimer.Stop();
            resettingSearch = true;
            try { search.Text = string.Empty; }
            finally { resettingSearch = false; }
            Filter();
        };
        flyout.Closed += (_, _) => filterTimer.Stop();
        flyout.Opened += (_, _) => search.Focus(FocusState.Programmatic);
        panel.Children.Add(search);
        panel.Children.Add(scroll);
        panel.Children.Add(empty);
        flyout.Content = panel;
        return flyout;
    }

    private MaterialPickerOption[] MaterialPickerOptions()
    {
        if (_materialPickerOptions is not null && ReferenceEquals(_materialPickerDatabase, App.State.Database) &&
            _materialPickerRevision == App.State.Revision && _materialPickerLanguage == UiText.Language)
            return _materialPickerOptions;
        _materialPickerDatabase = App.State.Database;
        _materialPickerRevision = App.State.Revision;
        _materialPickerLanguage = UiText.Language;
        return _materialPickerOptions = App.State.MaterialChoices().GroupBy(choice => choice.Type).OrderBy(group => group.Key)
            .SelectMany(group => MaterialCatalogOrdering.Sort(group, "name-asc"))
            .Select(choice => new MaterialPickerOption(choice, choice.Material.Name,
                $"{UnitLabel(choice.Material.Unit)} · {MaterialPricing.UnitCost(choice.Material):N2} {UiText.Get("руб.")}")).ToArray();
    }

    private void SelectRowMaterial(DetailRow row, Button button, MaterialChoice choice)
    {
        var focusRequest = _materialFocusRequests.Begin();
        var verticalOffset = ProductScrollViewer.VerticalOffset;
        if (row.Detail.IsGeneratedEdgeBand)
        {
            try
            {
                if (!EdgeBandingService.ReplaceGeneratedMaterial(App.State, _sourceProduct, row.Detail, choice)) return;
                UpdateSummary();
                var replacement = _detailRows.FirstOrDefault(item => item.Detail.IsGeneratedEdgeBand &&
                    item.Detail.MaterialId == choice.Material.Id && item.Detail.Type == choice.Type);
                _selectedDetailRow = replacement ?? _selectedDetailRow;
                UpdateDetailSelectionVisuals();
                ProductScrollViewer.DispatcherQueue.TryEnqueue(() =>
                {
                    if (!_materialFocusRequests.IsCurrent(focusRequest)) return;
                    replacement?.MaterialButton?.Focus(FocusState.Programmatic);
                    ProductScrollViewer.ChangeView(null, verticalOffset, null);
                });
            }
            catch (InvalidOperationException error)
            {
                NotificationCenter.Publish(UiText.Get("Не удалось сохранить"), UiText.Get(error.Message));
            }
            return;
        }
        row.SelectedMaterial = choice;
        row.CommitMaterial();
        if (button.Content is Grid content && content.Children.OfType<TextBlock>().FirstOrDefault() is { } label)
            label.Text = choice.Material.Name;
        if (button.Content is Grid imageContent && imageContent.Children.OfType<Image>().FirstOrDefault() is { } image)
            _ = LoadMaterialTextureAsync(image, choice.Material.Texture);
        UpdateMaterialRowView(row);
        UpdateSummary();
        ProductScrollViewer.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_materialFocusRequests.IsCurrent(focusRequest)) return;
            button.Focus(FocusState.Programmatic);
            ProductScrollViewer.ChangeView(null, verticalOffset, null);
        });
    }

    private void UpdateMaterialRowView(DetailRow row, bool updateHeaders = true)
    {
        UpdateEdgeBandButton(row);
        if (row.Container?.Child is not Grid grid) return;
        var material = row.SelectedMaterial?.Material;
        var sheet = material?.Unit == "m2";
        foreach (var child in grid.Children.OfType<FrameworkElement>())
        {
            var column = Grid.GetColumn(child);
            if (column == 3) child.Visibility = sheet || material?.Unit == "lm" ? Visibility.Visible : Visibility.Collapsed;
            if (column is 4 or 5 or 6) child.Visibility = sheet ? Visibility.Visible : Visibility.Collapsed;
            if (column != 6 || child is not Grid rotation) continue;
            _updatingMaterialView = true;
            try
            {
                var toggle = rotation.Children.OfType<ToggleSwitch>().First();
                toggle.IsEnabled = sheet;
                toggle.IsOn = DetailRotation.CanRotate(row.Detail, material);
                toggle.Visibility = sheet ? Visibility.Visible : Visibility.Collapsed;
                rotation.Children.OfType<FontIcon>().First().Visibility = DetailRotation.IsLocked(row.Detail, material)
                    ? Visibility.Visible : Visibility.Collapsed;
                ToolTipService.SetToolTip(rotation,DetailRotation.Reason(row.Detail, material));
            }
            finally { _updatingMaterialView = false; }
        }
        ToolTipService.SetToolTip(row.MaterialButton!,material?.Name ?? "Выберите материал");
        if (updateHeaders) UpdateDetailHeaders();
    }

    private static Task LoadMaterialTextureAsync(Image image, string? source) =>
        ImagePreviewService.SetVisibleAsync(image, source ?? "");

    private static string UnitLabel(string unit) => unit switch
    {
        "m2" => UiText.Get("м²"),
        "lm" => UiText.Get("пог. м"),
        _ => UiText.Get("шт.")
    };
    private MenuFlyout CreateDetailContextMenu(DetailRow row)
    {
        var flyout = new MenuFlyout();
        var add = new MenuFlyoutItem { Text = UiText.Get("Добавить") };
        add.Click += AddDetailClick;
        var edit = new MenuFlyoutItem { Text = UiText.Get("Изменить") };
        edit.Click += (_, _) =>
        {
            _selectedDetailRow = row;
            UpdateDetailSelectionVisuals();
        };
        var delete = new MenuFlyoutItem { Text = UiText.Get("Удалить") };
        delete.Click += async (_, _) =>
        {
            _selectedDetailRow = row;
            if (!await ConfirmDeleteAsync(UiText.Get("Удалить деталь?"),row.Detail.Name)) return;
            if (row.Detail.IsGeneratedEdgeBand) EdgeBandingService.RemoveMaterial(_sourceProduct, row.Detail.MaterialId);
            _sourceProduct.Details.Remove(row.Detail);
            RefreshDetails();
            UpdateSummary();
        };
        flyout.Items.Add(add);
        flyout.Items.Add(edit);
        flyout.Items.Add(delete);
        return flyout;
    }

    private void DetailRowTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border { Tag: DetailRow row })
        {
            SelectDetailRow(row);
        }
    }

    private void SelectDetailRow(DetailRow row)
    {
        if (ReferenceEquals(_selectedDetailRow, row)) return;
        var previous = _selectedDetailRow;
        _selectedDetailRow = row;
        if (previous is not null) UpdateDetailSelectionVisual(previous);
        UpdateDetailSelectionVisual(row);
    }

    private void ProductBackgroundTapped(object sender, TappedRoutedEventArgs e)
    {
        DetailRow? clickedRow = null;
        for (var element = e.OriginalSource as DependencyObject; element is not null && element != Root;
             element = VisualTreeHelper.GetParent(element))
        {
            // Leave native input, buttons, popups and scrollbars in charge of their own focus.
            if (element is Control control && control is not ScrollViewer && control.GetType() != typeof(ContentControl)) return;
            if (element is Border { Tag: DetailRow row }) clickedRow = row;
        }

        if (FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox or NumberBox)
        {
            BackgroundFocusTarget.IsTabStop = true;
            BackgroundFocusTarget.Focus(FocusState.Programmatic);
        }
        if (clickedRow is not null) SelectDetailRow(clickedRow);
        e.Handled = true;
    }

    private void UpdateDetailSelectionVisuals()
    {
        foreach (var row in _detailRows) UpdateDetailSelectionVisual(row);
    }

    private void UpdateDetailSelectionVisual(DetailRow row)
    {
        if (row.Container is null) return;
        var brush = row == _selectedDetailRow ? (Brush)Application.Current.Resources["AppAccentBrush"] : null;
        if (!ReferenceEquals(row.Container.BorderBrush, brush)) row.Container.BorderBrush = brush;
    }

    private IntPtr MinSizeSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr refData)
    {
        if (message == WmGetMinMaxInfo)
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MinTrackSize.X = MinimumWindowWidth;
            info.MinTrackSize.Y = MinimumWindowHeight;
            Marshal.StructureToPtr(info, lParam, false);
            return IntPtr.Zero;
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private static Product CloneProduct(Product source) => new()
    {
        Id = source.Id,
        Group = source.Group,
        CreatedAt = source.CreatedAt,
        SourceCatalogProductId = source.SourceCatalogProductId,
        Name = source.Name,
        Image = source.Image,
        Length = source.Length,
        Depth = source.Depth,
        Height = source.Height,
        Qty = source.Qty,
        HasLegs = source.HasLegs,
        LegHeight = source.LegHeight,
        FixedSizes = source.FixedSizes.Select(row => new FixedSize { Id = row.Id, Name = row.Name, Value = row.Value }).ToList(),
        Details = source.Details.Select(detail => new Detail
        {
            Id = detail.Id,
            Name = detail.Name,
            Qty = detail.Qty,
            Type = detail.Type,
            MaterialId = detail.MaterialId,
            ProjectUnitPrice = detail.ProjectUnitPrice,
            ProjectPriceMaterialId = detail.ProjectPriceMaterialId,
            ProjectPriceUnit = detail.ProjectPriceUnit,
            ProjectPriceMode = detail.ProjectPriceMode,
            ProjectPriceQuantity = detail.ProjectPriceQuantity,
            ProjectPriceIsManual = detail.ProjectPriceIsManual,
            ProjectWasteAllowancePercent = detail.ProjectWasteAllowancePercent,
            LengthExpr = detail.LengthExpr,
            WidthExpr = detail.WidthExpr,
            AllowRotation = detail.AllowRotation,
            IsGeneratedEdgeBand = detail.IsGeneratedEdgeBand,
            EdgeBands = detail.EdgeBands.Select(edge => new DetailEdgeBand { Side = edge.Side, Type = edge.Type, MaterialId = edge.MaterialId }).ToList()
        }).ToList()
    };

    private async Task<bool> ConfirmDeleteAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.ActualTheme,
            Title = title,
            Content = message,
            PrimaryButtonText = UiText.Get("Удалить"),
            CloseButtonText = UiText.Get("Отмена"),
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public sealed record FormulaReference(string Section, string Label, string Expression, string Value)
    {
        public override string ToString() => $"{Section}: {Label}, {Value}";
    }

    private delegate IntPtr WindowSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr refData);

    [DllImport("Comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, WindowSubclassProc subclassProc, UIntPtr subclassId, UIntPtr refData);

    [DllImport("Comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    private static string Empty(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}

public sealed class DetailRow(Detail detail, List<MaterialChoice> materialChoices)
{
    public Detail Detail { get; } = detail;
    public List<MaterialChoice> MaterialChoices { get; } = materialChoices;
    public MaterialChoice? SelectedMaterial { get; set; } = materialChoices.FirstOrDefault(item => item.Type == detail.Type && item.Material.Id == detail.MaterialId);
    public Button? MaterialButton { get; set; }
    public Flyout? MaterialFlyout { get; set; }
    public Flyout? EdgeBandFlyout { get; set; }
    public List<TextBox> FormulaBoxes { get; } = [];
    public string Calculation { get; set; } = "";
    public DetailCalc? CalculationResult { get; set; }
    public Border? Container { get; set; }
    public TextBlock? CalculationText { get; set; }
    public Button? EdgeBandButton { get; set; }
    public Windows.Foundation.Point? EdgeBandPointer { get; set; }

    public void CommitMaterial()
    {
        if (SelectedMaterial is null) return;
        Detail.Type = SelectedMaterial.Type;
        Detail.MaterialId = SelectedMaterial.Material.Id;
    }
}
