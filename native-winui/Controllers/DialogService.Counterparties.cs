using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace VitanCut.WinUI.Controllers;

public sealed partial class DialogService
{
    public async Task<CounterpartyEdit?> PromptCounterpartyAsync(string? previousName)
    {
        var name = new TextBox { Header = UiText.Get("Название заказчика"), Text = previousName ?? "", MinWidth = 280 };
        var logo = catalog.LogoFor(previousName);
        var image = new Image { Width = 32, Height = 32, Stretch = Stretch.UniformToFill };
        _ = ImagePreviewService.SetVisibleAsync(image, logo);
        var upload = new Button { Content = UiText.Get("Загрузить логотип") };
        var remove = new Button { Content = UiText.Get("Удалить логотип"), IsEnabled = logo.Length > 0 };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(image);
        actions.Children.Add(upload);
        actions.Children.Add(remove);
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(name);
        content.Children.Add(actions);
        var error = new TextBlock { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["UiDangerBrush"] };
        content.Children.Add(error);
        var dialog = CreateDialog(previousName is null ? "Добавить заказчика" : "Изменить заказчика", content,
            UiText.Get("Сохранить"), ContentDialogButton.Primary);
        var active = true;
        var busy = false;
        upload.Click += async (_, _) =>
        {
            if (!active || busy) return;
            busy = true;
            upload.IsEnabled = remove.IsEnabled = false;
            try
            {
                var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
                picker.FileTypeFilter.Add("*");
                InitializeWithWindow.Initialize(picker, windowHandle());
                var file = await picker.PickSingleFileAsync();
                if (!active || file is null) return;
                var thumbnail = await ImageResizeService.FromFileAsync(file.Path, 32, 32, cropToBounds: true);
                if (!active) return;
                if (thumbnail is null) throw new InvalidOperationException(UiText.Get("Не удалось загрузить логотип."));
                logo = thumbnail;
                await ImagePreviewService.SetVisibleAsync(image, logo);
                error.Visibility = Visibility.Collapsed;
            }
            catch (Exception failure) when (failure is InvalidOperationException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                if (active) { error.Text = UiText.Get("Не удалось загрузить логотип."); error.Visibility = Visibility.Visible; }
            }
            finally
            {
                busy = false;
                if (active) { upload.IsEnabled = true; remove.IsEnabled = logo.Length > 0; }
            }
        };
        remove.Click += (_, _) =>
        {
            logo = "";
            remove.IsEnabled = false;
            _ = ImagePreviewService.SetVisibleAsync(image, "");
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!busy && name.Text.Trim().Length > 0 &&
                !catalog.GetCounterparties().Any(item => string.Equals(item, name.Text.Trim(), StringComparison.CurrentCultureIgnoreCase) &&
                    !string.Equals(item, previousName, StringComparison.CurrentCultureIgnoreCase))) return;
            args.Cancel = true;
            error.Text = UiText.Get(name.Text.Trim().Length == 0 ? "Название заказчика" : "Заказчик уже существует");
            error.Visibility = Visibility.Visible;
        };
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary
                ? new CounterpartyEdit(name.Text.Trim(), logo) : null;
        }
        finally { active = false; }
    }
}
