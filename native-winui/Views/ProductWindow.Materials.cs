using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Views;

public sealed partial class ProductWindow
{
    private bool _updatingMaterialView;
    private MaterialPickerOption[]? _materialPickerOptions;
    private Database? _materialPickerDatabase;
    private long _materialPickerRevision = -1;
    private string _materialPickerLanguage = "";

    private bool _materialPickerOpen;
    private static int _nextMaterialTraceEditor;
    private readonly int _materialTraceEditor = Interlocked.Increment(ref _nextMaterialTraceEditor);
    private Detail? _materialTraceTarget;

    private void TraceMaterial(string action, Detail? detail = null, string? outcome = null, int? value = null) =>
        App.MaterialPickerTrace.Record(_materialTraceEditor, action,
            detail is null ? null : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(detail), outcome, value);

    private async Task PickRowMaterialAsync(DetailRow row, Windows.Foundation.Point position)
    {
        if (_materialPickerOpen || _editorClosed || !_detailEditor.Contains(row.Detail) || Root.XamlRoot is not { } root)
        {
            TraceMaterial("picker.open-blocked", row.Detail, _materialPickerOpen ? "already-open" : "target-unavailable");
            return;
        }

        _detailViewRequests.Begin();
        SelectDetailRow(row);
        var detail = row.Detail;
        _materialTraceTarget = detail;
        TraceMaterial("picker.open", detail, detail.IsGeneratedEdgeBand ? "generated-edge" : "detail");
        var browse = new MaterialPickerBrowse(MaterialPickerOptions(), App.MaterialLibrary.GetGroups(),
            UiText.Get("Добавить"), detail.IsGeneratedEdgeBand, "Кромка");
        TraceMaterial("picker.filter", detail, "all", browse.MatchCount);

        DetailRow? appliedRow = null;
        var pane = new MaterialPickerPane(browse, _motion, choice =>
        {
            var applied = ApplyRowMaterial(detail, choice, out var error);
            if (error is null) appliedRow = applied;
            return error;
        }, (action, outcome, value) => TraceMaterial(action, detail, outcome, value));
        var surface = new Border
        {
            Child = pane, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            Background = (Brush)Application.Current.Resources["SoftPanelBrush"],
            BorderBrush = (Brush)Application.Current.Resources["PanelStrokeBrush"],
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
        var scrim = new Grid { Background = new SolidColorBrush(Windows.UI.Color.FromArgb(82, 0, 0, 0)) };
        scrim.Children.Add(surface);
        Grid.SetRowSpan(scrim, Root.RowDefinitions.Count);

        var closed = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Finish(string reason, string? category)
        {
            if (closed.TrySetResult(category)) TraceMaterial("picker.close-request", detail, reason);
        }
        void Place()
        {
            if (closed.Task.IsCompleted) return;
            var bounds = MaterialPickerLayout.Place(Root.ActualWidth, Root.ActualHeight, position.X, position.Y, pane.RequiredHeight);
            surface.Width = bounds.Width;
            surface.Height = bounds.Height;
            surface.Margin = new Thickness(bounds.Left, bounds.Top, 0, 0);
        }
        void Resize(object sender, SizeChangedEventArgs args) => Place();
        void CloseWithEditor(object sender, WindowEventArgs args) => Finish("editor-closed", null);

        _materialPickerOpen = true;
        try
        {
            string? category;
            try
            {
                pane.CloseRequested += Finish;
                pane.SizeDemandChanged += Place;
                Root.SizeChanged += Resize;
                Closed += CloseWithEditor;
                ThemeService.SetCursorRevealSuppressed(this, true);
                Place();
                Root.Children.Add(scrim);
                category = await closed.Task;
                if (!_editorClosed && !TryFocusMaterial(appliedRow ?? row)) FocusEditorAnchor();
            }
            finally
            {
                pane.Stop();
                pane.CloseRequested -= Finish;
                pane.SizeDemandChanged -= Place;
                Closed -= CloseWithEditor;
                Root.SizeChanged -= Resize;
                Root.Children.Remove(scrim);
                TraceMaterial("picker.removed", detail);
            }
            if (_editorClosed) return;
            if (category is not null && await MaterialCreateDialog.ShowAsync(this, root, category) is { } selected && !_editorClosed)
            {
                appliedRow = ApplyRowMaterial(detail, selected, out var error);
                if (error is not null) await new ContentDialog
                {
                    XamlRoot = root, RequestedTheme = Root.ActualTheme, Title = UiText.Get("Не удалось сохранить"),
                    Content = error, CloseButtonText = UiText.Get("Закрыть")
                }.ShowAsync();
                NotificationCenter.Publish(UiText.Get("Готово"), UiText.Get("Материал сохранён"));
            }
            if (!_editorClosed) RevealDetail(appliedRow ?? row, DetailFocus.Material);
        }
        finally
        {
            _materialPickerOpen = false;
            if (!_editorClosed && Root.XamlRoot is not null) ThemeService.SetCursorRevealSuppressed(this, false);
        }
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

    private DetailRow? ApplyRowMaterial(Detail detail, MaterialChoice choice, out string? error)
    {
        error = null;
        TraceMaterial("option.apply-start", detail, string.IsNullOrEmpty(detail.MaterialId) ? "was-empty" : "had-material");
        MaterialEditResult result;
        try { result = _detailEditor.ApplyMaterial(detail, choice); }
        catch (InvalidOperationException failure) when (detail.IsGeneratedEdgeBand)
        {
            TraceMaterial("option.apply-error", detail, failure.GetType().Name, failure.HResult);
            error = UiText.Get(failure.Message);
            return null;
        }
        if (result.Status == MaterialEditStatus.MissingDetail)
        {
            TraceMaterial("option.apply-blocked", detail, "detail-missing");
            error = UiText.Get("Деталь больше не доступна. Откройте список материалов заново.");
            return null;
        }
        if (result.Status == MaterialEditStatus.MissingMaterial)
        {
            TraceMaterial("option.apply-blocked", detail, "material-missing");
            error = UiText.Get("Материал недоступен. Откройте список материалов заново.");
            return null;
        }
        if (!detail.IsGeneratedEdgeBand)
            TraceMaterial("option.model-committed", detail, detail.MaterialId == choice.Material.Id && detail.Type == choice.Type ? "matches" : "mismatch");
        if (result.Status == MaterialEditStatus.Applied) UpdateSummary();
        var row = _detailRows.FirstOrDefault(candidate => ReferenceEquals(candidate.Detail, result.Target));
        if (row is not null)
        {
            SelectDetailRow(row);
            UpdateMaterialRowView(row);
        }
        TraceMaterial("option.apply-end", result.Target ?? detail, row?.MaterialButton is null ? "no-view" : "view-updated");
        return row;
    }

    private void UpdateMaterialRowView(DetailRow row, bool updateHeaders = true)
    {
        if (updateHeaders) UpdateDetailHeaders();
        if (row.Container is not { Child: Grid grid } container || !ReferenceEquals(container.Tag, row)) return;
        UpdateEdgeBandButton(row);
        var material = row.SelectedMaterial?.Material;
        var name = material?.Name ?? UiText.Get("Выберите материал");
        if (row.MaterialName is { } label) label.Text = name;
        if (row.MaterialImage is { } image && !Equals(image.Tag, material?.Texture))
        {
            image.Tag = material?.Texture;
            _ = LoadMaterialTextureAsync(image, material?.Texture);
        }
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
        if (row.MaterialButton is { } button) ToolTipService.SetToolTip(button, name);
    }

    private static Task LoadMaterialTextureAsync(Image image, string? source) =>
        ImagePreviewService.SetVisibleAsync(image, source ?? "");

    private static string UnitLabel(string unit) => unit switch
    {
        "m2" => UiText.Get("м²"),
        "lm" => UiText.Get("пог. м"),
        _ => UiText.Get("шт.")
    };
}
