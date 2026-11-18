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
    private string _formulaNamesSignature = "";
    private long _formulaNamesVersion;
    private bool _insertingReference;

    private readonly Dictionary<TextBox, Flyout> _formulaFlyouts = [];
    private readonly Dictionary<TextBox, ListView> _formulaSuggestionLists = [];
    private readonly Dictionary<TextBox, FormulaQuery> _formulaQueries = [];
    private sealed record FormulaQuery(FormulaField Field, string Text, int Start, int Caret);
    private readonly Dictionary<TextBox, FormulaField> _formulaFields = [];
    private bool _refreshingFormulaText;
    private sealed record FormulaField(Detail Detail, bool Width, FormulaPresentation Presentation)
    {
        public string Expression => Width ? Detail.WidthExpr : Detail.LengthExpr;
        public string? DisplayedExpression { get; set; }
        public string? InputText { get; set; }
        public long DisplayedNamesVersion { get; set; } = -1;
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
            if (_refreshingFormulaText || !TryGetBoundFormula(target, out var field)) return;
            if (field.InputText == target.Text) return;
            field.InputText = target.Text;
            var expression = field.Presentation.Storage(target.Text);
            if (field.Width) field.Detail.WidthExpr = expression;
            else field.Detail.LengthExpr = expression;
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

    private bool TryGetBoundFormula(TextBox target, out FormulaField field)
    {
        if (_formulaFields.TryGetValue(target, out var current) && target.Tag is DetailRow row &&
            ReferenceEquals(current.Detail, row.Detail) && _detailEditor.Contains(current.Detail))
        {
            field = current;
            return true;
        }
        field = null!;
        return false;
    }

    private void ConfigureFormulaSuggestions(TextBox target)
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
        list.ItemClick += (_, args) =>
        {
            if (args.ClickedItem is FormulaReference reference) InsertFormulaSuggestion(target, reference);
        };
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
        if (!_formulaQueries.TryGetValue(target, out var query) ||
            !TryGetBoundFormula(target, out var field) || !ReferenceEquals(query.Field, field)) return;
        var caret = query.Caret;
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
            !TryGetBoundFormula(target, out var field) || !ReferenceEquals(query.Field, field)) return;
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
        if (!TryGetBoundFormula(target, out var field) || !FormulaSuggestionsOwnFocus(target))
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
            ConfigureFormulaSuggestions(target);
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
        _formulaQueries[target] = new FormulaQuery(field, text, queryStart, caret);
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

    public sealed record FormulaReference(string Section, string Label, string Expression, string Value)
    {
        public override string ToString() => $"{Section}: {Label}, {Value}";
    }
}
