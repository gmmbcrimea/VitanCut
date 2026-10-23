using VitanCut.WinUI.Models;
using System.Text.Json;

namespace VitanCut.WinUI.Services;

/// <summary>
/// Owns project mutations and keeps database persistence out of WinUI event handlers.
/// </summary>
public sealed class ProjectService(AppState state)
{
    public Project? Find(string id) => state.Database.Projects.FirstOrDefault(project => project.Id == id);

    public int UpdateCatalogPrices(Project project, IReadOnlyList<ProjectDetailPricing.CatalogChange> offered)
    {
        project = Resolve(project);
        var current = ProjectDetailPricing.CatalogChanges(state, project);
        if (!current.SequenceEqual(offered))
            throw new InvalidOperationException("Цены проекта или каталога изменились. Откройте проект заново.");
        if (current.Count == 0) return 0;
        state.CommitMutation(() =>
        {
            foreach (var change in current)
            {
                var product = project.Products.Single(item => item.Id == change.ProductId);
                var detail = product.Details.Single(item => item.Id == change.DetailId);
                var material = state.FindMaterial(detail.Type, detail.MaterialId)
                    ?? throw new InvalidOperationException("Материал не найден");
                ProjectDetailPricing.UseCatalogPrice(detail, material);
            }
            Touch(project);
        });
        return current.Count;
    }

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
        state.CommitMutation(() =>
        {
            foreach (var product in project.Products) ProjectDetailPricing.Capture(state, product);
            state.Database.Projects.Add(project);
            EnsureCounterparty(project.Counterparty);
        });
    }

    public void UpdateProject(Project project, string name, string counterparty, string address)
    {
        project = Resolve(project);
        state.CommitMutation(() =>
        {
            project.Name = name;
            project.Counterparty = counterparty;
            project.Address = address;
            Touch(project);
            EnsureCounterparty(counterparty);
        });
    }

    public void Delete(Project project)
    {
        project = Resolve(project);
        state.CommitMutation(() => state.Database.Projects.Remove(project));
    }

    public Product AddCatalogProduct(Project project, string productId)
    {
        project = Resolve(project);
        var template = state.Database.Catalog.GetValueOrDefault(project.Counterparty)?
            .FirstOrDefault(product => product.Id == productId)
            ?? throw new InvalidOperationException("Изделие каталога уже удалено или изменено в облаке.");
        var copy = JsonSerializer.Deserialize<Product>(JsonSerializer.Serialize(template))
            ?? throw new InvalidDataException("Не удалось скопировать изделие.");
        copy.Qty = 1;
        ProjectDetailPricing.Clear(copy);
        SaveProduct(project, null, copy);
        return copy;
    }

    public void SaveProduct(Project project, Product? previous, Product saved)
    {
        project = Resolve(project);
        ProjectDetailPricing.Capture(state, saved);
        state.CommitMutation(() =>
        {
            if (previous is null)
            {
                saved.Id = Ids.NewId();
                project.Products.Add(saved);
            }
            else
            {
                var index = project.Products.FindIndex(product => product.Id == previous.Id);
                if (index < 0) throw new InvalidOperationException("Изделие проекта уже удалено или изменено в облаке.");
                saved.Id = project.Products[index].Id;
                project.Products[index] = saved;
            }
            Touch(project);
        });
    }

    public void SaveCutLayout(Project project, IEnumerable<CutLayoutOverride> layout)
    {
        project = Resolve(project);
        var savedLayout = layout.Select(item => new CutLayoutOverride
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
        state.CommitMutation(() => { project.CutLayout = savedLayout; Touch(project); });
    }
    public void SaveCutPlans(Project project, IReadOnlyList<SavedCutPlan> plans, string activeId, string expectedSignature, CutReportScope scope = CutReportScope.All)
    {
        project = Resolve(project);
        var baseline = CuttingService.BuildCutReport(state, project, false, scope);
        if (CutPlanService.Signature(baseline) != expectedSignature)
            throw new InvalidOperationException("Размеры, материалы или настройки раскроя изменились. Откройте расчёт заново.");
        foreach (var plan in plans.Where(p => p.InputSignature == expectedSignature))
            if (CutPlanService.Restore(baseline, plan) is null) throw new InvalidOperationException("Раскладка содержит пересечения или неверные размеры.");
        if (scope == CutReportScope.Additional)
        {
            var savedPlans = plans.Select(CutPlanService.Copy).ToList();
            state.CommitMutation(() =>
            {
                project.AdditionalCutPlans = savedPlans;
                project.ActiveAdditionalCutPlanId = activeId;
            });
            return;
        }
        var savedMainPlans = plans.Select(CutPlanService.Copy).ToList();
        state.CommitMutation(() =>
        {
            project.CutPlans = savedMainPlans;
            project.ActiveCutPlanId = activeId;
        });
    }
    public void DeleteProduct(Project project, Product product)
    {
        project = Resolve(project);
        state.CommitMutation(() =>
        {
            var removed = project.Products.RemoveAll(item => item.Id == product.Id);
            if (removed == 0) throw new InvalidOperationException("Изделие проекта уже удалено.");
            Touch(project);
        });
    }

    public void AddPayroll(Project project, Payroll payroll)
    {
        project = Resolve(project);
        state.CommitMutation(() => { project.Payrolls.Add(payroll); Touch(project); });
    }

    public void UpdatePayroll(Project project, int index, string mode, double amount, double rate, string note)
    {
        project = Resolve(project);
        if (index < 0 || index >= project.Payrolls.Count) return;
        state.CommitMutation(() =>
        {
            var payroll = project.Payrolls[index];
            payroll.Mode = mode;
            payroll.Amount = amount;
            payroll.Rate = rate;
            payroll.Note = note;
            Touch(project);
        });
    }

    public void DeletePayroll(Project project, int index)
    {
        project = Resolve(project);
        if (index < 0 || index >= project.Payrolls.Count) return;
        state.CommitMutation(() => { project.Payrolls.RemoveAt(index); Touch(project); });
    }

    private void EnsureCounterparty(string counterparty)
    {
        if (string.IsNullOrWhiteSpace(counterparty) ||
            state.Database.Counterparties.Contains(counterparty, StringComparer.CurrentCultureIgnoreCase)) return;
        state.Database.Counterparties.Add(counterparty);
    }

    private Project Resolve(Project project) => Find(project.Id)
        ?? throw new InvalidOperationException("Проект уже удалён.");

    private static void Touch(Project project) => project.UpdatedAt = DateTimeOffset.Now;
}
