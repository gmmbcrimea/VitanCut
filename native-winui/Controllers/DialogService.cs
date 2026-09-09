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
    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot() ?? throw new InvalidOperationException("Окно ещё не готово для диалога."),
            RequestedTheme = (xamlRoot()?.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default,
            Title = title,
            Content = message,
            CloseButtonText = "Понятно"
        };
        await dialog.ShowAsync();
    }

    public async Task<string?> PromptTextAsync(string title, string header, string value)
    {
        var input = new TextBox { Header = header, Text = value, MinWidth = 320 };
        var dialog = CreateDialog(title, input, "Сохранить", ContentDialogButton.Primary);
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text.Trim() : null;
    }

    public async Task<bool> ConfirmDeleteAsync(string title, string message)
    {
        var dialog = CreateDialog(title, message, "Удалить", ContentDialogButton.Close);
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task<ProjectEdit?> PromptProjectAsync(Project project)
    {
        var nameBox = new TextBox { Header = "Название проекта", Text = project.Name };
        var customerBox = new ComboBox
        {
            Header = "Заказчик",
            IsEditable = true,
            ItemsSource = catalog.GetCounterparties(),
            Text = project.Counterparty
        };
        var addressBox = new TextBox { Header = "Адрес", Text = project.Address };
        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        panel.Children.Add(nameBox);
        panel.Children.Add(customerBox);
        panel.Children.Add(addressBox);

        var dialog = CreateDialog("Изменить проект", panel, "Сохранить", ContentDialogButton.Primary);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        var customer = string.IsNullOrWhiteSpace(customerBox.Text) ? "Без заказчика" : customerBox.Text.Trim();
        return new ProjectEdit(NameOr(nameBox.Text, project.Name), customer, addressBox.Text.Trim());
    }

    public async Task<PayrollEdit?> PromptPayrollAsync(Payroll payroll)
    {
        var modeBox = new ComboBox { Header = "Тип начисления", SelectedIndex = payroll.Mode == "area" ? 1 : 0 };
        modeBox.Items.Add(new ComboBoxItem { Content = "Фиксированная сумма", Tag = "fixed" });
        modeBox.Items.Add(new ComboBoxItem { Content = "За м²", Tag = "area" });
        var amountBox = new NumberBox { Header = "Сумма", Value = payroll.Mode == "fixed" ? payroll.Amount : payroll.Rate };
        var noteBox = new TextBox { Header = "Комментарий", Text = payroll.Note };
        var panel = new StackPanel { Spacing = 12, MinWidth = 360 };
        panel.Children.Add(modeBox);
        panel.Children.Add(amountBox);
        panel.Children.Add(noteBox);

        var dialog = CreateDialog("Изменить ЗП", panel, "Сохранить", ContentDialogButton.Primary);
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
        Title = title,
        Content = content,
        PrimaryButtonText = primaryButtonText,
        CloseButtonText = "Отмена",
        DefaultButton = defaultButton
    };

    private static string NameOr(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
