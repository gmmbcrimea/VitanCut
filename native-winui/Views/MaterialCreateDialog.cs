using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace VitanCut.WinUI.Views;

internal static class MaterialCreateDialog
{
    public static async Task<MaterialChoice?> ShowAsync(Window owner, XamlRoot root, string category)
    {
        var fields = new StackPanel { Spacing = 12 };
        var name = new TextBox { Header = UiText.Get("Название") };
        var unit = new ComboBox { Header = UiText.Get("Единица"), HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (code, label) in new[] { ("m2", "м²"), ("lm", "пог. м"), ("pc", "шт.") })
            unit.Items.Add(new ComboBoxItem { Tag = code, Content = UiText.Get(label) });
        var priceMode = new ComboBox { Header = UiText.Get("Цена указана"), HorizontalAlignment = HorizontalAlignment.Stretch };
        var cost = new NumberBox { Header = UiText.Get("Цена за единицу"), Minimum = 0, Value = 0 };
        var roll = new NumberBox { Header = UiText.Get("Длина бухты, м"), Minimum = 0, Value = 0 };
        var length = new NumberBox { Header = UiText.Get("Длина листа"), Minimum = 0, Value = 0 };
        var width = new NumberBox { Header = UiText.Get("Ширина листа"), Minimum = 0, Value = 0 };
        var direction = new CheckBox { Content = UiText.Get("Есть направление текстуры") };
        var edging = new CheckBox { Content = UiText.Get("Разрешить кромление деталей") };
        var image = new Image { Width = 100, Height = 100, Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
        var upload = new Button { Content = UiText.Get("Загрузить текстуру") };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"] };
        string texture = "";
        string Unit() => (unit.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "pc";
        string Mode() => (priceMode.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "unit";
        void UpdatePrice()
        {
            roll.Visibility = Unit() == "lm" && Mode() == "roll" ? Visibility.Visible : Visibility.Collapsed;
            cost.Header = UiText.Get(Mode() == "sheet" ? "Цена за лист" : Mode() == "roll" ? "Цена за бухту" :
                Unit() == "m2" ? "Цена за м²" : Unit() == "lm" ? "Цена за погонный метр" : "Цена за единицу");
        }
        unit.SelectionChanged += (_, _) =>
        {
            var sheet = Unit() == "m2";
            length.Visibility = width.Visibility = direction.Visibility = sheet ? Visibility.Visible : Visibility.Collapsed;
            priceMode.Items.Clear();
            priceMode.Items.Add(new ComboBoxItem { Tag = "unit", Content = UiText.Get(sheet ? "За квадратный метр" : Unit() == "lm" ? "За погонный метр" : "За штуку") });
            if (sheet) priceMode.Items.Add(new ComboBoxItem { Tag = "sheet", Content = UiText.Get("За лист") });
            if (Unit() == "lm") priceMode.Items.Add(new ComboBoxItem { Tag = "roll", Content = UiText.Get("За бухту") });
            priceMode.Visibility = Unit() == "pc" ? Visibility.Collapsed : Visibility.Visible;
            priceMode.SelectedIndex = 0;
            UpdatePrice();
        };
        priceMode.SelectionChanged += (_, _) => UpdatePrice();
        void ShowError(string message) { error.Text = UiText.Get(message); error.Visibility = Visibility.Visible; }
        upload.Click += async (_, _) =>
        {
            upload.IsEnabled = false;
            try
            {
                var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
                foreach (var extension in new[] { ".png", ".jpg", ".jpeg" }) picker.FileTypeFilter.Add(extension);
                InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));
                var file = await picker.PickSingleFileAsync();
                if (file is null) return;
                var resized = await ImageResizeService.FromFileAsync(file.Path, 100, 100, cropToBounds: true);
                if (resized is null) { ShowError("Выберите корректное изображение PNG или JPEG."); return; }
                texture = resized;
                await ImagePreviewService.SetVisibleAsync(image, texture);
            }
            catch (Exception exception) { ShowError(exception.Message); }
            finally { upload.IsEnabled = true; }
        };
        foreach (var control in new UIElement[] { name, unit, priceMode, cost, roll, length, width, direction, edging,
            new TextBlock { Text = UiText.Get("Текстура материала"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }, image, upload, error })
            fields.Children.Add(control);
        unit.SelectedIndex = category == "Кромка" ? 1 : category is "ЛДСП" or "Пиломатериалы" ? 0 : 2;
        var dialog = new ContentDialog
        {
            XamlRoot = root, RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            Title = $"{UiText.Get("Добавить материал")} · {category}",
            Content = new ScrollViewer { Content = fields, MaxHeight = Math.Max(180, root.Size.Height - 230),
                Width = Math.Max(240, Math.Min(440, root.Size.Width - 100)), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            PrimaryButtonText = UiText.Get("Сохранить"), CloseButtonText = UiText.Get("Отмена"),
            DefaultButton = ContentDialogButton.Primary
        };
        MaterialChoice? result = null;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try
            {
                var material = new Material
                {
                    Name = name.Text.Trim(), Unit = Unit(), Cost = NumberBoxInput.Read(cost),
                    PriceMode = Mode(), RollLength = Unit() == "lm" ? NumberBoxInput.Read(roll) : 0,
                    SheetLength = Unit() == "m2" ? NumberBoxInput.Read(length) : 0,
                    SheetWidth = Unit() == "m2" ? NumberBoxInput.Read(width) : 0,
                    TextureDirection = Unit() == "m2" && direction.IsChecked == true,
                    EdgeBandingEnabled = edging.IsChecked == true, Texture = texture
                };
                App.MaterialLibrary.Add(category, material);
                result = new MaterialChoice(category, material);
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
            { args.Cancel = true; ShowError(exception.Message); }
        };
        await dialog.ShowAsync();
        return result;
    }
}
