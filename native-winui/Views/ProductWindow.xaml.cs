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
    private readonly List<NumberBox> _fixedValueBoxes = [];
    private readonly Dictionary<TextBox, Flyout> _formulaFlyouts = [];
    private readonly Dictionary<TextBox, FormulaField> _formulaFields = [];
    private bool _refreshingFormulaText;
    private sealed record FormulaField(Detail Detail, bool Width, FormulaPresentation Presentation)
    {
        public string Expression => Width ? Detail.WidthExpr : Detail.LengthExpr;
    }
    private List<DetailRow> _detailRows = [];
    private DetailRow? _selectedDetailRow;
    private bool _loading = true;
    private bool _insertingReference;

    private void DetailsTableViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        DetailsTable.Width = Math.Max(DetailTableLayout.MinimumWidth + 28, e.NewSize.Width);
    }

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
            grid.ColumnDefinitions[i].Width = new GridLength(widths[i]);
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
        Title = _isNew ? "Vitan-Cut - Добавить изделие" : $"Vitan-Cut - {_sourceProduct.Name}";
        TitleText.Text = _isNew ? "Добавить изделие" : _sourceProduct.Name;
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

            var nameBox = new TextBox { Header = $"Размер {i + 1}", PlaceholderText = "Название" };
            var valueBox = new NumberBox { Header = "Значение", Minimum = 0 };
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
        var bytes = ProductImageData.Read(file.Path);
        if (bytes is null || !await ImagePreviewService.SetAsync(ProductImagePreview, file.Path))
        {
            await new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = "Не удалось загрузить изображение", Content = "Выберите PNG или JPEG размером до 20 МБ.", CloseButtonText = "Понятно" }.ShowAsync();
            return;
        }
        var media = file.FileType.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "png" : "jpeg";
        _sourceProduct.Image = $"data:image/{media};base64," + Convert.ToBase64String(bytes);
        UpdateImagePreview();
    }

    private async void UpdateImagePreview()
    {
        var path = _sourceProduct.Image;
        var loaded = await ImagePreviewService.SetAsync(ProductImagePreview, path);
        if (_sourceProduct.Image != path) return;
        ProductImagePreview.Visibility = loaded ? Visibility.Visible : Visibility.Collapsed;
        ProductImageStatusText.Text = loaded ? (path.StartsWith("data:") ? "Изображение изделия" : Path.GetFileName(path)) : "Изображение не выбрано или недоступно";
    }
    private void RefreshDetails(Detail? select = null)
    {
        var selectedId = select?.Id ?? _selectedDetailRow?.Detail.Id;
        foreach (var flyout in _formulaFlyouts.Values)
        {
            if (flyout.IsOpen) flyout.Hide();
        }
        _formulaFlyouts.Clear();
        _formulaFields.Clear();
        _detailRows = _sourceProduct.Details.Select(CreateDetailRow).ToList();
        DetailsRowsPanel.Children.Clear();
        foreach (var row in _detailRows)
        {
            DetailsRowsPanel.Children.Add(CreateDetailRowView(row));
        }

        _selectedDetailRow = _detailRows.FirstOrDefault(row => row.Detail.Id == selectedId) ?? _detailRows.FirstOrDefault();
        UpdateDetailSelectionVisuals();
        UpdateDetailHeaders();
        DetailsRowsPanel.UpdateLayout();
        ThemeService.ApplyLayout(DetailsRowsPanel, App.Preferences.Current);
        UpdateDetailColumns();
    }

    private DetailRow CreateDetailRow(Detail detail)
    {
        var row = new DetailRow(detail, App.State.MaterialChoices().ToList());
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
        FillProduct();
        RefreshDetails();
        UpdateSummary();
    }

    private void AddDetailClick(object sender, RoutedEventArgs e)
    {
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
        RefreshDetails(detail);
        UpdateSummary();
        ScrollDetailIntoView(_selectedDetailRow);
    }

    private void ScrollDetailIntoView(DetailRow? row)
    {
        if (row?.Container is not FrameworkElement container) return;

        void BringIntoView()
        {
            DetailsScrollViewer.UpdateLayout();
            container.StartBringIntoView(new BringIntoViewOptions
            {
                AnimationDesired = App.Preferences.Current.AnimationsEnabled,
                VerticalAlignmentRatio = 0.5
            });
        }

        container.Loaded += (_, _) => BringIntoView();
        DetailsScrollViewer.DispatcherQueue.TryEnqueue(BringIntoView);
    }
    private async void DeleteDetailClick(object sender, RoutedEventArgs e)
    {
        if (_selectedDetailRow is not { } row) return;
        if (!await ConfirmDeleteAsync("Удалить деталь?", row.Detail.Name)) return;
        _sourceProduct.Details.Remove(row.Detail);
        RefreshDetails();
        UpdateSummary();
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
            if (!App.State.Database.Materials.TryGetValue(group, out var materials))
                App.State.Database.Materials[group] = materials = [];
            var material = new Material { Name = "Ножка пластик", Unit = "pc", Cost = 0 };
            materials.Add(material);
            if (!App.State.Database.MaterialGroups.Contains(group)) App.State.Database.MaterialGroups.Add(group);
            match = new MaterialChoice(group, material);
            App.State.Save();
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
            Title = "Материал добавлен",
            Content = "Материал «Ножка пластик» отсутствовал. Он создан автоматически: шт., 0 руб.",
            CloseButtonText = "Понятно"
        };
        await dialog.ShowAsync();
    }
    private async void SaveClick(object sender, RoutedEventArgs e)
    {
        SaveProductFields();
        if (ProductValidation.Error(App.State, _sourceProduct) is { } error)
        {
            await new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = "Проверьте изделие", Content = error, CloseButtonText = "Понятно" }.ShowAsync();
            return;
        }
        try
        {

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
                    Title = "Добавить в каталог?",
                    Content = "Изделие с таким названием отсутствует в каталоге этого заказчика.",
                    PrimaryButtonText = "В каталог и проект",
                    SecondaryButtonText = "Только в проект",
                    CloseButtonText = "Отмена",
                    DefaultButton = ContentDialogButton.Primary
                };
                var result = await decision.ShowAsync();
                if (result == ContentDialogResult.None) return;
                if (result == ContentDialogResult.Primary)
                    _catalog.SaveProduct(_project.Counterparty, null, CloneProduct(_sourceProduct));
            }
        }

        ProductSaved?.Invoke(CloneProduct(_sourceProduct), _isNew);
        Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = "Не удалось сохранить", Content = ex.Message, CloseButtonText = "Закрыть" }.ShowAsync();
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
        SaveProductFields();
        foreach (var row in _detailRows) UpdateDetailRowCalculation(row);
        var totals = Calculator.Product(App.State, _sourceProduct);
        SummaryAreaText.Text = $"{Calculator.Number(totals.Area)} м²";
        SummaryCostText.Text = Calculator.Money(totals.Cost);
        RefreshFormulaLabels();
    }

    private void UpdateDetailRowCalculation(DetailRow row)
    {
        var calc = Calculator.Detail(App.State, _sourceProduct, row.Detail);
        var unit = row.SelectedMaterial?.Material.Unit;
        var size = unit == "m2" ? $"{Calculator.Number(calc.Length)} × {Calculator.Number(calc.Width)} мм · "
            : unit == "lm" ? $"{Calculator.Number(calc.Length)} мм · " : "";
        row.Calculation = string.IsNullOrEmpty(calc.Error) ? size + Calculator.Money(calc.Cost) : calc.Error;
        if (row.CalculationText is not null) row.CalculationText.Text = row.Calculation;
    }

    private FrameworkElement CreateDetailRowView(DetailRow row)
    {
        var border = new Border
        {
            Padding = new Thickness(8),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Colors.Transparent),
            Tag = row
        };
        border.Tapped += DetailRowTapped;
        border.ContextFlyout = CreateDetailContextMenu(row);
        row.Container = border;

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        var nameBox = new TextBox { Text = row.Detail.Name, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        nameBox.TextChanged += (_, _) => { row.Detail.Name = nameBox.Text; DetailRowChanged(nameBox, EventArgs.Empty); };
        grid.Children.Add(nameBox);

        var materialButton = new Button
        {
            Content = new TextBlock { Text = row.SelectedMaterial?.Material.Name ?? "Выберите материал", TextTrimming = TextTrimming.CharacterEllipsis },
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        materialButton.Flyout = CreateMaterialFlyout(row, materialButton);
        ToolTipService.SetToolTip(materialButton, row.SelectedMaterial?.Material.Name ?? "Выберите материал");
        Grid.SetColumn(materialButton, 1);
        grid.Children.Add(materialButton);
        var qtyBox = new NumberBox { Value = row.Detail.Qty <= 0 ? 1 : row.Detail.Qty, Minimum = 1, MinWidth = 0, VerticalAlignment = VerticalAlignment.Center };
        qtyBox.ValueChanged += (_, _) => { row.Detail.Qty = qtyBox.Value; DetailRowNumberChanged(qtyBox, null!); };
        Grid.SetColumn(qtyBox, 2);
        grid.Children.Add(qtyBox);

        var isSheetMaterial = row.SelectedMaterial?.Material.Unit == "m2";
        var isLinearMaterial = row.SelectedMaterial?.Material.Unit == "lm";
        var lengthBox = CreateFormulaBox(row, false, isSheetMaterial || isLinearMaterial);
        Grid.SetColumn(lengthBox, 3);
        grid.Children.Add(lengthBox);

        var widthBox = CreateFormulaBox(row, true, isSheetMaterial);
        Grid.SetColumn(widthBox, 4);
        grid.Children.Add(widthBox);

        var rotationBox = new ToggleSwitch
        {
            IsOn = DetailRotation.CanRotate(row.Detail, row.SelectedMaterial?.Material),
            IsEnabled = row.SelectedMaterial?.Material.TextureDirection != true,
            OnContent = "",
            OffContent = "",
            MinWidth = 40,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = isSheetMaterial ? Visibility.Visible : Visibility.Collapsed
        };
        var rotationCell = new Grid { Visibility = isSheetMaterial ? Visibility.Visible : Visibility.Collapsed, ColumnSpacing = 2 };
        rotationCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        rotationCell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        var rotationLock = new FontIcon { Glyph = "\uE72E", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(rotationLock, 1);
        void UpdateRotationIndicator()
        {
            rotationLock.Visibility = DetailRotation.IsLocked(row.Detail, row.SelectedMaterial?.Material) ? Visibility.Visible : Visibility.Collapsed;
            ToolTipService.SetToolTip(rotationCell, DetailRotation.Reason(row.Detail, row.SelectedMaterial?.Material));
        }
        rotationBox.Toggled += (_, _) =>
        {
            row.Detail.AllowRotation = rotationBox.IsOn;
            UpdateRotationIndicator();
            DetailRowChanged(rotationBox, EventArgs.Empty);
        };
        UpdateRotationIndicator();
        rotationCell.Children.Add(rotationBox);
        rotationCell.Children.Add(rotationLock);
        Grid.SetColumn(rotationCell, 5);
        grid.Children.Add(rotationCell);

        var calculationText = new TextBlock
        {
            Text = row.Calculation,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        row.CalculationText = calculationText;
        Grid.SetColumn(calculationText, 6);
        grid.Children.Add(calculationText);

        border.Child = grid;
        return border;
    }

    private TextBox CreateFormulaBox(DetailRow row, bool width, bool visible)
    {
        var field = new FormulaField(row.Detail, width, new FormulaPresentation(_sourceProduct));
        var target = new TextBox
        {
            Text = field.Presentation.Display(field.Expression),
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = visible ? Visibility.Visible : Visibility.Collapsed
        };
        _formulaFields[target] = field;
        ToolTipService.SetToolTip(target, target.Text);
        target.TextChanged += (_, _) =>
        {
            if (_refreshingFormulaText) return;
            var expression = field.Presentation.Storage(target.Text);
            if (width) row.Detail.WidthExpr = expression;
            else row.Detail.LengthExpr = expression;
            ToolTipService.SetToolTip(target, target.Text);
            DetailRowChanged(target, EventArgs.Empty);
            UpdateFormulaSuggestions(target);
        };
        target.LostFocus += (_, _) => RefreshFormulaLabels();
        ConfigureFormulaSuggestions(target, row);
        return target;
    }

    private void RefreshFormulaLabels()
    {
        if (_refreshingFormulaText) return;
        _refreshingFormulaText = true;
        try
        {
            foreach (var (target, field) in _formulaFields)
            {
                if (target.FocusState != FocusState.Unfocused) continue;
                var display = field.Presentation.Display(field.Expression);
                if (target.Text != display) target.Text = display;
                ToolTipService.SetToolTip(target, display);
            }
        }
        finally { _refreshingFormulaText = false; }
    }

    private void ConfigureFormulaSuggestions(TextBox target, DetailRow row)
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            ShowMode = FlyoutShowMode.Transient
        };

        _formulaFlyouts[target] = flyout;
        target.Tag = row;
        target.SelectionChanged += (_, _) => { if (target.FocusState != FocusState.Unfocused) UpdateFormulaSuggestions(target); };
        target.KeyDown += (_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.Escape || !flyout.IsOpen) return;
            flyout.Hide();
            args.Handled = true;
        };
    }

    private void UpdateFormulaSuggestions(TextBox target)
    {
        if (_loading || _refreshingFormulaText || _insertingReference || !_formulaFlyouts.TryGetValue(target, out var flyout)) return;

        var text = target.Text;
        var caret = Math.Clamp(target.SelectionStart, 0, text.Length);
        var queryStart = FormulaEditing.QueryStart(text, caret);
        if (queryStart < 0)
        {
            if (flyout.IsOpen) flyout.Hide();
            return;
        }

        var query = FormulaText.NormalizeWhitespace(text[queryStart..caret]);
        var currentRow = target.Tag as DetailRow;
        var references = FormulaReferences(currentRow)
            .Where(reference => string.IsNullOrEmpty(query) ||
                                FormulaText.NormalizeWhitespace(reference.Label).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                FormulaText.NormalizeWhitespace(reference.Expression).Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (references.Count == 0)
        {
            if (flyout.IsOpen) flyout.Hide();
            return;
        }

        var content = new StackPanel { Width = 430, Spacing = 4 };
        content.Children.Add(new TextBlock
        {
            Text = "Ссылки на размеры",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(12, 10, 12, 2)
        });

        foreach (var group in references.GroupBy(reference => reference.Section))
        {
            content.Children.Add(new TextBlock
            {
                Text = group.Key,
                Opacity = 0.68,
                Margin = new Thickness(12, 8, 12, 2)
            });

            foreach (var reference in group)
            {
                var item = new Button
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(12, 7, 12, 7),
                    Content = new Grid()
                };
                var row = (Grid)item.Content;
                row.ColumnSpacing = 12;
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = reference.Label, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
                ToolTipService.SetToolTip(item, reference.Label);
                var value = new TextBlock { Text = reference.Value, Opacity = 0.72 };
                Grid.SetColumn(value, 1);
                row.Children.Add(value);
                item.Click += (_, _) =>
                {
                    _insertingReference = true;
                    var expression = _formulaFields[target].Presentation.Display(reference.Expression);
                    var prefix = text[..queryStart].TrimEnd() + " ";
                    target.Text = prefix + expression + text[caret..];
                    target.SelectionStart = prefix.Length + expression.Length;
                    flyout.Hide();
                    target.Focus(FocusState.Programmatic);
                    _insertingReference = false;
                };
                content.Children.Add(item);
            }
        }

        flyout.Content = new ScrollViewer { Content = content, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollMode = ScrollMode.Disabled };
        if (!flyout.IsOpen) flyout.ShowAt(target);
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
        yield return new FormulaReference("Изделие", "Длина", "длина изделия", $"{Calculator.Number(ProductLengthBox.Value, 0)} мм");
        yield return new FormulaReference("Изделие", "Глубина", "глубина изделия", $"{Calculator.Number(ProductDepthBox.Value, 0)} мм");
        yield return new FormulaReference("Изделие", "Высота", "высота изделия", $"{Calculator.Number(ProductHeightBox.Value, 0)} мм");
        if (HasLegsBox.IsChecked == true)
            yield return new FormulaReference("Изделие", "Ножка", "высота ножки", $"{Calculator.Number(LegHeightBox.Value, 0)} мм");

        for (var index = 0; index < _fixedNameBoxes.Count; index++)
        {
            var name = _fixedNameBoxes[index].Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;
            yield return new FormulaReference("Фиксированные размеры", name, name, $"{Calculator.Number(_fixedValueBoxes[index].Value, 0)} мм");
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
                ? $"Деталь {detailIndex}"
                : row.Detail.Name.Trim();
            var calc = Calculator.Detail(App.State, _sourceProduct, row.Detail);

            yield return new FormulaReference(
                "Детали",
                $"{detailName} · длина",
                $"[[detail:{row.Detail.Id}:length]]",
                $"{Calculator.Number(calc.Length, 0)} мм");

            if (row.SelectedMaterial?.Material.Unit == "m2")
            {
                yield return new FormulaReference(
                    "Детали",
                    $"{detailName} · ширина",
                    $"[[detail:{row.Detail.Id}:width]]",
                    $"{Calculator.Number(calc.Width, 0)} мм");
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

    private MenuFlyout CreateMaterialFlyout(DetailRow row, Button button)
    {
        var flyout = new MenuFlyout();
        foreach (var group in row.MaterialChoices.GroupBy(choice => choice.Type).OrderBy(group => group.Key))
        {
            var category = new MenuFlyoutSubItem { Text = group.Key };
            foreach (var choice in group.OrderBy(item => item.Material.Name))
            {
                var item = new MenuFlyoutItem { Text = $"{choice.Material.Name} / {UnitLabel(choice.Material.Unit)}" };
                item.Click += (_, _) =>
                {
                    row.SelectedMaterial = choice;
                    row.CommitMaterial();
                    button.Content = choice.Material.Name;
                    RefreshDetails(row.Detail);
                    UpdateSummary();
                };
                category.Items.Add(item);
            }
            flyout.Items.Add(category);
        }
        return flyout;
    }

    private static string UnitLabel(string unit) => unit switch
    {
        "m2" => "м²",
        "lm" => "пог. м",
        _ => "шт."
    };
    private MenuFlyout CreateDetailContextMenu(DetailRow row)
    {
        var flyout = new MenuFlyout();
        var add = new MenuFlyoutItem { Text = "Добавить" };
        add.Click += AddDetailClick;
        var edit = new MenuFlyoutItem { Text = "Изменить" };
        edit.Click += (_, _) =>
        {
            _selectedDetailRow = row;
            UpdateDetailSelectionVisuals();
        };
        var delete = new MenuFlyoutItem { Text = "Удалить" };
        delete.Click += async (_, _) =>
        {
            _selectedDetailRow = row;
            if (!await ConfirmDeleteAsync("Удалить деталь?", row.Detail.Name)) return;
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
            _selectedDetailRow = row;
            UpdateDetailSelectionVisuals();
        }
    }

    private void UpdateDetailSelectionVisuals()
    {
        foreach (var row in _detailRows)
        {
            if (row.Container is null) continue;
            row.Container.BorderThickness = new Thickness(1);
            row.Container.BorderBrush = row == _selectedDetailRow
                ? (Brush)Application.Current.Resources["AppAccentBrush"]
                : null;
        }
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
            LengthExpr = detail.LengthExpr,
            WidthExpr = detail.WidthExpr,
            AllowRotation = detail.AllowRotation
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
            PrimaryButtonText = "Удалить",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private sealed record FormulaReference(string Section, string Label, string Expression, string Value);

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
    public string Calculation { get; set; } = "";
    public Border? Container { get; set; }
    public TextBlock? CalculationText { get; set; }

    public void CommitMaterial()
    {
        if (SelectedMaterial is null) return;
        Detail.Type = SelectedMaterial.Type;
        Detail.MaterialId = SelectedMaterial.Material.Id;
    }
}
