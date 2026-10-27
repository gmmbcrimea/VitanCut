using Microsoft.UI.Xaml;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Coordinates project mutations, dialogs, and product editor windows.
/// </summary>
public sealed class ProjectCommandsController(
    AppState state,
    ProjectService projects,
    DialogService dialogs,
    ProductWindowCoordinator productWindows)
{
    public void OpenCreateProject(Action<Project> onCreated)
    {
        var window = new Views.CreateProjectWindow(state.Database.Counterparties);
        window.ProjectCreated += project =>
        {
            projects.Add(project);
            onCreated(project);
        };
        window.Activate();
    }

    public async Task<bool> EditProjectAsync(Project project)
    {
        var edit = await dialogs.PromptProjectAsync(project);
        if (edit is null) return false;

        projects.UpdateProject(project, edit.Value.Name, edit.Value.Counterparty, edit.Value.Address);
        return true;
    }

    public async Task<bool> DeleteProjectAsync(Project project)
    {
        if (!await dialogs.ConfirmDeleteAsync(UiText.Get("Удалить проект?"),project.Name)) return false;
        projects.Delete(project);
        return true;
    }

    public void OpenProduct(Project project, Product? product, Action onSaved) =>
        productWindows.OpenProjectProduct(project, product, onSaved);

    public async Task<bool> DeleteProductAsync(Project project, Product product)
    {
        if (!await dialogs.ConfirmDeleteAsync(UiText.Get("Удалить изделие?"),product.Name)) return false;
        projects.DeleteProduct(project, product);
        return true;
    }

    public async Task<bool> AddPayrollAsync(Project project)
    {
        var draft = new Payroll();
        var edit = await dialogs.PromptPayrollAsync(draft, creating: true);
        if (edit is null) return false;
        draft.Mode = edit.Value.Mode;
        draft.Amount = edit.Value.Amount;
        draft.Rate = edit.Value.Rate;
        draft.Note = edit.Value.Note;
        projects.AddPayroll(project, draft);
        return true;
    }

    public async Task<bool> EditPayrollAsync(Project project, int index)
    {
        if (index < 0 || index >= project.Payrolls.Count) return false;

        var payroll = project.Payrolls[index];
        var edit = await dialogs.PromptPayrollAsync(payroll);
        if (edit is null) return false;

        projects.UpdatePayroll(project, index, edit.Value.Mode, edit.Value.Amount, edit.Value.Rate, edit.Value.Note);
        return true;
    }

    public async Task<bool> DeletePayrollAsync(Project project, int index)
    {
        if (index < 0 || index >= project.Payrolls.Count) return false;
        if (!await dialogs.ConfirmDeleteAsync(UiText.Get("Удалить строку ЗП?"),project.Payrolls[index].Note)) return false;

        projects.DeletePayroll(project, index);
        return true;
    }
}
