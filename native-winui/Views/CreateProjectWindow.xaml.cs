using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;
using Windows.Graphics;

namespace VitanCut.WinUI.Views;

public sealed partial class CreateProjectWindow : Window
{
    public event Action<Project>? ProjectCreated;

    public CreateProjectWindow(IEnumerable<string> customers)
    {
        InitializeComponent();
        Title = "Расчёт мебели - Создать проект";
        App.Preferences.Apply(this);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        foreach (var customer in customers.Where(item => !string.IsNullOrWhiteSpace(item)).Distinct().OrderBy(item => item))
            CustomerBox.Items.Add(customer);
        CustomerBox.Items.Add(new ComboBoxItem { Content = "Создать нового заказчика", Tag = "new-customer" });
        CustomerBox.SelectedIndex = CustomerBox.Items.Count > 1 ? 0 : -1;
        Root.Loaded += (_, _) =>
        {
            ResizeToContent();
            CreateButton.Focus(FocusState.Programmatic);
        };
    }

    private void ResizeToContent()
    {
        Root.Measure(new Windows.Foundation.Size(460, double.PositiveInfinity));
        var height = Math.Max(440, (int)Math.Ceiling(Root.DesiredSize.Height) + 96);
        AppWindow.Resize(new SizeInt32(520, height));
    }

    private void CustomerChanged(object sender, SelectionChangedEventArgs e)
    {
        var createNew = CustomerBox.SelectedItem is ComboBoxItem { Tag: "new-customer" };
        NewCustomerBox.Visibility = createNew ? Visibility.Visible : Visibility.Collapsed;
        if (createNew)
            NewCustomerBox.DispatcherQueue.TryEnqueue(() => NewCustomerBox.Focus(FocusState.Programmatic));
    }

    private void CancelClick(object sender, RoutedEventArgs e) => Close();

    private void CreateClick(object sender, RoutedEventArgs e)
    {
        var createNew = CustomerBox.SelectedItem is ComboBoxItem { Tag: "new-customer" };
        var selected = CustomerBox.SelectedItem is ComboBoxItem item ? item.Content?.ToString() : CustomerBox.SelectedItem?.ToString();
        var customer = createNew ? NewCustomerBox.Text.Trim() : selected ?? "";
        var now = DateTimeOffset.Now;
        ProjectCreated?.Invoke(new Project
        {
            Name = Empty(ProjectNameBox.Text, "Новый проект"),
            Counterparty = Empty(customer, "Частный заказчик"),
            Address = AddressBox.Text.Trim(),
            CreatedAt = now,
            UpdatedAt = now
        });
        Close();
    }

    private static string Empty(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}