using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Keeps project-page rendering out of the application's window and does not own mutations.
/// </summary>
public sealed class ProjectPageController
{
    private readonly AppState _state;
    private readonly ListView _projectsList;
    private readonly UIElement _noProjectsPanel;
    private readonly UIElement _emptyStatePanel;
    private readonly UIElement _projectPanel;
    private readonly TextBlock _title;
    private readonly TextBlock _customer;
    private readonly TextBlock _address;
    private readonly TextBlock _area;
    private readonly TextBlock _materials;
    private readonly TextBlock _payroll;
    private readonly TextBlock _total;
    private readonly ListView _products;
    private readonly ListView _payrolls;
    private readonly UIElement _noProductsPanel;
    private readonly UIElement _noPayrollsPanel;

    public ProjectPageController(
        AppState state,
        ListView projectsList,
        UIElement noProjectsPanel,
        UIElement emptyStatePanel,
        UIElement projectPanel,
        TextBlock title,
        TextBlock customer,
        TextBlock address,
        TextBlock area,
        TextBlock materials,
        TextBlock payroll,
        TextBlock total,
        ListView products,
        ListView payrolls,
        UIElement noProductsPanel,
        UIElement noPayrollsPanel)
    {
        _state = state;
        _projectsList = projectsList;
        _noProjectsPanel = noProjectsPanel;
        _emptyStatePanel = emptyStatePanel;
        _projectPanel = projectPanel;
        _title = title;
        _customer = customer;
        _address = address;
        _area = area;
        _materials = materials;
        _payroll = payroll;
        _total = total;
        _products = products;
        _payrolls = payrolls;
        _noProductsPanel = noProductsPanel;
        _noPayrollsPanel = noPayrollsPanel;
    }

    public Project? SelectedProject => _projectsList.SelectedItem as Project;

    public void BindProjectList(IReadOnlyList<Project> projects, Project? selected, bool selectFirst)
    {
        _projectsList.ItemsSource = null;
        _projectsList.ItemsSource = projects;
        _noProjectsPanel.Visibility = projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _projectsList.Visibility = projects.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        if (selected is not null && projects.Contains(selected))
            _projectsList.SelectedItem = selected;
        else
            _projectsList.SelectedIndex = selectFirst && projects.Count > 0 ? 0 : -1;
    }

    public void Render(Project? project, Func<Payroll, string> payrollLabel)
    {
        _emptyStatePanel.Visibility = project is null ? Visibility.Visible : Visibility.Collapsed;
        _projectPanel.Visibility = project is null ? Visibility.Collapsed : Visibility.Visible;
        if (project is null) return;

        _title.Text = project.Name;
        _customer.Text = project.Counterparty;
        _address.Text = project.Address;

        var totals = Calculator.Project(_state, project);
        _area.Text = Calculator.Number(totals.Area);
        _materials.Text = Calculator.Number(totals.ProductCost, 0);
        _payroll.Text = Calculator.Number(totals.Payroll, 0);
        _total.Text = Calculator.Number(totals.Cost, 0);

        _products.ItemsSource = null;
        _products.ItemsSource = project.Products;
        _payrolls.ItemsSource = null;
        _payrolls.ItemsSource = project.Payrolls.Select(payrollLabel).ToList();
        _noProductsPanel.Visibility = project.Products.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _products.Visibility = project.Products.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _noPayrollsPanel.Visibility = project.Payrolls.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _payrolls.Visibility = project.Payrolls.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}