using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using System.Globalization;
using System.Diagnostics;
using System.Text;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;
using Windows.Graphics;

namespace VitanCut.WinUI.Views;

public sealed partial class ReportWindow : Window
{
    private const double BaseCutScale = 0.22;
    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private readonly List<InteractiveSheet> _interactiveSheets = [];
    private CutPartView? _dragPart;
    private double _dragOffsetX;
    private double _dragOffsetY;
    private uint? _dragPointerId;
    private CutReport? _cutReport;
    private readonly DetailingReport? _detailingReport;
    private readonly Func<Task>? _openAdditionalCut;

    public ReportWindow(CutReport report, IReadOnlyList<SavedCutPlan> plans, string activeId, Action<IReadOnlyList<SavedCutPlan>, string> savePlans,
        Func<Task>? openAdditionalCut = null, bool isAdditional = false)
    {
        InitializeComponent();
        _cutReport = report;
        _baseline = report;
        _signature = CutPlanService.Signature(report);
        _plans = plans.Select(CutPlanService.Copy).ToList();
        _savePlans = savePlans;
        _openAdditionalCut = openAdditionalCut;
        SaveReportButton.Click += SaveReportClick;
        PrintReportButton.Click += PrintReportClick;
        AdditionalCutButton.Click += async (_, _) =>
        {
            if (_openAdditionalCut is not null) await _openAdditionalCut();
        };
        ResetLayoutButton.Click += ResetLayoutClick;
        ResetLayoutButton.Visibility = Visibility.Visible;
        Title = isAdditional ? "Расчёт мебели - Дополнительный раскрой" : "Расчёт мебели - Раскрой ДСП";
        TitleText.Text = isAdditional ? "Дополнительный раскрой" : "Раскрой ДСП";
        SubtitleText.Text = $"{report.ProjectName} · {report.Counterparty} · {report.Address}";
        CutTools.Visibility = Visibility.Visible;
        AdditionalCutButton.Visibility = _openAdditionalCut is null ? Visibility.Collapsed : Visibility.Visible;
        SaveReportButton.Content = "Сохранить раскрой";
        App.Preferences.Apply(this);
        AppWindow.Resize(new SizeInt32(1180, 820));
        InitializeCutTools(activeId);
    }

    public ReportWindow(CutReport report) : this(report, [], "", (_, _) => { })
    {
    }

    public ReportWindow(DetailingReport report)
    {
        InitializeComponent();
        _detailingReport = report;
        SaveReportButton.Click += SaveReportClick;
        PrintReportButton.Click += PrintReportClick;
        Title = "Расчёт мебели - Деталировка";
        TitleText.Text = "Деталировка";
        SubtitleText.Text = $"{report.ProjectName} · {report.Counterparty} · {report.Address}";
        HintText.Text = "";
        SaveReportButton.Content = "Сохранить деталировку";
        DetailingTools.Visibility = Visibility.Visible;
        App.Preferences.Apply(this);
        AppWindow.Resize(new SizeInt32(1180, 820));
        BuildDetailingReport(report);
    }

    private void DspOnlyToggled(object sender, RoutedEventArgs e)
    {
        if (_detailingReport is not null) BuildDetailingReport(_detailingReport);
    }

    private void BuildCutReport(CutReport report)
    {
        ContentPanel.Children.Clear();
        _interactiveSheets.Clear();
        if (!report.Groups.Any())
        {
            ContentPanel.Children.Add(EmptyState("Листовых деталей нет", "В раскрой попадают материалы с единицей м2 и заданным размером листа."));
        }

        if (report.Unplaced.Count > 0)
        {
            var warning = Section();
            warning.Background = (Brush)Application.Current.Resources["AppWarningBrush"];
            warning.Children.Add(SectionHeader("Не удалось разместить детали", "Эти детали не вошли в раскрой и не должны быть потеряны при производстве.", ""));
            foreach (var item in report.Unplaced)
            {
                warning.Children.Add(new TextBlock
                {
                    Text = $"{item.MaterialName} · {item.ProductName} · {item.DetailName}: {item.Reason}",
                    TextWrapping = TextWrapping.Wrap
                });
            }
            ContentPanel.Children.Add(new Expander
            {
                Header = $"Не размещено: {report.Unplaced.Count}", HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new ScrollViewer { Content = warning, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden }
            });
        }
        foreach (var group in report.Groups)
        {
            var panel = Section();
            panel.Children.Add(SectionHeader(
                group.MaterialName,
                $"{N(group.SheetLength)} x {N(group.SheetWidth)} мм{(group.TextureDirection ? " · текстура" : "")}",
                $"{group.Sheets.Count} лист."));

            foreach (var sheet in group.Sheets)
            {
                panel.Children.Add(SheetView(group.MaterialId, sheet, report.Trim, report.Gap));
            }

            ContentPanel.Children.Add(panel);
        }

        var metrics = CuttingMetrics.Analyze(report, UsefulRemainderBox.Value);
        if (metrics.Remainders.Count > 0)
        {
            var leftovers = Section();
            leftovers.Children.Add(SectionHeader("Полезные остатки", $"От {N(UsefulRemainderBox.Value)} мм", $"{metrics.Remainders.Count} шт."));
            foreach (var remainder in metrics.Remainders)
            {
                leftovers.Children.Add(new TextBlock
                {
                    Text = $"{remainder.MaterialName} · лист {remainder.SheetNumber}: {N(remainder.Length)} × {N(remainder.Width)} мм · {N(remainder.Area)} м²",
                    TextWrapping = TextWrapping.Wrap
                });
            }
            ContentPanel.Children.Add(new Expander { Header = $"Полезные остатки: {metrics.Remainders.Count}", Content = leftovers });
        }
    }

    private UIElement SheetView(string materialId, CutSheet sheet, double trim, double gap)
    {
        var interactive = new InteractiveSheet(sheet, trim, gap) { MaterialId = materialId };
        _interactiveSheets.Add(interactive);

        var wrap = new Border
        {
            Padding = new Thickness(14),
            Margin = new Thickness(0, 12, 0, 0),
            Background = (Brush)Application.Current.Resources["SoftPanelBrush"],
            CornerRadius = new CornerRadius(8)
        };

        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(SectionHeader($"Лист {sheet.Number}", $"{Math.Round(sheet.Efficiency * 100)}% заполнения", ""));

        var host = new Viewbox
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        interactive.Host = host;

        var canvas = new Canvas
        {
            Width = Math.Max(1, sheet.SheetLength * BaseCutScale),
            Height = Math.Max(1, sheet.SheetWidth * BaseCutScale),
            Background = new SolidColorBrush(Colors.White),
            ContextFlyout = CreateCutSheetMenu(interactive)
        };
        canvas.PointerMoved += CutCanvasPointerMoved;
        canvas.PointerPressed += CutSheetPointerPressed;
        canvas.PointerReleased += CutCanvasPointerReleased;
        canvas.PointerCanceled += CancelCutGesture;
        canvas.PointerCaptureLost += CancelCutGesture;
        canvas.Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, canvas.Width, canvas.Height) };
        interactive.Canvas = canvas;

        canvas.Children.Add(new Rectangle
        {
            Width = canvas.Width,
            Height = canvas.Height,
            Stroke = Brush("#697386"),
            StrokeThickness = 1,
            IsHitTestVisible = false
        });

        if (RemaindersButton.IsChecked == true) DrawRemainders(canvas, sheet, trim, gap);
        if (CutLinesSwitch.IsChecked == true) DrawCutLines(canvas, sheet, trim);

        for (var i = 0; i < sheet.Placements.Count; i++)
        {
            DrawPart(interactive, sheet.Placements[i], sheet.Placements[i].DisplayNumber);
        }

        host.Child = canvas;
        stack.Children.Add(host);
        stack.Children.Add(Legend(sheet.Placements));
        wrap.Child = stack;
        return wrap;
    }

    private void DrawRemainders(Canvas canvas, CutSheet sheet, double trim, double gap)
    {
        foreach (var remainder in CuttingMetrics.RemaindersForSheet(sheet, trim, gap, UsefulRemainderBox.Value))
        {
            var block = new Border { Width = remainder.Length * BaseCutScale, Height = remainder.Width * BaseCutScale,
                Background = Brush("#DCEFD8"), BorderBrush = Brush("#4D7745"), BorderThickness = new Thickness(0.8),
                IsHitTestVisible = false };
            if (block.Width >= 55 && block.Height >= 26)
                block.Child = new TextBlock { Text = $"{N(remainder.Length)} × {N(remainder.Width)}", FontSize = 10,
                    Foreground = Brush("#284823"), TextTrimming = TextTrimming.CharacterEllipsis,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Canvas.SetLeft(block, remainder.X * BaseCutScale); Canvas.SetTop(block, remainder.Y * BaseCutScale);
            canvas.Children.Add(block);
        }
    }

    private static void DrawCutLines(Canvas canvas, CutSheet sheet, double trim)
    {
        foreach (var cut in CuttingMetrics.LinesForSheet(sheet, trim))
        {
            var line = new Line
            {
                Stroke = Brush(cut.IsTrim ? "#7C3AED" : "#596575"),
                StrokeThickness = cut.IsTrim ? 1.4 : 0.8,
                StrokeDashArray = new DoubleCollection { cut.IsTrim ? 2 : 3, cut.IsTrim ? 1 : 2 },
                Opacity = cut.IsTrim ? .88 : .64,
                IsHitTestVisible = false
            };
            if (cut.IsVertical)
            {
                line.X1 = line.X2 = cut.Position * BaseCutScale;
                line.Y1 = cut.Start * BaseCutScale; line.Y2 = cut.End * BaseCutScale;
            }
            else
            {
                line.Y1 = line.Y2 = cut.Position * BaseCutScale;
                line.X1 = cut.Start * BaseCutScale; line.X2 = cut.End * BaseCutScale;
            }
            Canvas.SetZIndex(line, 1);
            canvas.Children.Add(line);
        }
    }

    private void DrawPart(InteractiveSheet sheet, CutPlacement placement, int number)
    {
        var part = new CutPartView(sheet, placement);
        sheet.Parts.Add(part);

        var rect = new Rectangle
        {
            Width = Math.Max(1, part.Length * BaseCutScale),
            Height = Math.Max(1, part.Width * BaseCutScale),
            Fill = CutFill(number),
            Stroke = CutStrokeBrush(),
            StrokeThickness = 1
        };
        part.Rect = rect;
        rect.ContextFlyout = CreateCutPartMenu(part);
        sheet.Canvas.Children.Add(rect);

        var hit = new Rectangle
        {
            Fill = Brush("#01000000"),
            Tag = part,
            ContextFlyout = rect.ContextFlyout
        };
        part.HitRect = hit;
        hit.PointerPressed += CutPartPointerPressed;
        sheet.Canvas.Children.Add(hit);

        var label = new TextBlock
        {
            Text = number.ToString(CultureInfo.InvariantCulture),
            Foreground = Brush("#102233"),
            FontSize = 12,
            Margin = new Thickness(4, 1, 0, 0)
        };
        part.Label = label;
        label.IsHitTestVisible = false;
        sheet.Canvas.Children.Add(label);
        if (!part.AllowRotation)
        {
            part.LockIcon = RotationLockIcon();
            part.LockIcon.Foreground = Brush("#102233");
            part.LockIcon.IsHitTestVisible = false;
            sheet.Canvas.Children.Add(part.LockIcon);
            ToolTipService.SetToolTip(hit, "Поворот детали запрещён");
        }
        PositionPart(part);
    }

    private MenuFlyout CreateCutPartMenu(CutPartView part)
    {
        var flyout = new MenuFlyout();
        var rotateOne = new MenuFlyoutItem { Text = "Повернуть деталь", IsEnabled = part.AllowRotation };
        rotateOne.Click += async (_, _) => await RotatePartsAsync(new HashSet<string> { part.InstanceId });
        flyout.Items.Add(rotateOne);

        var rotateSame = new MenuFlyoutItem { Text = "Повернуть такие на этом листе", IsEnabled = part.AllowRotation };
        rotateSame.Click += async (_, _) =>
        {
            if (_cutReport is not null) await RotatePartsAsync(CutEditing.IdenticalIds(_cutReport, part.InstanceId));
        };
        flyout.Items.Add(rotateSame);
        var rotateAll = new MenuFlyoutItem { Text = "Повернуть такие на всех листах", IsEnabled = part.AllowRotation };
        rotateAll.Click += async (_, _) =>
        {
            if (_cutReport is not null) await RotatePartsAsync(CutEditing.IdenticalIds(_cutReport, part.InstanceId, allSheets: true));
        };
        flyout.Items.Add(rotateAll);
        var rotateSelection = new MenuFlyoutItem { Text = "Повернуть выделенные" };
        rotateSelection.Click += async (_, _) => await RotatePartsAsync(_selected.ToHashSet());
        flyout.Items.Add(rotateSelection);
        if (!part.AllowRotation)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(new MenuFlyoutItem { Text = "Поворот заблокирован", IsEnabled = false });
        }
        flyout.Items.Add(new MenuFlyoutSeparator());
        var orderSheet = new MenuFlyoutItem { Text = "Упорядочить лист" };
        orderSheet.Click += async (_, _) => await OrderSheetAsync(part.Sheet);
        flyout.Items.Add(orderSheet);
        return flyout;
    }

    private MenuFlyout CreateCutSheetMenu(InteractiveSheet sheet)
    {
        var flyout = new MenuFlyout();
        var order = new MenuFlyoutItem { Text = "Упорядочить лист" };
        order.Click += async (_, _) => await OrderSheetAsync(sheet);
        flyout.Items.Add(order);
        return flyout;
    }


    private static void PositionPart(CutPartView part)
    {
        part.Rect.Width = Math.Max(1, part.Length * BaseCutScale);
        part.Rect.Height = Math.Max(1, part.Width * BaseCutScale);
        part.Rect.Stroke = CutStrokeBrush();
        part.HitRect.Width = part.Rect.Width;
        part.HitRect.Height = part.Rect.Height;
        Canvas.SetLeft(part.Rect, part.X * BaseCutScale);
        Canvas.SetTop(part.Rect, part.Y * BaseCutScale);
        Canvas.SetLeft(part.HitRect, part.X * BaseCutScale);
        Canvas.SetTop(part.HitRect, part.Y * BaseCutScale);
        Canvas.SetLeft(part.Label, part.X * BaseCutScale + 2);
        Canvas.SetTop(part.Label, part.Y * BaseCutScale + 2);
        if (part.LockIcon is { } icon)
        {
            icon.FontSize = Math.Min(12, Math.Max(6, Math.Min(part.Rect.Width, part.Rect.Height) - 4));
            Canvas.SetLeft(icon, part.X * BaseCutScale + Math.Max(0, part.Rect.Width - icon.FontSize - 3));
            Canvas.SetTop(icon, part.Y * BaseCutScale + 3);
        }
    }

    private static double Clamp(double value, double min, double max) => Math.Max(min, Math.Min(max, value));
    private static double ClampX(CutPartView part, double value) =>
        Clamp(value, part.Sheet.Trim, Math.Max(part.Sheet.Trim, part.Sheet.Width - part.Sheet.Trim - part.Length));
    private static double ClampY(CutPartView part, double value) =>
        Clamp(value, part.Sheet.Trim, Math.Max(part.Sheet.Trim, part.Sheet.Height - part.Sheet.Trim - part.Width));

    private static UIElement Legend(IReadOnlyList<CutPlacement> placements)
    {
        var groups = placements
            .GroupBy(part => part.DisplayNumber).OrderBy(group => group.Key)
            .Select((group, index) => new { Number = group.Key, Index = index, First = group.First(), Count = group.Count() })
            .ToList();
        var grid = new Grid { ColumnSpacing = 20, RowSpacing = 6 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = Math.Max(1, (int)Math.Ceiling(groups.Count / 2d));
        for (var i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var item in groups)
        {
            var text = new TextBlock
            {
                Text = $"{item.Number}. {item.First.ProductName} · {item.First.DetailName} · {N(item.First.BaseLength)} × {N(item.First.BaseWidth)} · {item.Count} шт.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78,
                FontSize = 13
            };
            var index = item.Index;
            Grid.SetColumn(text, index / rows);
            Grid.SetRow(text, index % rows);
            grid.Children.Add(text);
        }
        return grid;
    }

    private static SolidColorBrush AccentBrush()
    {
        return Application.Current.Resources["AppAccentBrush"] is SolidColorBrush accent
            ? new SolidColorBrush(accent.Color)
            : Brush("#0F6CBD");
    }
    private SolidColorBrush CutFill(int number)
    {
        return Brush(CutColors.Fill(_cutReport?.Palette ?? "blue", number));
    }
    private static SolidColorBrush CutStrokeBrush() => Brush(CutColors.Stroke(App.Preferences.Current.CutPalette));

    private CutReport? CurrentCutReport() => _cutReport;
    private DetailingReport? CurrentDetailingReport() => _detailingReport is null
        ? null
        : DspOnlySwitch.IsOn ? CuttingService.FilterDetailingReportToDsp(_detailingReport) : _detailingReport;
    private async void SaveReportClick(object sender, RoutedEventArgs e)
    {
        SetExportBusy(true);
        try
        {
        var isCut = _cutReport is not null;
        var format = isCut ? App.Preferences.Current.CutExportFormat : App.Preferences.Current.DetailingExportFormat;
        var extension = format == "xlsx" ? ".xlsx" : ".pdf";
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = isCut ? "раскрой" : DetailingFileName(_detailingReport?.ProjectName ?? "проект")
        };
        picker.FileTypeChoices.Add(format == "xlsx" ? "Excel" : "PDF", new List<string> { extension });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

            if (CurrentCutReport() is { } cut)
            {
                await Task.Run(() => { if (format == "xlsx") ReportDocumentService.SaveXlsx(cut, file.Path); else ReportDocumentService.SavePdf(cut, file.Path); });
            }
            else if (CurrentDetailingReport() is { } detailing)
            {
                await Task.Run(() => { if (format == "xlsx") ReportDocumentService.SaveXlsx(detailing, file.Path); else ReportDocumentService.SavePdf(detailing, file.Path); });
            }
            HintText.Text = $"Сохранено: {file.Name}";
        }
        catch (Exception ex)
        {
            HintText.Text = $"Не удалось сохранить: {ex.Message}";
        }
        finally { SetExportBusy(false); }
    }

    private static string DetailingFileName(string projectName)
    {
        var invalidCharacters = System.IO.Path.GetInvalidFileNameChars();
        var safeProjectName = string.Concat(projectName.Trim().Select(character =>
            invalidCharacters.Contains(character) ? '_' : character)).Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(safeProjectName)) safeProjectName = "проект";
        return $"Деталировка_{safeProjectName}_{DateTime.Now:yyyy-MM-dd_HH-mm}";
    }

    private async void PrintReportClick(object sender, RoutedEventArgs e)
    {
        SetExportBusy(true);
        try
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vitan-cut-{Guid.NewGuid():N}.pdf");
            if (CurrentCutReport() is { } cut) await Task.Run(() => ReportDocumentService.SavePdf(cut, path));
            else if (CurrentDetailingReport() is { } detailing) await Task.Run(() => ReportDocumentService.SavePdf(detailing, path));
            else return;

            var file = await StorageFile.GetFileFromPathAsync(path);
            var opened = await Windows.System.Launcher.LaunchFileAsync(file);
            HintText.Text = opened ? "PDF открыт для печати" : "Не удалось открыть PDF для печати";
        }
        catch (Exception ex)
        {
            HintText.Text = $"Не удалось подготовить печать: {ex.Message}";
        }
        finally { SetExportBusy(false); }
    }
    private void SetExportBusy(bool busy)
    {
        SaveReportButton.IsEnabled = !busy;
        PrintReportButton.IsEnabled = !busy;
        ResetLayoutButton.IsEnabled = !busy;
        CutTools.IsHitTestVisible = !busy;
        CutVariantBox.IsEnabled = !busy;
        SaveLayoutButton.IsEnabled = !busy;
        ContentPanel.IsHitTestVisible = !busy;
        if (!busy && _cutReport is not null) PaintSelection();
    }
    private void BuildDetailingReport(DetailingReport report)
    {
        var shownReport = DspOnlySwitch.IsOn ? CuttingService.FilterDetailingReportToDsp(report) : report;
        ContentPanel.Children.Clear();
        if (!shownReport.Products.Any())
        {
            ContentPanel.Children.Add(EmptyState("Деталей ДСП нет", "В изделиях не найдено деталей с материалом ДСП."));
            return;
        }

        if (shownReport.Issues.Count > 0)
        {
            var warning = Section();
            warning.Background = (Brush)Application.Current.Resources["AppWarningBrush"];
            warning.Children.Add(SectionHeader("Проверьте деталировку", "Строки с ошибками не должны уходить в производство.", $"{shownReport.Issues.Count} шт."));
            foreach (var issue in shownReport.Issues)
                warning.Children.Add(new TextBlock { Text = $"{issue.ProductName} · {issue.DetailName}: {issue.Message}", TextWrapping = TextWrapping.Wrap });
            ContentPanel.Children.Add(warning);
        }

        var summary = Section();
        summary.Children.Add(SectionHeader("Итоги проекта", $"{N(shownReport.Summary.DetailQuantity)} деталей · {N(shownReport.Summary.Area)} м²", $"{N(shownReport.Summary.Cost)} ₽"));
        ContentPanel.Children.Add(summary);

        foreach (var product in shownReport.Products)
        {
            var section = Section();
            var grid = new Grid { ColumnSpacing = 20 };
            if (report.IncludeImages)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (report.IncludeImages) grid.Children.Add(ProductVisual(product));

            var body = new StackPanel { Spacing = 14 };
            Grid.SetColumn(body, report.IncludeImages ? 1 : 0);
            var legs = product.HasLegs ? $" · ножки {N(product.LegHeight)} мм" : "";
            body.Children.Add(SectionHeader(product.Name, $"{N(product.Length)} x {N(product.Depth)} x {N(product.Height)} мм · {N(product.Qty)} шт.{legs}", ""));
            if (product.FixedSizes.Any())
                body.Children.Add(new TextBlock { Text = "Фиксированные размеры: " + string.Join(" · ", product.FixedSizes.Select(size => $"{size.Name}: {N(size.Value)} мм")), TextWrapping = TextWrapping.Wrap, Opacity = 0.72 });
            body.Children.Add(DetailingTable(product));
            grid.Children.Add(body);
            section.Children.Add(grid);
            ContentPanel.Children.Add(section);
        }
    }
    private static UIElement ProductVisual(DetailingProduct product)
    {
        var border = new Border
        {
            MinHeight = 170,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.Resources["SoftPanelBrush"]
        };

        if (ProductImageData.Read(product.Image) is not null)
        {
            var image = new Image { Width = 196, Height = 146, Stretch = Stretch.Uniform };
            border.Child = image;
            _ = ImagePreviewService.SetAsync(image, product.Image);
            return border;
        }

        var canvas = new Canvas { Width = 196, Height = 146 };
        var body = new Polygon
        {
            Points =
            [
                new Windows.Foundation.Point(34, 24),
                new Windows.Foundation.Point(132, 24),
                new Windows.Foundation.Point(166, 52),
                new Windows.Foundation.Point(166, 122),
                new Windows.Foundation.Point(68, 122),
                new Windows.Foundation.Point(34, 96)
            ],
            Fill = Brush("#cdefff"),
            Stroke = AccentBrush(),
            StrokeThickness = 2
        };
        canvas.Children.Add(body);
        canvas.Children.Add(Line(68, 52, 166, 52));
        canvas.Children.Add(Line(68, 52, 68, 122));
        canvas.Children.Add(Line(34, 24, 68, 52));
        canvas.Children.Add(Line(132, 24, 166, 52));
        var label = new TextBlock
        {
            Text = $"{N(product.Length)} x {N(product.Depth)} x {N(product.Height)}",
            FontSize = 12,
            Opacity = .8
        };
        Canvas.SetLeft(label, 28);
        Canvas.SetTop(label, 130);
        canvas.Children.Add(label);
        border.Child = canvas;
        return border;
    }

    private static UIElement DetailingTable(DetailingProduct product)
    {
        var table = new Grid { RowSpacing = 0 };
        var widths = new[] { 2.1, 2.2, 0.8, 0.8, 0.8, 0.9, 1.0 };
        foreach (var width in widths)
        {
            table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width, GridUnitType.Star) });
        }

        AddTableRow(table, ["Деталь", "Материал", "Кол-во", "Длина", "Ширина", "Квадратура", "Стоимость, руб."], true);

        if (!product.Rows.Any())
        {
            AddTableRow(table, ["Деталей пока нет", "", "", "", "", "", ""], false);
        }

        foreach (var row in product.Rows)
        {
            AddTableRow(table,
            [
                row.DetailName,
                $"{row.Type}\n{row.MaterialName}{(row.HasIssue ? "\n⚠ " + row.Issue : "")}",
                N(row.Qty),
                row.Length > 0 ? N(row.Length) : "—",
                row.Width > 0 ? N(row.Width) : "—",
                row.Area > 0 ? N(row.Area) + " м²" : "—",
                N(row.Cost)
            ], false, row.RotationLocked);
        }

        return table;
    }

    private static FontIcon RotationLockIcon() => new()
    {
        Glyph = "\uE72E", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 14,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static void AddTableRow(Grid table, string[] cells, bool header, bool rotationLocked = false)
    {
        var rowIndex = table.RowDefinitions.Count;
        table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = new Border
            {
                Padding = new Thickness(10),
                BorderBrush = (Brush)Application.Current.Resources["PanelStrokeBrush"],
                BorderThickness = new Thickness(0, 0, 0, 1),
                Background = header ? (Brush)Application.Current.Resources["SoftPanelBrush"] : null
            };
            var text = new TextBlock
            {
                Text = cells[i],
                TextWrapping = TextWrapping.Wrap,
                Opacity = header ? 0.72 : 1,
                FontWeight = header ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
            };
            cell.Child = text;
            if (i == 0 && rotationLocked)
            {
                var content = new Grid { ColumnSpacing = 6 };
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var icon = RotationLockIcon();
                ToolTipService.SetToolTip(icon, "Поворот детали запрещён");
                content.Children.Add(icon);
                Grid.SetColumn(text, 1);
                content.Children.Add(text);
                cell.Child = content;
            }
            Grid.SetRow(cell, rowIndex);
            Grid.SetColumn(cell, i);
            table.Children.Add(cell);
        }
    }

    private static StackPanel Section()
    {
        return new StackPanel
        {
            Spacing = 0,
            Padding = new Thickness(20),
            Background = (Brush)Application.Current.Resources["PanelSurfaceBrush"]
        };
    }

    private static UIElement SectionHeader(string title, string subtitle, string right)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 16), ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var stack = new StackPanel { Spacing = 3 };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            stack.Children.Add(new TextBlock { Text = subtitle, Opacity = 0.68, TextWrapping = TextWrapping.Wrap });
        }
        grid.Children.Add(stack);

        if (!string.IsNullOrWhiteSpace(right))
        {
            var rightText = new TextBlock { Text = right, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top };
            Grid.SetColumn(rightText, 1);
            grid.Children.Add(rightText);
        }

        return grid;
    }

    private static UIElement EmptyState(string title, string text)
    {
        var panel = new StackPanel
        {
            Width = 420,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 8,
            Margin = new Thickness(0, 120, 0, 0)
        };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Center });
        panel.Children.Add(new TextBlock { Text = text, Opacity = 0.68, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        return panel;
    }

    private static Line Line(double x1, double y1, double x2, double y2)
    {
        return new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = AccentBrush(),
            StrokeThickness = 1.5
        };
    }

    private static SolidColorBrush Brush(string hex)
    {
        hex = hex.TrimStart('#');
        var a = hex.Length == 8 ? Convert.ToByte(hex[..2], 16) : (byte)255;
        var start = hex.Length == 8 ? 2 : 0;
        var r = Convert.ToByte(hex.Substring(start, 2), 16);
        var g = Convert.ToByte(hex.Substring(start + 2, 2), 16);
        var b = Convert.ToByte(hex.Substring(start + 4, 2), 16);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
    }

    private sealed class InteractiveSheet(CutSheet sheet, double trim, double gap)
    {
        public string MaterialId { get; init; } = "";
        public int Number { get; } = sheet.Number;
        public double Trim { get; } = trim;
        public double Gap { get; } = gap;
        public double Width { get; } = sheet.SheetLength;
        public double Height { get; } = sheet.SheetWidth;
        public Canvas Canvas { get; set; } = null!;
        public Viewbox Host { get; set; } = null!;
        public List<CutPartView> Parts { get; } = [];
    }

    private sealed class CutPartView(InteractiveSheet sheet, CutPlacement placement)
    {
        public CutPlacement Placement { get; } = placement;
        public InteractiveSheet Sheet { get; } = sheet;
        public string ProductId { get; } = placement.ProductId;
        public string DetailId { get; } = placement.DetailId;
        public string InstanceId { get; } = placement.InstanceId;
        public double BaseLength { get; } = placement.BaseLength;
        public double BaseWidth { get; } = placement.BaseWidth;
        public bool AllowRotation { get; } = placement.AllowRotation;
        public FontIcon? LockIcon { get; set; }
        public bool Rotated { get; set; } = placement.Rotated;
        public double X { get; set; } = placement.X;
        public double Y { get; set; } = placement.Y;
        public double Length { get; set; } = placement.Length;
        public double Width { get; set; } = placement.Width;
        public Rectangle Rect { get; set; } = null!;
        public Rectangle HitRect { get; set; } = null!;
        public TextBlock Label { get; set; } = null!;
    }

    private static string N(double value) => Calculator.Number(value);
}
