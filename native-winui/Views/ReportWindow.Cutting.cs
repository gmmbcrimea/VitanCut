using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;
using Windows.Foundation;
using Windows.System;

namespace VitanCut.WinUI.Views;

public sealed partial class ReportWindow
{
    private readonly CutReport? _baseline;
    private readonly string _signature = "";
    private List<SavedCutPlan> _plans = [];
    private readonly Action<IReadOnlyList<SavedCutPlan>, string>? _savePlans;
    private string _activePlanId = "";
    private CutReport? _editOrigin;
    private bool _loadingVariants;
    private bool _cutBusy;
    private bool _cutClosed;
    private readonly HashSet<string> _selected = [];
    private readonly Stack<CutReport> _undo = [];
    private InteractiveSheet? _marqueeSheet;
    private Point _marqueeStart;
    private Rectangle? _marquee;
    private Point _pressPosition;
    private bool _dragMoved;
    private InteractiveSheet? _dropSheet;
    private readonly List<(Canvas Canvas, Rectangle Shape, CutPartView Part)> _ghosts = [];

    private void InitializeCutTools(string activeId)
    {
        if (_baseline is null) return;
        if (_plans.Count == 0) _plans.Add(CutPlanService.Capture(_baseline, _signature, "Автоматический", "Гильотинный", true));
        _activePlanId = _plans.FirstOrDefault(p => p.Id == activeId && CutPlanService.Restore(_baseline, p) is not null)?.Id
            ?? _plans.First(p => CutPlanService.Restore(_baseline, p) is not null).Id;
        _cutReport = CutPlanService.Restore(_baseline, _plans.First(p => p.Id == _activePlanId))!;
        _editOrigin = _cutReport;
        CutVariantBox.SelectionChanged += ChangeVariant;
        SaveLayoutButton.Click += SaveNamedLayout;
        CutLinesSwitch.Checked += (_, _) => RenderCut();
        CutLinesSwitch.Unchecked += (_, _) => RenderCut();
        RemaindersButton.Checked += (_, _) => RenderCut();
        RemaindersButton.Unchecked += (_, _) => RenderCut();
        UsefulRemainderBox.Value = App.Preferences.Current.CutUsefulRemainder;
        UsefulRemainderBox.ValueChanged += (_, _) =>
        {
            if (double.IsFinite(UsefulRemainderBox.Value)) App.Preferences.SetCutUsefulRemainder(UsefulRemainderBox.Value);
            RenderCut();
        };
        RotateSelectionButton.Click += async (_, _) => await RotatePartsAsync(_selected.ToHashSet());
        MoveSelectionButton.Click += async (_, _) =>
        {
            if (_cutReport is null || TargetSheetBox.SelectedItem is not ComboBoxItem { Tag: InteractiveSheet target }) return;
            var anchor = _cutReport.Groups.SelectMany(g => g.Sheets).SelectMany(s => s.Placements).FirstOrDefault(p => _selected.Contains(p.InstanceId));
            var report = _cutReport;
            var ids = _selected.ToHashSet();
            if (anchor is not null) await ComputeEditAsync(() => CutEditing.Move(report, ids, target.MaterialId, target.Number, anchor.InstanceId, anchor.X, anchor.Y));
        };
        ClearSelectionButton.Click += (_, _) => { _selected.Clear(); PaintSelection(); };
        UndoCutButton.Click += (_, _) =>
        {
            if (_undo.Count == 0 || _cutBusy) return;
            var previous = _undo.Peek();
            if (CommitEdit(previous, false)) _undo.Pop();
            PaintSelection();
        };
        Root.KeyDown += (_, e) => { if (e.Key == VirtualKey.Escape) { CancelGesture(); _selected.Clear(); PaintSelection(); } };
        Closed += (_, _) => { _cutClosed = true; CancelGesture(); };
        ReportScroll.SizeChanged += (_, _) => SizeSheetViews();
        RefreshVariants();
        RenderCut();
    }

    private void RefreshVariants()
    {
        _loadingVariants = true;
        CutVariantBox.Items.Clear();
        foreach (var plan in _plans)
        {
            var report = _baseline is null ? null : CutPlanService.Restore(_baseline, plan);
            var label = report is null ? $"{plan.Name} · устарел" : $"{plan.Name} · {CutOptimization.SheetCount(report)} лист.";
            var item = new ComboBoxItem { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 }, Tag = plan, IsEnabled = report is not null };
            ToolTipService.SetToolTip(item, label);
            CutVariantBox.Items.Add(item);
            if (plan.Id == _activePlanId) CutVariantBox.SelectedItem = item;
        }
        _loadingVariants = false;
    }

    private void ChangeVariant(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingVariants || _cutBusy || _baseline is null || CutVariantBox.SelectedItem is not ComboBoxItem { Tag: SavedCutPlan plan }) return;
        var restored = CutPlanService.Restore(_baseline, plan);
        if (restored is null) return;
        try
        {
            _savePlans?.Invoke(_plans, plan.Id);
            CancelGesture(); _activePlanId = plan.Id; _cutReport = restored; _editOrigin = restored;
            _selected.Clear(); _undo.Clear(); RenderCut();
        }
        catch (Exception error) { HintText.Text = error.Message; RefreshVariants(); }
    }

    private void RenderCut()
    {
        if (_cutReport is null) return;
        var scroll = ReportScroll.VerticalOffset;
        BuildCutReport(_cutReport);
        SizeSheetViews();
        TargetSheetBox.Items.Clear();
        foreach (var sheet in _interactiveSheets)
        {
            var group = _cutReport.Groups.First(g => g.MaterialId == sheet.MaterialId);
            TargetSheetBox.Items.Add(new ComboBoxItem { Content = $"{group.MaterialName} · {sheet.Number}", Tag = sheet });
        }
        if (TargetSheetBox.Items.Count > 0) TargetSheetBox.SelectedIndex = 0;
        PaintSelection();
        var metrics = CuttingMetrics.Analyze(_cutReport, UsefulRemainderBox.Value);
        var sheetArea = _cutReport.Groups.SelectMany(g => g.Sheets).Sum(s => s.SheetLength * s.SheetWidth) / 1_000_000;
        CutMetricsText.Text = $"Листов: {CutOptimization.SheetCount(_cutReport)}\nПлощадь листов: {N(sheetArea)} м²\nПолезных остатков: {metrics.Remainders.Count}\nРезов: {metrics.Cuts.Count}\nДлина резов: {N(metrics.Cuts.Length / 1000)} м";
        HintText.Text = $"Листов: {CutOptimization.SheetCount(_cutReport)} · Не размещено: {_cutReport.Unplaced.Count}";
        DispatcherQueue.TryEnqueue(() => ReportScroll.ChangeView(null, scroll, null, true));
    }

    private void SizeSheetViews()
    {
        var width = Math.Max(160, (ReportScroll.ActualWidth > 0 ? ReportScroll.ActualWidth : AppWindow.Size.Width - 224) - 100);
        var height = Math.Max(200, (ReportScroll.ActualHeight > 0 ? ReportScroll.ActualHeight : AppWindow.Size.Height - 40) - 290);
        foreach (var sheet in _interactiveSheets)
        {
            var ratio = sheet.Width / sheet.Height;
            sheet.Host.Width = Math.Min(width, height * ratio);
            sheet.Host.Height = sheet.Host.Width / ratio;
        }
        PaintSelection();
    }

    private void PaintSelection()
    {
        var parts = _interactiveSheets.SelectMany(s => s.Parts).ToList();
        _selected.IntersectWith(parts.Select(p => p.InstanceId));
        foreach (var part in parts)
        {
            part.Rect.Stroke = _selected.Contains(part.InstanceId) ? Brush("#102233") : CutStrokeBrush();
            var scale = part.Sheet.Host.Width / (part.Sheet.Width * BaseCutScale);
            part.Rect.StrokeThickness = (_selected.Contains(part.InstanceId) ? 3 : 1.25) / scale;
            part.Label.FontSize = Math.Min(14 / scale, Math.Max(1, Math.Min(part.Rect.Width, part.Rect.Height) - 4));
        }
        SelectionText.Text = $"Выделено: {_selected.Count}";
        RotateSelectionButton.IsEnabled = !_cutBusy && _selected.Count > 0 && parts.Where(p => _selected.Contains(p.InstanceId)).All(p => p.AllowRotation);
        MoveSelectionButton.IsEnabled = !_cutBusy && _selected.Count > 0;
        UndoCutButton.IsEnabled = !_cutBusy && _undo.Count > 0;
    }

    private async void SaveNamedLayout(object sender, RoutedEventArgs e)
    {
        if (_cutBusy || _cutReport is null) return;
        _cutBusy = true;
        try
        {
            var existing = _plans.FirstOrDefault(p => p.Id == _activePlanId && !p.Automatic);
            var name = new TextBox { Text = existing?.Name ?? $"Пользовательский {DateTime.Now:dd.MM HH:mm}", MaxLength = 100 };
            var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = "Сохранить вариант", Content = name,
                PrimaryButtonText = "Сохранить", CloseButtonText = "Отмена", DefaultButton = ContentDialogButton.Primary };
            if (existing is null && (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(name.Text))) return;
            var plan = CutPlanService.Capture(_cutReport, _signature, name.Text.Trim(), "Пользовательский");
            if (existing is not null) plan.Id = existing.Id;
            var next = _plans.Where(p => p.Id != plan.Id).Append(plan).ToList();
            _savePlans?.Invoke(next, plan.Id);
            _plans = next; _activePlanId = plan.Id; _editOrigin = _cutReport;
            RefreshVariants(); HintText.Text = "Вариант сохранён в проекте";
        }
        catch (Exception error) { HintText.Text = error.Message; }
        finally { _cutBusy = false; PaintSelection(); }
    }

    private bool CommitEdit(CutReport nextReport, bool remember = true)
    {
        if (_cutReport is null) return false;
        try
        {
            if (remember) _undo.Push(_cutReport);
            _cutReport = nextReport;
            RenderCut(); HintText.Text = "Есть несохранённые изменения"; return true;
        }
        catch (Exception error) { HintText.Text = $"Правка не сохранена: {error.Message}"; return false; }
    }

    private async Task ApplyEditAsync(CutEditProposal proposal)
    {
        if (_cutBusy) return;
        if (!proposal.Success) { HintText.Text = proposal.Error; return; }
        _cutBusy = true;
        try
        {
            var nextReport = proposal.Report;
            if (proposal.NewSheets > 0)
            {
                var title = string.IsNullOrWhiteSpace(proposal.ConfirmationTitle) ? "Недостаточно места на листе" : proposal.ConfirmationTitle;
                var content = string.IsNullOrWhiteSpace(proposal.ConfirmationContent)
                    ? $"Часть деталей не помещается. Создать новых листов: {proposal.NewSheets}? Они будут вставлены сразу после листа, с которым выполняется операция."
                    : proposal.ConfirmationContent;
                var action = string.IsNullOrWhiteSpace(proposal.ConfirmationAction) ? "Создать и перенести" : proposal.ConfirmationAction;
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = title,
                    Content = $"{content}\n\n{proposal.SheetSummary}", PrimaryButtonText = action,
                    CloseButtonText = "Отмена", DefaultButton = ContentDialogButton.Close };
                if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            }
            var emptied = NewlyEmptySheets(nextReport);
            if (emptied.Count > 0)
            {
                var names = string.Join(", ", emptied.Select(sheet => $"лист {sheet.Number}"));
                var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = "Лист освобождён",
                    Content = $"{names} больше не содержит деталей. Удалить {(emptied.Count == 1 ? "его" : "их")} из карты раскроя?",
                    PrimaryButtonText = "Удалить", CloseButtonText = "Оставить", DefaultButton = ContentDialogButton.Close };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary) nextReport = CutEditing.RemoveEmptySheets(nextReport, emptied);
            }
            CommitEdit(nextReport);
        }
        finally { _cutBusy = false; PaintSelection(); }
    }

    private async Task ComputeEditAsync(Func<CutEditProposal> compute)
    {
        if (_cutBusy || _cutClosed) return;
        _cutBusy = true; SetExportBusy(true);
        CutEditProposal? proposal = null;
        try { proposal = await Task.Run(compute); }
        catch (Exception error) { if (!_cutClosed) HintText.Text = error.Message; }
        finally { _cutBusy = false; if (!_cutClosed) SetExportBusy(false); }
        if (!_cutClosed && proposal is not null) await ApplyEditAsync(proposal);
    }

    private Task RotatePartsAsync(HashSet<string> ids)
    {
        var report = _cutReport;
        return report is null || _cutBusy ? Task.CompletedTask : ComputeEditAsync(() => CutEditing.Rotate(report, ids));
    }

    private Task OrderSheetAsync(InteractiveSheet sheet)
    {
        var report = _cutReport;
        return report is null || _cutBusy ? Task.CompletedTask : ComputeEditAsync(() => CutEditing.OrderSheet(report, sheet.MaterialId, sheet.Number));
    }
    private void ResetLayoutClick(object sender, RoutedEventArgs e) { if (!_cutBusy && _editOrigin is not null) CommitEdit(_editOrigin); }

    private List<CutSheetReference> NewlyEmptySheets(CutReport next)
    {
        if (_cutReport is null) return [];
        var after = next.Groups.ToDictionary(group => group.MaterialId, group => group.Sheets.ToDictionary(sheet => sheet.Number));
        return _cutReport.Groups.SelectMany(group => group.Sheets
                .Where(sheet => sheet.Placements.Count > 0 && after.TryGetValue(group.MaterialId, out var sheets) &&
                    sheets.TryGetValue(sheet.Number, out var nextSheet) && nextSheet.Placements.Count == 0)
                .Select(sheet => new CutSheetReference(group.MaterialId, sheet.Number)))
            .ToList();
    }

    private void CutPartPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_cutBusy || sender is not FrameworkElement { Tag: CutPartView part } || !e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed) return;
        if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control))
        {
            if (!_selected.Add(part.InstanceId)) _selected.Remove(part.InstanceId);
            PaintSelection(); e.Handled = true; return;
        }
        if (!_selected.Contains(part.InstanceId)) { _selected.Clear(); _selected.Add(part.InstanceId); }
        PaintSelection();
        _dragPart = part; _dragPointerId = e.Pointer.PointerId; _dragMoved = false;
        _pressPosition = e.GetCurrentPoint(Root).Position;
        var point = e.GetCurrentPoint(part.Sheet.Canvas).Position;
        _dragOffsetX = point.X / BaseCutScale - part.X;
        _dragOffsetY = point.Y / BaseCutScale - part.Y;
        part.Sheet.Canvas.CapturePointer(e.Pointer); e.Handled = true;
    }

    private void CutSheetPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_cutBusy || _dragPart is not null || !e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed) return;
        _marqueeSheet = _interactiveSheets.FirstOrDefault(s => s.Canvas == sender as Canvas);
        if (_marqueeSheet is null) return;
        if (!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)) _selected.Clear();
        _marqueeStart = e.GetCurrentPoint(_marqueeSheet.Canvas).Position;
        _dragPointerId = e.Pointer.PointerId;
        _marquee = new Rectangle { Stroke = Brush("#0078d4"), Fill = Brush("#220078d4"), StrokeThickness = 1, IsHitTestVisible = false };
        Canvas.SetLeft(_marquee, _marqueeStart.X); Canvas.SetTop(_marquee, _marqueeStart.Y);
        _marquee.Width = 0; _marquee.Height = 0;
        _marqueeSheet.Canvas.Children.Add(_marquee); Canvas.SetZIndex(_marquee, 3000);
        _marqueeSheet.Canvas.CapturePointer(e.Pointer); PaintSelection(); e.Handled = true;
    }

    private void CutCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointerId != e.Pointer.PointerId) return;
        if (_marqueeSheet is not null && _marquee is not null)
        {
            var point = e.GetCurrentPoint(_marqueeSheet.Canvas).Position;
            Canvas.SetLeft(_marquee, Math.Min(point.X, _marqueeStart.X)); Canvas.SetTop(_marquee, Math.Min(point.Y, _marqueeStart.Y));
            _marquee.Width = Math.Abs(point.X - _marqueeStart.X); _marquee.Height = Math.Abs(point.Y - _marqueeStart.Y);
            e.Handled = true; return;
        }
        if (_dragPart is null || _cutReport is null) return;
        var rootPoint = e.GetCurrentPoint(Root).Position;
        if (Math.Abs(rootPoint.X - _pressPosition.X) + Math.Abs(rootPoint.Y - _pressPosition.Y) < 4 && !_dragMoved) return;
        _dragMoved = true;
        var target = SheetAt(e);
        if (target != _dropSheet)
        {
            ClearGhosts(); _dropSheet = target;
            if (target is not null)
            foreach (var part in _interactiveSheets.SelectMany(s => s.Parts).Where(p => _selected.Contains(p.InstanceId)))
            {
                var ghost = new Rectangle { Width = part.Length * BaseCutScale, Height = part.Width * BaseCutScale,
                    Fill = Brush("#660078d4"), Stroke = Brush("#0078d4"), StrokeThickness = 2, IsHitTestVisible = false };
                Canvas.SetZIndex(ghost, 2000); target.Canvas.Children.Add(ghost); _ghosts.Add((target.Canvas, ghost, part));
            }
        }
        if (target is not null)
        {
            var wanted = DropPosition(e, target);
            foreach (var (_, ghost, part) in _ghosts)
            {
                Canvas.SetLeft(ghost, (part.X + wanted.X - _dragPart.X) * BaseCutScale);
                Canvas.SetTop(ghost, (part.Y + wanted.Y - _dragPart.Y) * BaseCutScale);
            }
        }
        var scrollPoint = e.GetCurrentPoint(ReportScroll).Position;
        if (scrollPoint.Y < 32) ReportScroll.ChangeView(null, Math.Max(0, ReportScroll.VerticalOffset - 24), null, true);
        else if (scrollPoint.Y > ReportScroll.ActualHeight - 32) ReportScroll.ChangeView(null, ReportScroll.VerticalOffset + 24, null, true);
        e.Handled = true;
    }

    private InteractiveSheet? SheetAt(PointerRoutedEventArgs e) => _interactiveSheets.FirstOrDefault(s =>
    {
        var p = e.GetCurrentPoint(s.Canvas).Position;
        return p.X >= 0 && p.Y >= 0 && p.X <= s.Canvas.Width && p.Y <= s.Canvas.Height;
    });

    private (double X, double Y) DropPosition(PointerRoutedEventArgs e, InteractiveSheet sheet)
    {
        var point = e.GetCurrentPoint(sheet.Canvas).Position;
        var part = _dragPart!.Placement with { X = point.X / BaseCutScale - _dragOffsetX, Y = point.Y / BaseCutScale - _dragOffsetY };
        return CutEditing.Snap(part, sheet.Parts.Where(p => !_selected.Contains(p.InstanceId)).Select(p => p.Placement).ToList(), sheet.Width, sheet.Height, sheet.Trim, sheet.Gap);
    }

    private async void CutCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_dragPointerId != e.Pointer.PointerId) return;
        Func<CutEditProposal>? compute = null;
        if (_marqueeSheet is not null && _marquee is not null)
        {
            var rect = new Rect(Canvas.GetLeft(_marquee), Canvas.GetTop(_marquee), _marquee.Width, _marquee.Height);
            foreach (var part in _marqueeSheet.Parts)
                if (part.X * BaseCutScale < rect.Right && (part.X + part.Length) * BaseCutScale > rect.Left &&
                    part.Y * BaseCutScale < rect.Bottom && (part.Y + part.Width) * BaseCutScale > rect.Top) _selected.Add(part.InstanceId);
        }
        else if (_dragPart is not null && _dragMoved && _cutReport is not null)
        {
            var part = _dragPart;
            var report = _cutReport;
            var ids = _selected.ToHashSet();
            var anchorId = part.InstanceId;
            if (SheetAt(e) is { } target)
            {
                var point = DropPosition(e, target);
                compute = () => CutEditing.Move(report, ids, target.MaterialId, target.Number, anchorId, point.X, point.Y);
            }
            else if (ReleasedOutsideSheet(e, part.Sheet))
            {
                compute = () => CutEditing.MoveToNewSheet(report, ids, part.Sheet.MaterialId, part.Sheet.Number, anchorId);
            }
        }
        CancelGesture(); PaintSelection(); e.Handled = true;
        if (compute is not null) await ComputeEditAsync(compute);
    }

    private void ClearGhosts() { foreach (var (canvas, shape, _) in _ghosts) canvas.Children.Remove(shape); _ghosts.Clear(); }
    private static bool ReleasedOutsideSheet(PointerRoutedEventArgs e, InteractiveSheet sheet)
    {
        var point = e.GetCurrentPoint(sheet.Canvas).Position;
        return point.X < 0 || point.Y < 0 || point.X > sheet.Canvas.Width || point.Y > sheet.Canvas.Height;
    }
    private void CancelCutGesture(object sender, PointerRoutedEventArgs e) { if (_dragPointerId == e.Pointer.PointerId) CancelGesture(); }
    private void CancelGesture()
    {
        var canvas = _dragPart?.Sheet.Canvas ?? _marqueeSheet?.Canvas;
        _dragPointerId = null; _dragPart = null; _dropSheet = null;
        if (_marqueeSheet is not null && _marquee is not null) _marqueeSheet.Canvas.Children.Remove(_marquee);
        _marqueeSheet = null; _marquee = null; ClearGhosts(); canvas?.ReleasePointerCaptures();
    }
}
