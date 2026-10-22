using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Views;

public sealed partial class ProductWindow
{
    private void UpdateEdgeBandButton(DetailRow row)
    {
        if (row.EdgeBandButton is not { } button) return;
        button.Visibility = EdgeBandingService.IsEnabled(App.State, row.Detail) ? Visibility.Visible : Visibility.Collapsed;
        button.Style = (Style)Application.Current.Resources[row.Detail.EdgeBands.Count > 0 ? "AccentButtonStyle" : "UiButtonStyle"];
        ToolTipService.SetToolTip(button,UiText.Get("Кромление детали «") + row.Detail.Name + "»");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button,UiText.Get("Кромка: ") + row.Detail.Name);
    }

    private void SyncEdgeBandRows()
    {
        if (!_sourceProduct.Details.Any(detail => detail.IsGeneratedEdgeBand || detail.EdgeBands.Count > 0)) return;
        EdgeBandingService.Refresh(App.State, _sourceProduct);
        foreach (var row in _detailRows.Where(row => row.Detail.IsGeneratedEdgeBand && !_sourceProduct.Details.Contains(row.Detail)).ToList())
        {
            if (row.Container is { } container) DetailsRowsPanel.Children.Remove(container);
            foreach (var field in _formulaFields.Where(pair => pair.Value.Detail == row.Detail).Select(pair => pair.Key).ToList())
            {
                if (_formulaFlyouts.Remove(field, out var flyout) && flyout.IsOpen) flyout.Hide();
                _formulaSuggestionLists.Remove(field);
                _formulaQueries.Remove(field);
                _formulaFields.Remove(field);
            }
            _detailRows.Remove(row);
            if (_selectedDetailRow == row) _selectedDetailRow = _detailRows.FirstOrDefault();
        }
        var choices = App.State.MaterialChoices().ToList();
        foreach (var detail in _sourceProduct.Details.Where(detail => detail.IsGeneratedEdgeBand && _detailRows.All(row => row.Detail != detail)))
        {
            var row = CreateDetailRow(detail, choices);
            _detailRows.Add(row);
            var view = CreateDetailRowView(row);
            DetailsRowsPanel.Children.Add(view);
            ThemeService.ApplyLayout(view, App.Preferences.Current);
        }
        foreach (var row in _detailRows) UpdateEdgeBandButton(row);
        UpdateDetailColumns();
        UpdateDetailSelectionVisuals();
        UpdateDetailHeaders();
    }

    private void OpenEdgeBanding(DetailRow row, Button anchor)
    {
        SaveProductFields();
        var length = FormulaEvaluator.Evaluate(_sourceProduct, row.Detail);
        var width = FormulaEvaluator.Evaluate(_sourceProduct, row.Detail, width: true);
        var valid = length.IsValid && width.IsValid && length.Value > 0 && width.Value > 0;
        var flyout = new Flyout { ShouldConstrainToRootBounds = true };
        var panel = new StackPanel { Width = 300, Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = row.Detail.Name, TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        var choices = App.State.MaterialChoices().Where(choice =>
            string.Equals(choice.Type, "Кромка", StringComparison.CurrentCultureIgnoreCase) && choice.Material.Unit == "lm").ToList();
        var materials = new ComboBox { Header = UiText.Get("Материал кромки"), ItemsSource = choices, DisplayMemberPath = "Material.Name",
            HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = UiText.Get("Выберите кромку") };
        var current = row.Detail.EdgeBands.FirstOrDefault();
        materials.SelectedItem = choices.FirstOrDefault(choice => choice.Material.Id == current?.MaterialId) ?? choices.FirstOrDefault();
        panel.Children.Add(materials);
        if (choices.Count == 0)
            panel.Children.Add(new TextBlock { Text = UiText.Get("Добавьте материал в категорию «Кромка» с единицей «пог. м»."), TextWrapping = TextWrapping.Wrap });

        var drawing = new Canvas { Width = 300, Height = 200 };
        var scale = valid ? Math.Min(240 / length.Value, 140 / width.Value) : 1;
        var partWidth = valid ? length.Value * scale : 180;
        var partHeight = valid ? width.Value * scale : 100;
        var x = (300 - partWidth) / 2;
        var y = (200 - partHeight) / 2;
        var face = new Border { Width = partWidth, Height = partHeight,
            Background = (Brush)Application.Current.Resources["SoftPanelBrush"],
            BorderBrush = (Brush)Application.Current.Resources["PanelStrokeBrush"], BorderThickness = new Thickness(1) };
        Canvas.SetLeft(face, x);
        Canvas.SetTop(face, y);
        drawing.Children.Add(face);
        var buttons = new Dictionary<string, ToggleButton>();
        foreach (var (side, label) in new[] { ("top", "Верхняя грань"), ("right", "Правая грань"), ("bottom", "Нижняя грань"), ("left", "Левая грань") })
        {
            var horizontal = side is "top" or "bottom";
            var edgeButton = new ToggleButton { Width = horizontal ? partWidth : 14, Height = horizontal ? 14 : partHeight,
                MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), CornerRadius = new CornerRadius(2),
                IsEnabled = valid && choices.Count > 0 };
            Canvas.SetLeft(edgeButton, side == "left" ? x - 14 : side == "right" ? x + partWidth : x);
            Canvas.SetTop(edgeButton, side == "top" ? y - 14 : side == "bottom" ? y + partHeight : y);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(edgeButton,label);
            ToolTipService.SetToolTip(edgeButton,label + " · " + Calculator.Number(horizontal ? length.Value : width.Value) + UiText.Get(" мм"));
            edgeButton.Click += (_, _) =>
            {
                if (materials.SelectedItem is not MaterialChoice selected) return;
                EdgeBandingService.SetSide(row.Detail, side, edgeButton.IsChecked == true ? selected : null);
                UpdateSummary();
                PaintEdges();
            };
            buttons.Add(side, edgeButton);
            drawing.Children.Add(edgeButton);
        }
        void PaintEdges()
        {
            var selected = materials.SelectedItem as MaterialChoice;
            foreach (var (side, control) in buttons)
            {
                var edge = row.Detail.EdgeBands.FirstOrDefault(edge => edge.Side == side);
                control.IsChecked = edge is not null && edge.MaterialId == selected?.Material.Id;
                control.Background = (Brush)Application.Current.Resources[edge is not null ? "AppAccentBrush" : "PanelStrokeBrush"];
            }
        }
        materials.SelectionChanged += (_, _) => PaintEdges();
        PaintEdges();
        panel.Children.Add(drawing);
        panel.Children.Add(new TextBlock { Text = valid ? UiText.Format($"{Calculator.Number(length.Value)} × {Calculator.Number(width.Value)} мм") :
            UiText.Get("Проверьте размеры и формулы детали."), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap });
        flyout.Content = panel;
        if (row.EdgeBandPointer is { } pointer)
            flyout.ShowAt(Root, new FlyoutShowOptions { Position = pointer, Placement = FlyoutPlacementMode.Auto });
        else flyout.ShowAt(anchor);
    }
}
