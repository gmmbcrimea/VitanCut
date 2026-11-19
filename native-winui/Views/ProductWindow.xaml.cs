using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using System.Runtime.InteropServices;
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
    private readonly ProductDetailEditor _detailEditor;
    private readonly ProductEditorMotion _motion = new();
    private readonly bool _isNew;
    private readonly CatalogService? _catalog;
    private readonly bool _catalogEditor;
    private readonly List<TextBox> _fixedNameBoxes = [];
    private readonly UiRequestGate _imagePreviewRequests = new();
    private readonly UiRequestGate _detailViewRequests = new();
    private readonly List<NumberBox> _fixedValueBoxes = [];
    private bool _loading = true;
    private bool _saving;
    private bool _editorClosed;
    private bool _updatingStickyDetailActions;

    private void ProductScrollViewChanged(object sender, ScrollViewerViewChangedEventArgs e) => UpdateStickyDetailActions();

    private readonly Dictionary<UiWork, IDisposable> _frameMeasurements = [];

    private void MeasureScrollFrame(object sender, PointerRoutedEventArgs e)
    {
        if (!_materialPickerOpen) _detailViewRequests.Begin();
        MeasureNextFrame(UiWork.ProductScrollFrame);
    }

    private void MeasureNextFrame(UiWork stage)
    {
        if (!App.UiPerformance.Enabled || _frameMeasurements.ContainsKey(stage)) return;
        if (_frameMeasurements.Count == 0) CompositionTarget.Rendering += CompleteFrame;
        _frameMeasurements.Add(stage, App.UiPerformance.Measure(stage, asynchronous: true));
    }

    private void CompleteFrame(object? sender, object e)
    {
        CompositionTarget.Rendering -= CompleteFrame;
        foreach (var measurement in _frameMeasurements.Values) measurement.Dispose();
        _frameMeasurements.Clear();
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
        LegDetails.RecognizeLegacy(App.State, _sourceProduct);
        _detailEditor = new ProductDetailEditor(App.State, _sourceProduct);
        _catalog = catalog;
        _catalogEditor = catalogEditor;

        InitializeComponent();
        DetailsRowsPanel.ItemsSource = _detailRows;
        App.UiPerformance.Trace(UiLifecycle.ProductOpened);
        TraceMaterial("editor.open", outcome: _catalogEditor ? "catalog" : "project", value: _sourceProduct.Details.Count);
        Closed += (_, _) =>
        {
            _editorClosed = true;
            _detailViewRequests.Begin();
            TraceMaterial("editor.close");
            App.UiPerformance.Trace(UiLifecycle.ProductClosed);
            CompositionTarget.Rendering -= CompleteFrame;
            _frameMeasurements.Clear();
            App.UiPerformance.Flush();
        };
        Root.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(MeasureScrollFrame), true);
        Root.GotFocus += (_, args) =>
        {
            if (_materialPickerOpen || ReferenceEquals(args.OriginalSource, ProductNameBox) && _materialTraceTarget is not null)
            {
                var target = args.OriginalSource;
                var kind = ReferenceEquals(target, ProductNameBox) ? "product-name" :
                    ReferenceEquals(target, BackgroundFocusTarget) ? "editor-fallback" : target?.GetType().Name;
                TraceMaterial("editor.focus", _materialTraceTarget, kind);
            }
            CloseInactiveFormulaSuggestions();
        };
        BackgroundFocusTarget.LostFocus += (_, _) => BackgroundFocusTarget.IsTabStop = false;
        Root.AddHandler(UIElement.TappedEvent, new TappedEventHandler(ProductBackgroundTapped), true);
        Root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            _detailViewRequests.Begin();
            if (_materialPickerOpen) TraceMaterial("editor.pointer-press-during-picker", _materialTraceTarget,
                args.OriginalSource?.GetType().Name);
        }), true);
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, _) => _detailViewRequests.Begin()), true);
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
                foreach (var target in _formulaFlyouts.Keys.ToList()) HideFormulaSuggestions(target);
        };
        Title = _isNew ? UiText.Get("Vitan-Cut - Добавить изделие") : $"Vitan-Cut - {_sourceProduct.Name}";
        TitleText.Text = _isNew ? UiText.Get("Добавить изделие") : _sourceProduct.Name;
        SubtitleText.Prefix = _project.Name + " · ";
        SubtitleText.Counterparty = _project.Counterparty;
        App.Preferences.Apply(this);
        ConfigureMinimumWindowSize();

        BuildFixedSizeRows();
        LoadCatalogProducts();
        FillProduct();
        UpdateResponsiveLayout();
        UpdateSummary(reconcileRows: true);
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
        LegDetails.RecognizeLegacy(App.State, _sourceProduct);
        _sourceProduct.SourceCatalogProductId = product.Id;
        FillProduct();
        UpdateSummary(reconcileRows: true);
    }

    private void ProductFieldChanged(object sender, object e)
    {
        if (_loading) return;
        NormalizeDetailInputs();
        UpdateSummary();
        RefreshOpenFormulaSuggestions();
    }

    private void ProductNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading) return;
        NormalizeDetailInputs();
        UpdateSummary();
        RefreshOpenFormulaSuggestions();
    }

    private void ProductToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        UpdateLegHeightVisibility();
        if (HasLegsBox.IsChecked == true) EnsureLegDetail();
        NormalizeDetailInputs();
        UpdateSummary(reconcileRows: true);
        RefreshOpenFormulaSuggestions();
    }

    private void UpdateLegHeightVisibility() =>
        LegHeightBox.Visibility = HasLegsBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private void EnsureLegDetail()
    {
        LegDetails.RecognizeLegacy(App.State, _sourceProduct);
        if (_sourceProduct.Details.Any(detail => detail.IsGeneratedLeg)) return;

        var match = App.MaterialHistory.Sort(App.State.MaterialChoices().Where(LegDetails.IsMaterial),
            App.Preferences.Current.MaterialPickerOrder).FirstOrDefault();

        if (match is null)
        {
            var group = App.State.Database.Materials.Keys.FirstOrDefault(key => key.Contains("фурнит", StringComparison.CurrentCultureIgnoreCase)) ?? "Фурнитура";
            var material = new Material { Name = "Ножка пластик", Group = LegDetails.Group, Unit = "pc", Cost = 0 };
            App.State.CommitMutation(() =>
            {
                App.State.Database.Materials.TryAdd(group, []);
                App.State.Database.Materials[group].Add(material);
                if (!App.State.Database.MaterialGroups.Contains(group)) App.State.Database.MaterialGroups.Add(group);
            });
            match = new MaterialChoice(group, material);
            _ = ShowLegMaterialNoticeAsync();
        }

        _sourceProduct.Details.Add(LegDetails.Create(match));
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
        if (_editorClosed || _saving || _materialPickerOpen || _confirmingDetailDelete || Root.XamlRoot is not { } root) return;
        _saving = true;
        try
        {
            TraceMaterial("product.save-request", value: _sourceProduct.Details.Count);
            SaveProductFields();
            if (ProductValidation.Error(App.State, _sourceProduct) is { } error)
            {
                TraceMaterial("product.save-blocked", outcome: "validation");
                await new ContentDialog { XamlRoot = root, RequestedTheme = Root.ActualTheme, Title = UiText.Get("Проверьте изделие"), Content = error, CloseButtonText = UiText.Get("Понятно") }.ShowAsync();
                return;
            }

            var savedProduct = CloneProduct(_sourceProduct);
            DetailOrdering.SortForSave(savedProduct);
            if (_isNew && !_catalogEditor && _catalog is not null &&
                !_catalog.GetProducts(_project.Counterparty)
                    .Any(product => string.Equals(product.Name, savedProduct.Name, StringComparison.CurrentCultureIgnoreCase)))
            {
                var decision = new ContentDialog
                {
                    XamlRoot = root,
                    RequestedTheme = Root.ActualTheme,
                    Title = UiText.Get("Добавить в каталог?"),
                    Content = UiText.Get("Изделие с таким названием отсутствует в каталоге этого заказчика."),
                    PrimaryButtonText = UiText.Get("В каталог и проект"),
                    SecondaryButtonText = UiText.Get("Только в проект"),
                    CloseButtonText = UiText.Get("Отмена"),
                    DefaultButton = ContentDialogButton.Primary
                };
                var result = await decision.ShowAsync();
                if (result == ContentDialogResult.None || _editorClosed || Root.XamlRoot != root) return;
                if (result == ContentDialogResult.Primary)
                    _catalog.SaveProduct(_project.Counterparty, null, CloneProduct(savedProduct));
            }

            ProductSaved?.Invoke(savedProduct, _isNew);
            TraceMaterial("product.save-dispatched", outcome: ProductSaved is null ? "no-handler" : "handler-returned",
                value: savedProduct.Details.Count(detail => !string.IsNullOrEmpty(detail.MaterialId)));
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            TraceMaterial("product.save-error", outcome: ex.GetType().Name, value: ex.HResult);
            if (!_editorClosed && Root.XamlRoot == root)
                await new ContentDialog { XamlRoot = root, RequestedTheme = Root.ActualTheme, Title = UiText.Get("Не удалось сохранить"), Content = ex.Message, CloseButtonText = UiText.Get("Закрыть") }.ShowAsync();
        }
        finally { _saving = false; }
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
            IsGeneratedLeg = detail.IsGeneratedLeg,
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
