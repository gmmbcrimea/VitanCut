using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

/// <summary>
/// Owns project mutations and keeps database persistence out of WinUI event handlers.
/// </summary>
public sealed class ProjectService(AppState state)
{
    public IReadOnlyList<Project> GetOrdered(string sortKey) => sortKey switch
    {
        "created" => state.Database.Projects.OrderByDescending(project => project.CreatedAt).ToList(),
        "name" => state.Database.Projects.OrderBy(project => project.Name).ToList(),
        "counterparty" => state.Database.Projects
            .OrderBy(project => project.Counterparty)
            .ThenBy(project => project.Name)
            .ToList(),
        _ => state.Database.Projects.OrderByDescending(project => project.UpdatedAt).ToList()
    };

    public void Add(Project project)
    {
        state.Database.Projects.Add(project);
        EnsureCounterparty(project.Counterparty);
        state.Save();
    }

    public void UpdateProject(Project project, string name, string counterparty, string address)
    {
        project.Name = name;
        project.Counterparty = counterparty;
        project.Address = address;
        Touch(project);
        EnsureCounterparty(counterparty);
        state.Save();
    }

    public void Delete(Project project)
    {
        state.Database.Projects.Remove(project);
        state.Save();
    }

    public void SaveProduct(Project project, Product? previous, Product saved)
    {
        if (previous is null)
        {
            saved.Id = Ids.NewId();
            project.Products.Add(saved);
        }
        else
        {
            var index = project.Products.IndexOf(previous);
            if (index >= 0) project.Products[index] = saved;
        }

        Touch(project);
        state.Save();
    }

    public void SaveCutLayout(Project project, IEnumerable<CutLayoutOverride> layout)
    {
        project.CutLayout = layout.Select(item => new CutLayoutOverride
        {
            InstanceId = item.InstanceId,
            BaseLength = item.BaseLength,
            BaseWidth = item.BaseWidth,
            X = item.X,
            Y = item.Y,
            Length = item.Length,
            Width = item.Width,
            Rotated = item.Rotated
        }).ToList();
        Touch(project);
        state.Save();
    }
    public void SaveCutPlans(Project project, IReadOnlyList<SavedCutPlan> plans, string activeId, string expectedSignature, CutReportScope scope = CutReportScope.All)
    {
        if (!state.Database.Projects.Contains(project)) throw new InvalidOperationException("Проект уже удалён.");
        var baseline = CuttingService.BuildCutReport(state, project, false, scope);
        if (CutPlanService.Signature(baseline) != expectedSignature)
            throw new InvalidOperationException("Размеры, материалы или настройки раскроя изменились. Откройте расчёт заново.");
        foreach (var plan in plans.Where(p => p.InputSignature == expectedSignature))
            if (CutPlanService.Restore(baseline, plan) is null) throw new InvalidOperationException("Раскладка содержит пересечения или неверные размеры.");
        if (scope == CutReportScope.Additional)
        {
            var old = project.AdditionalCutPlans;
            var oldActive = project.ActiveAdditionalCutPlanId;
            project.AdditionalCutPlans = plans.Select(CutPlanService.Copy).ToList();
            project.ActiveAdditionalCutPlanId = activeId;
            try { state.Save(); }
            catch { project.AdditionalCutPlans = old; project.ActiveAdditionalCutPlanId = oldActive; throw; }
            return;
        }
        var mainOld = project.CutPlans;
        var mainOldActive = project.ActiveCutPlanId;
        project.CutPlans = plans.Select(CutPlanService.Copy).ToList();
        project.ActiveCutPlanId = activeId;
        try { state.Save(); }
        catch { project.CutPlans = mainOld; project.ActiveCutPlanId = mainOldActive; throw; }
    }
    public void DeleteProduct(Project project, Product product)
    {
        project.Products.Remove(product);
        Touch(project);
        state.Save();
    }

    public void AddPayroll(Project project, Payroll payroll)
    {
        project.Payrolls.Add(payroll);
        Touch(project);
        state.Save();
    }

    public void UpdatePayroll(Project project, int index, string mode, double amount, double rate, string note)
    {
        if (index < 0 || index >= project.Payrolls.Count) return;
        var payroll = project.Payrolls[index];
        payroll.Mode = mode;
        payroll.Amount = amount;
        payroll.Rate = rate;
        payroll.Note = note;
        Touch(project);
        state.Save();
    }

    public void DeletePayroll(Project project, int index)
    {
        if (index < 0 || index >= project.Payrolls.Count) return;
        project.Payrolls.RemoveAt(index);
        Touch(project);
        state.Save();
    }

    private void EnsureCounterparty(string counterparty)
    {
        if (string.IsNullOrWhiteSpace(counterparty) ||
            state.Database.Counterparties.Contains(counterparty, StringComparer.CurrentCultureIgnoreCase)) return;
        state.Database.Counterparties.Add(counterparty);
    }

    private static void Touch(Project project) => project.UpdatedAt = DateTimeOffset.Now;
}
