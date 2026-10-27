using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Creates application dialogs from a single place and keeps them independent from MainWindow.
/// </summary>
public sealed class DialogService(Func<XamlRoot?> xamlRoot, CatalogService catalog)
{
    public async Task<bool> ConfirmLanguageRestartAsync(string language)
    {
        var root = xamlRoot() ?? throw new InvalidOperationException("Окно ещё не готово для диалога.");
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = (root.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            Title = UiText.Translate("Язык интерфейса", language),
            Content = UiText.Translate("Язык сохранён. Изменение вступит в силу после перезапуска приложения.", language),
            PrimaryButtonText = UiText.Translate("Перезапустить сейчас", language),
            CloseButtonText = UiText.Translate("Понятно", language),
            DefaultButton = ContentDialogButton.Close
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot() ?? throw new InvalidOperationException("Окно ещё не готово для диалога."),
            RequestedTheme = (xamlRoot()?.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            Title = UiText.Get(title),
            Content = UiText.Get(message),
            CloseButtonText = UiText.Get("Понятно")
        };
        await dialog.ShowAsync();
    }

    public async Task<string?> PromptTextAsync(string title, string header, string value)
    {
        var input = new TextBox { Header = header, Text = value, MinWidth = 320 };
        var dialog = CreateDialog(title,input,UiText.Get("Сохранить"),ContentDialogButton.Primary);
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text.Trim() : null;
    }

    public async Task<bool> ConfirmDeleteAsync(string title, string message)
    {
        var dialog = CreateDialog(title,message,UiText.Get("Удалить"),ContentDialogButton.Close);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<ProjectEdit?> PromptProjectAsync(Project project)
    {
        var nameBox = new TextBox { Header = UiText.Get("Название проекта"), Text = project.Name };
        var customerBox = new ComboBox
        {
            Header = UiText.Get("Заказчик"),
            IsEditable = true,
            ItemsSource = catalog.GetCounterparties(),
            Text = project.Counterparty
        };
        var addressBox = new TextBox { Header = UiText.Get("Адрес"), Text = project.Address };
        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        panel.Children.Add(nameBox);
        panel.Children.Add(customerBox);
        panel.Children.Add(addressBox);

        var dialog = CreateDialog(UiText.Get("Изменить проект"),panel,UiText.Get("Сохранить"),ContentDialogButton.Primary);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var customer = string.IsNullOrWhiteSpace(customerBox.Text) ? "Без заказчика" : customerBox.Text.Trim();
        return new ProjectEdit(NameOr(nameBox.Text, project.Name), customer, addressBox.Text.Trim());
    }

    public async Task<PayrollEdit?> PromptPayrollAsync(Payroll payroll, bool creating = false)
    {
        var modeBox = new ComboBox { Header = UiText.Get("Тип начисления"), HorizontalAlignment = HorizontalAlignment.Stretch };
        modeBox.Items.Add(new ComboBoxItem { Content = UiText.Get("Фиксированная сумма"), Tag = "fixed" });
        modeBox.Items.Add(new ComboBoxItem { Content = UiText.Get("За м²"), Tag = "area" });
        modeBox.SelectedIndex = payroll.Mode == "area" ? 1 : 0;
        var amountBox = new NumberBox { Header = UiText.Get(payroll.Mode == "area" ? "Ставка за м²" : "Сумма"),
            Minimum = 0, Value = payroll.Mode == "fixed" ? payroll.Amount : payroll.Rate };
        modeBox.SelectionChanged += (_, _) => amountBox.Header = UiText.Get(modeBox.SelectedIndex == 1 ? "Ставка за м²" : "Сумма");
        var noteBox = new TextBox { Header = UiText.Get("Комментарий"), Text = payroll.Note };
        var panel = new StackPanel { Spacing = 12, MinWidth = 360 };
        panel.Children.Add(modeBox);
        panel.Children.Add(amountBox);
        panel.Children.Add(noteBox);
        var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["UiDangerBrush"] };
        panel.Children.Add(errorText);

        var dialog = CreateDialog(UiText.Get(creating ? "Добавить ЗП" : "Изменить ЗП"),panel,UiText.Get("Сохранить"),ContentDialogButton.Primary);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (double.IsFinite(amountBox.Value) && amountBox.Value >= 0) return;
            args.Cancel = true;
            errorText.Text = UiText.Get("Укажите конечную неотрицательную сумму.");
            errorText.Visibility = Visibility.Visible;
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var mode = modeBox.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() ?? "fixed" : "fixed";
        return new PayrollEdit(
            mode,
            mode == "fixed" ? amountBox.Value : payroll.Amount,
            mode == "area" ? amountBox.Value : payroll.Rate,
            NameOr(noteBox.Text, "ЗП"));
    }

    private ContentDialog CreateDialog(string title, object content, string primaryButtonText, ContentDialogButton defaultButton) => new()
    {
        XamlRoot = xamlRoot() ?? throw new InvalidOperationException("Окно еще не готово для диалога."),
        RequestedTheme = (xamlRoot()?.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
        Title = UiText.Get(title),
        Content = content is string message ? UiText.Get(message) : content,
        PrimaryButtonText = UiText.Get(primaryButtonText),
        CloseButtonText = UiText.Get("Отмена"),
        DefaultButton = defaultButton
    };

    private static string NameOr(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
