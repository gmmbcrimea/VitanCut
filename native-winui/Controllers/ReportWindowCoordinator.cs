using VitanCut.WinUI.Services;
using System.Text.Json;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Builds and opens project reports as owned task windows.
/// </summary>
public sealed class ReportWindowCoordinator(OwnedWindowActivator windows, AppState state, ProjectService projects)
{
    public async Task OpenCutReportAsync(Models.Project project, CutReportScope scope = CutReportScope.MainDsp)
    {
        List<Models.SavedCutPlan> PlansFor(Models.Project value) =>
            scope == CutReportScope.Additional ? value.AdditionalCutPlans : value.CutPlans;
        string ActiveFor(Models.Project value) =>
            scope == CutReportScope.Additional ? value.ActiveAdditionalCutPlanId : value.ActiveCutPlanId;

        var expectedPlans = JsonSerializer.Serialize(PlansFor(project));
        var snapshot = new AppState();
        snapshot.Database.Materials = JsonSerializer.Deserialize<Dictionary<string, List<Models.Material>>>(JsonSerializer.Serialize(state.Database.Materials))!;
        snapshot.Database.Preferences = JsonSerializer.Deserialize<Models.AppPreferences>(JsonSerializer.Serialize(state.Database.Preferences))!;
        var copy = JsonSerializer.Deserialize<Models.Project>(JsonSerializer.Serialize(project))!;
        var (baseline, plans, signature, activeId) = await Task.Run(() =>
        {
            var baseline = CuttingService.BuildCutReport(snapshot, copy, applySavedLayout: false, scope);
            var signature = CutPlanService.Signature(baseline);
            var generated = CutOptimization.Calculate(baseline);
            var automatic = generated.Select((a, i) => CutPlanService.Capture(a.Report, signature, $"{i + 1}. {a.Name}", a.Algorithm, true)).ToList();
            var saved = PlansFor(copy).Where(p => !p.Automatic && p.Name != "Рабочие правки").Select(CutPlanService.Copy).ToList();
            if (scope == CutReportScope.MainDsp && saved.Count == 0 && copy.CutLayout.Count > 0)
                saved.Add(CutPlanService.Capture(CuttingService.BuildCutReport(snapshot, copy, scope: scope), signature, "Прежняя ручная карта", "Пользовательский"));
            var plans = automatic.Concat(saved).ToList();
            var active = saved.FirstOrDefault(p => p.Id == ActiveFor(copy) && CutPlanService.Restore(baseline, p) is not null)?.Id ?? automatic[0].Id;
            return (baseline, plans, signature, active);
        });
        void SavePlans(IReadOnlyList<Models.SavedCutPlan> saved, string active)
        {
            if (JsonSerializer.Serialize(PlansFor(project)) != expectedPlans)
                throw new InvalidOperationException("Варианты раскроя изменены в другом окне. Откройте расчёт заново, чтобы не потерять изменения.");
            projects.SaveCutPlans(project, saved, active, signature, scope);
            expectedPlans = JsonSerializer.Serialize(PlansFor(project));
        }
        SavePlans(plans, activeId);
        var hasAdditional = scope == CutReportScope.MainDsp && await Task.Run(() =>
            CuttingService.BuildCutReport(snapshot, copy, applySavedLayout: false, scope: CutReportScope.Additional).Groups.Count > 0);
        windows.Activate(new Views.ReportWindow(baseline, plans, activeId, SavePlans,
            hasAdditional ? () => OpenCutReportAsync(project, CutReportScope.Additional) : null,
            isAdditional: scope == CutReportScope.Additional));
    }

    public void OpenDetailingReport(Models.Project project) =>
        windows.Activate(new Views.ReportWindow(CuttingService.BuildDetailingReport(state, project)));
}
