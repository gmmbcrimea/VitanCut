using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public sealed record MaterialSummaryRow(string Category, string MaterialId, string Name, string Unit,
    double Quantity, double UnitPrice, double Cost);
public sealed record MaterialSummaryReport(string ProjectName, List<MaterialSummaryRow> Rows, List<string> Issues)
{
    public double Total => Rows.Sum(row => row.Cost);
}

public static class MaterialSummaryService
{
    public static MaterialSummaryReport Build(AppState state, Project project)
    {
        var rows = new List<MaterialSummaryRow>();
        var issues = new List<string>();
        foreach (var product in project.Products)
        {
            var count = product.Qty <= 0 ? 1 : product.Qty;
            foreach (var detail in product.Details)
            {
                var calculated = Calculator.Detail(state, product, detail);
                var material = calculated.Material;
                if (material is null || !string.IsNullOrWhiteSpace(calculated.Error))
                    issues.Add($"{product.Name} / {detail.Name}: " + (material is null ? UiText.Get("Материал не найден") : calculated.Error));
                rows.Add(new(detail.Type, detail.MaterialId, material?.Name ?? detail.MaterialId,
                    material?.Unit ?? "pc", calculated.Usage * count,
                    material is null ? 0 : ProjectDetailPricing.UnitPrice(detail, material), calculated.Cost * count));
            }
        }
        return new(project.Name, rows.GroupBy(row => (row.Category, row.MaterialId, row.Unit, row.UnitPrice))
            .Select(group => group.First() with { Quantity = group.Sum(row => row.Quantity), Cost = group.Sum(row => row.Cost) })
            .OrderBy(row => row.Category).ThenBy(row => row.Name).ToList(), issues);
    }

    public static string UnitLabel(string unit) => UiText.Get(unit switch { "m2" => "м²", "lm" => "пог. м", _ => "шт." });
}
