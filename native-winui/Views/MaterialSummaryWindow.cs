using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VitanCut.WinUI.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace VitanCut.WinUI.Views;

public sealed class MaterialSummaryWindow : Window
{
    public MaterialSummaryWindow(MaterialSummaryReport report)
    {
        Title = UiText.Get("Сводная таблица");
        var root = new Grid { Padding = new Thickness(24), RowSpacing = 16 };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new Grid { ColumnSpacing = 16 };
        heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock { Text = report.ProjectName, FontSize = 24, TextWrapping = TextWrapping.Wrap });
        var save = new Button { Content = UiText.Get("Сохранить в Excel"), VerticalAlignment = VerticalAlignment.Center };
        CommandAppearance.Decorate(save);
        Grid.SetColumn(save, 1);
        heading.Children.Add(save);
        root.Children.Add(heading);
        var table = new StackPanel { MinWidth = 660, Spacing = 4 };
        Grid MakeRow(string[] values, bool bold = false)
        {
            var grid = new Grid { ColumnSpacing = 16, Padding = new Thickness(12, 8, 12, 8) };
            foreach (var size in new[] { new GridLength(1, GridUnitType.Star), new GridLength(80), new GridLength(100), new GridLength(125), new GridLength(125) })
                grid.ColumnDefinitions.Add(new() { Width = size });
            for (var i = 0; i < values.Length; i++)
            {
                var text = new TextBlock { Text = values[i], TextWrapping = TextWrapping.Wrap,
                    FontWeight = bold ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                    HorizontalAlignment = i >= 2 ? HorizontalAlignment.Right : HorizontalAlignment.Stretch };
                Grid.SetColumn(text, i);
                grid.Children.Add(text);
            }
            return grid;
        }
        table.Children.Add(MakeRow(new[] { "Материал", "Единица", "Количество", "Цена за единицу", "Стоимость" }.Select(UiText.Get).ToArray(), true));
        foreach (var row in report.Rows)
            table.Children.Add(MakeRow([row.Name, MaterialSummaryService.UnitLabel(row.Unit), Calculator.Number(row.Quantity, 3),
                Calculator.Number(row.UnitPrice), Calculator.Number(row.Cost)]));
        if (report.Rows.Count == 0) table.Children.Add(new TextBlock { Text = UiText.Get("Материалы не найдены"), Margin = new Thickness(12) });
        foreach (var issue in report.Issues)
            table.Children.Add(new TextBlock { Text = issue, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 4, 12, 4) });
        var scroll = new ScrollViewer { Content = table, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        var total = new TextBlock { Text = $"{UiText.Get("Итого")}: {Calculator.Number(report.Total)} {UiText.Get("руб.")}",
            FontSize = 20, HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetRow(total, 2);
        root.Children.Add(total);
        Content = root;
        App.Preferences.Apply(this);
        root.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PanelSurfaceBrush"];
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1000, 720));
        save.Click += async (_, _) =>
        {
            save.IsEnabled = false;
            try
            {
                var picker = new FileSavePicker
                {
                    SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                    SuggestedFileName = ReportFileName.Create(UiText.Get("Сводная таблица"), report.ProjectName)
                };
                picker.FileTypeChoices.Add("Excel", [".xlsx"]);
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
                var file = await picker.PickSaveFileAsync();
                if (file is null) return;
                await Task.Run(() => ReportDocumentService.SaveXlsx(report, file.Path));
                NotificationCenter.Publish(UiText.Get("Готово"), UiText.Get("Файл сохранён"));
            }
            catch (Exception error)
            {
                await new ContentDialog { XamlRoot = root.XamlRoot, Title = UiText.Get("Сводная таблица"),
                    Content = error.Message, CloseButtonText = UiText.Get("Понятно"), RequestedTheme = root.ActualTheme }.ShowAsync();
            }
            finally { save.IsEnabled = true; }
        };
    }
}
