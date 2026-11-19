using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Views;

public sealed partial class ProductWindow
{
    private readonly ObservableCollection<DetailRow> _detailRows = [];
    private sealed record DetailRowView(Border Container, Action<DetailRow> Bind, Action Unbind)
    {
        public string Scale { get; set; } = App.Preferences.Current.InterfaceScale;
        public bool AnimationsEnabled { get; set; } = App.Preferences.Current.AnimationsEnabled;
    }
    private DetailRow? _selectedDetailRow;
    private bool _confirmingDetailDelete;

    private void RefreshDetailRows(bool hasEdges)
    {
        EdgeBandingService.Refresh(App.State, _sourceProduct);
        using var measurement = App.UiPerformance.Measure(UiWork.ProductRows);
        using (App.UiPerformance.Measure(UiWork.ProductRowControls))
        {
            SyncDetailRows();
        }
        using (App.UiPerformance.Measure(UiWork.ProductRowLayout))
        {
            if (hasEdges)
                foreach (var row in _detailRows) UpdateEdgeBandButton(row);
            UpdateDetailSelectionVisuals();
            UpdateDetailHeaders();
            if (DetailsRowsPanel.Layout is StackLayout layout)
                layout.Spacing = InterfaceOptions.Gap(4, App.Preferences.Current.InterfaceScale);
            UpdateDetailColumns();
        }
    }

    private void SyncDetailRows()
    {
        using var measurement = App.UiPerformance.Measure(UiWork.ProductRowData);
        _detailEditor.SyncRows(_detailRows, row => row.Detail, CreateDetailRow);
        if (_selectedDetailRow is null || !_detailEditor.Contains(_selectedDetailRow.Detail))
            _selectedDetailRow = _detailRows.FirstOrDefault();
    }

    private DetailRow CreateDetailRow(Detail detail) => new(detail, _detailEditor);

    private void AddDetailClick(object sender, RoutedEventArgs e)
    {
        if (_editorClosed || _saving || _materialPickerOpen || _confirmingDetailDelete) return;
        using var measurement = App.UiPerformance.Measure(UiWork.ProductRowAppend);
        MeasureNextFrame(UiWork.ProductRowAppendFrame);
        foreach (var target in _formulaFlyouts.Keys.ToList()) HideFormulaSuggestions(target);
        var detail = new Detail
        {
            Name = "Новая деталь",
            Type = "",
            MaterialId = "",
            LengthExpr = "длина изделия",
            WidthExpr = "глубина изделия"
        };
        _sourceProduct.Details.Add(detail);
        var row = CreateDetailRow(detail);
        _detailRows.Add(row);
        TraceMaterial("detail.add", detail, value: _detailRows.Count);
        SelectDetailRow(row);
        UpdateSummary();
        RevealDetail(row, DetailFocus.Name);
    }

    private void DetailElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
    {
        using var measurement = App.UiPerformance.Measure(UiWork.ProductRowRealize);
        if (args.Element is not ContentControl host || args.Index < 0 || args.Index >= _detailRows.Count) return;
        var row = _detailRows[args.Index];
        if (ReferenceEquals(row.Detail, _materialTraceTarget)) TraceMaterial("target.view-prepared", row.Detail, value: args.Index);
        if (host.Tag is DetailRowView existing)
        {
            if (!ReferenceEquals(existing.Container.Tag, row))
            {
                using var reuseMeasurement = App.UiPerformance.Measure(UiWork.ProductRowReuse);
                existing.Unbind();
                existing.Bind(row);
            }
            var preferences = App.Preferences.Current;
            if (existing.Scale != preferences.InterfaceScale || existing.AnimationsEnabled != preferences.AnimationsEnabled)
            {
                ThemeService.ApplyLayout(existing.Container, preferences);
                existing.Scale = preferences.InterfaceScale;
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
        row.EdgeBandFlyout?.Hide();
        if (row.Container?.ContextFlyout is { IsOpen: true } menu) menu.Hide();
        foreach (var field in row.FormulaBoxes) HideFormulaSuggestions(field);
    }

    private void ReleaseDetailRowView(DetailRow row)
    {
        if (ReferenceEquals(row.Detail, _materialTraceTarget)) TraceMaterial("target.view-released", row.Detail);
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
        row.NameBox = null;
        row.MaterialButton = null;
        row.MaterialName = null;
        row.MaterialImage = null;
        row.EdgeBandFlyout = null;
        row.CalculationText = null;
        row.EdgeBandButton = null;
    }
    private async void DeleteDetailClick(object sender, RoutedEventArgs e)
    {
        if (_selectedDetailRow is { } row) await DeleteDetailAsync(row);
    }

    private async Task DeleteDetailAsync(DetailRow row)
    {
        if (_editorClosed || _saving || _materialPickerOpen || _confirmingDetailDelete ||
            !_detailEditor.Contains(row.Detail)) return;
        _confirmingDetailDelete = true;
        try
        {
            if (!await ConfirmDeleteAsync(UiText.Get("Удалить деталь?"), row.Detail.Name) ||
                _editorClosed || !_detailEditor.Contains(row.Detail)) return;
            if (row.Detail.IsGeneratedEdgeBand) EdgeBandingService.RemoveMaterial(_sourceProduct, row.Detail.MaterialId);
            _sourceProduct.Details.Remove(row.Detail);
            TraceMaterial("detail.delete", row.Detail);
            UpdateSummary(reconcileRows: true);
            NotificationCenter.Publish(UiText.Get("Деталь удалена"), UiText.Format($"Деталь «{row.Detail.Name}» удалена из изделия."));
        }
        finally { _confirmingDetailDelete = false; }
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

    private void NormalizeDetailInputs()
    {
        foreach (var row in _detailRows)
        {
            row.Detail.Name = Empty(row.Detail.Name, "Новая деталь");
            row.Detail.LengthExpr = Empty(row.Detail.LengthExpr, "0");
            row.Detail.WidthExpr = Empty(row.Detail.WidthExpr, "0");
            row.Detail.Qty = row.Detail.Qty <= 0 ? 1 : row.Detail.Qty;
        }
    }

    private void UpdateSummary(bool reconcileRows = false)
    {
        using var measurement = App.UiPerformance.Measure(UiWork.ProductSummary);
        SaveProductFields();
        var hasEdges = _sourceProduct.Details.Any(detail => detail.IsGeneratedEdgeBand || detail.EdgeBands.Count > 0);
        if (reconcileRows || hasEdges) RefreshDetailRows(hasEdges);
        if (_catalogEditor)
        {
            if (reconcileRows) ProjectDetailPricing.Clear(_sourceProduct);
        }
        else ProjectDetailPricing.Capture(App.State, _sourceProduct);
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
        var hadError = !string.IsNullOrEmpty(row.CalculationResult?.Error);
        row.CalculationResult = calc;
        var unit = row.SelectedMaterial?.Material.Unit;
        var size = unit == "m2" ? $"{Calculator.Number(calc.Length)} × {Calculator.Number(calc.Width)} мм · "
            : unit == "lm" ? $"{Calculator.Number(calc.Length)} мм · " : "";
        var calculation = string.IsNullOrEmpty(calc.Error) ? size + Calculator.Money(calc.Cost) : calc.Error;
        if (row.Calculation != calculation || hadError != !string.IsNullOrEmpty(calc.Error))
        {
            row.Calculation = calculation;
            RenderRowCalculation(row);
        }
        return calc;
    }

    private static void RenderRowCalculation(DetailRow row)
    {
        if (row.CalculationText is not { } text) return;
        text.Text = row.Calculation;
        text.Foreground = (Brush)Application.Current.Resources[string.IsNullOrEmpty(row.CalculationResult?.Error) ? "AppTextBrush" : "UiDangerBrush"];
        ToolTipService.SetToolTip(text, row.Calculation);
    }

    private DetailRowView CreateDetailRowView(DetailRow row)
    {
        var rebinding = false;
        var bindingVersion = 0;
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
            if (border.Tag is not DetailRow activeRow) return;
            var menu = border.ContextFlyout ??= CreateDetailContextMenu(activeRow);
            menu.AreOpenCloseAnimationsEnabled = _motion.AllowsAnimations;
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
        row.NameBox = nameBox;
        nameBox.IsReadOnly = row.Detail.IsGeneratedLeg || row.Detail.IsGeneratedEdgeBand;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(nameBox,UiText.Get("Название детали"));
        nameBox.TextChanged += (_, _) =>
        {
            if (rebinding || border.Tag is null || row.Detail.IsGeneratedLeg || row.Detail.IsGeneratedEdgeBand ||
                row.Detail.Name == nameBox.Text) return;
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
        var materialName = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(materialName, 1);
        materialContent.Children.Add(materialName);
        var materialButton = new Button
        {
            Content = materialContent,
            MinWidth = 0,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left
        };
        row.MaterialButton = materialButton;
        row.MaterialName = materialName;
        row.MaterialImage = materialImage;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(materialButton,UiText.Get("Материал детали ") + row.Detail.Name);
        (Detail Target, int Binding, uint PointerId, Windows.Foundation.Point Position)? materialPress = null;
        materialButton.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler((_, args) =>
            {
                if (border.Tag is not DetailRow pressedRow) return;
                materialPress = (pressedRow.Detail, bindingVersion, args.Pointer.PointerId, args.GetCurrentPoint(Root).Position);
                TraceMaterial("material-field.press", pressedRow.Detail);
            }), true);
        materialButton.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, args) =>
        {
            if (materialPress is { } press && press.PointerId == args.Pointer.PointerId && press.Binding == bindingVersion)
                materialPress = (press.Target, press.Binding, press.PointerId, args.GetCurrentPoint(Root).Position);
        }), true);
        materialButton.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler((_, _) => materialPress = null), true);
        materialButton.Click += async (_, _) =>
        {
            var press = materialPress;
            materialPress = null;
            if (!materialButton.IsLoaded || border.Tag is not DetailRow currentRow) return;
            if (press is { } pointer && (pointer.Binding != bindingVersion || !ReferenceEquals(pointer.Target, currentRow.Detail)))
            {
                TraceMaterial("material-field.recycled-press", pointer.Target);
                return;
            }
            var position = press?.Position ?? materialButton.TransformToVisual(Root)
                .TransformPoint(new Windows.Foundation.Point(0, materialButton.ActualHeight));
            await PickRowMaterialAsync(currentRow, position);
        };
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
            Tag = "icon-tool",
            Content = new FontIcon { Glyph = "\uE8AB", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 16 },
            Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
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
            priceButton.Click += async (_, _) =>
            {
                if (border.Tag is DetailRow activeRow) await EditProjectDetailPriceAsync(activeRow);
            };
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
        UpdateMaterialRowView(row, updateHeaders: false);
        RenderRowCalculation(row);
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
                row.NameBox = nameBox;
                row.MaterialButton = materialButton;
                row.MaterialName = materialName;
                row.MaterialImage = materialImage;
                row.EdgeBandButton = edgeButton;
                row.CalculationText = calculationText;
                nameBox.Text = row.Detail.Name;
                nameBox.IsReadOnly = row.Detail.IsGeneratedEdgeBand || row.Detail.IsGeneratedLeg;
                qtyBox.Value = row.Detail.Qty <= 0 ? 1 : row.Detail.Qty;
                foreach (var control in grid.Children.OfType<Control>()) control.IsEnabled = !row.Detail.IsGeneratedEdgeBand;
                materialButton.IsEnabled = true;
                if (priceButton is not null) priceButton.IsEnabled = true;
                BindFormulaBox(lengthBox, row, false);
                BindFormulaBox(widthBox, row, true);
                UpdateMaterialRowView(row, updateHeaders: false);
                RenderRowCalculation(row);
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
            bindingVersion++;
            ReleaseDetailRowView(row);
            border.Tag = null;
            border.ContextFlyout = null;
            materialImage.Source = null;
            materialImage.Tag = null;
            _ = LoadMaterialTextureAsync(materialImage, null);
        });
    }

    private async Task EditProjectDetailPriceAsync(DetailRow row)
    {
        var choice = row.SelectedMaterial;
        if (_catalogEditor || choice is null || !_detailEditor.Contains(row.Detail) ||
            Root.XamlRoot is not { } xamlRoot) return;
        var material = choice.Material;
        bool IsCurrentTarget() => _detailEditor.Contains(row.Detail) && row.Detail.MaterialId == material.Id &&
            row.Detail.Type == choice.Type;
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
            XamlRoot = xamlRoot, RequestedTheme = Root.ActualTheme, Title = UiText.Get("Цена детали в проекте"), Content = content,
            MinWidth = Math.Min(540, xamlRoot.Size.Width - 48),
            PrimaryButtonText = UiText.Get("Сохранить"), SecondaryButtonText = UiText.Get("Цена каталога"),
            CloseButtonText = UiText.Get("Отмена"), DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!IsCurrentTarget()) return;
            try { ProjectDetailPricing.SetEnteredPrice(row.Detail, material, NumberBoxInput.Read(price), NumberBoxInput.Read(wasteAllowance)); }
            catch (InvalidOperationException exception)
            { args.Cancel = true; error.Text = UiText.Get(exception.Message); error.Visibility = Visibility.Visible; }
        };
        var result = await dialog.ShowAsync();
        if (!IsCurrentTarget()) return;
        if (result == ContentDialogResult.Secondary)
        {
            if (MaterialPricing.Error(material).Length > 0) return;
            ProjectDetailPricing.UseCatalogPrice(row.Detail, material);
        }
        if (result != ContentDialogResult.None) UpdateSummary();
    }

    private void UpdateDetailHeaders()
    {
        var hasLinear = _detailRows.Any(row => row.SelectedMaterial?.Material.Unit is "m2" or "lm");
        var hasSheet = _detailRows.Any(row => row.SelectedMaterial?.Material.Unit == "m2");
        DetailLengthHeader.Visibility = hasLinear ? Visibility.Visible : Visibility.Collapsed;
        DetailWidthHeader.Visibility = hasSheet ? Visibility.Visible : Visibility.Collapsed;
        DetailRotationHeader.Visibility = hasSheet ? Visibility.Visible : Visibility.Collapsed;
    }

    private MenuFlyout CreateDetailContextMenu(DetailRow row)
    {
        var flyout = new MenuFlyout();
        var add = new MenuFlyoutItem { Text = UiText.Get("Добавить") };
        add.Click += AddDetailClick;
        var edit = new MenuFlyoutItem { Text = UiText.Get("Изменить") };
        edit.Click += (_, _) =>
        {
            if (_detailEditor.Contains(row.Detail)) SelectDetailRow(row);
        };
        var delete = new MenuFlyoutItem { Text = UiText.Get("Удалить") };
        delete.Click += async (_, _) => await DeleteDetailAsync(row);
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
        if (_materialPickerOpen) return;
        DetailRow? clickedRow = null;
        var element = e.OriginalSource as DependencyObject;
        for (; element is not null && element != Root; element = VisualTreeHelper.GetParent(element))
        {
            // Leave native input, buttons, popups and scrollbars in charge of their own focus.
            if (element is Control control && control is not ScrollViewer && control.GetType() != typeof(ContentControl)) return;
            if (element is Border { Tag: DetailRow row }) clickedRow = row;
        }
        // Popup input can route here even though it is outside the editor's visual tree.
        if (element != Root) return;

        if (FocusManager.GetFocusedElement(Root.XamlRoot) is TextBox or NumberBox) FocusEditorAnchor();
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

    private sealed class DetailRow(Detail detail, ProductDetailEditor editor)
    {
        public Detail Detail { get; } = detail;
        public MaterialChoice? SelectedMaterial => editor.MaterialFor(Detail);
        public TextBox? NameBox { get; set; }
        public Button? MaterialButton { get; set; }
        public TextBlock? MaterialName { get; set; }
        public Image? MaterialImage { get; set; }
        public Flyout? EdgeBandFlyout { get; set; }
        public List<TextBox> FormulaBoxes { get; } = [];
        public string Calculation { get; set; } = "";
        public DetailCalc? CalculationResult { get; set; }
        public Border? Container { get; set; }
        public TextBlock? CalculationText { get; set; }
        public Button? EdgeBandButton { get; set; }
        public Windows.Foundation.Point? EdgeBandPointer { get; set; }
    }
}
