using System.Globalization;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public static class Calculator
{
    public static DetailCalc Detail(AppState state, Product product, Detail detail)
    {
        var material = state.FindMaterial(detail.Type, detail.MaterialId);
        var unit = material?.Unit ?? "pc";
        var lengthResult = unit is "m2" or "lm" ? FormulaEvaluator.Evaluate(product, detail) : new FormulaResult(0);
        var widthResult = unit == "m2" ? FormulaEvaluator.Evaluate(product, detail, width: true) : new FormulaResult(0);
        var length = lengthResult.Value;
        var width = widthResult.Value;
        var qty = detail.Qty <= 0 ? 1 : detail.Qty;
        var rawArea = length * width * qty / 1_000_000d;
        var isProjectArea = unit == "m2" && Contains(detail.Type, material?.Name, "дсп", "двп", "лдсп");
        var area = isProjectArea ? rawArea : 0;
        var usage = unit switch
        {
            "m2" => rawArea,
            "lm" => length * qty / 1000d,
            _ => qty
        };
        var cost = usage * (material?.Cost ?? 0);
        return new DetailCalc(length, width, qty, area, rawArea, usage, cost, material,
            string.Join("; ", new[] { lengthResult.Error, widthResult.Error }.Where(error => error.Length > 0).Distinct()));
    }

    public static Totals Product(AppState state, Product product)
    {
        var qty = product.Qty <= 0 ? 1 : product.Qty;
        var area = 0d;
        var raw = 0d;
        var cost = 0d;
        foreach (var detail in product.Details)
        {
            var calc = Detail(state, product, detail);
            area += calc.Area * qty;
            raw += calc.RawArea * qty;
            cost += calc.Cost * qty;
        }
        return new Totals(area, raw, cost, 0, cost);
    }

    public static Totals Project(AppState state, Project project)
    {
        var area = 0d;
        var raw = 0d;
        var dspCost = 0d;
        var allMaterialCost = 0d;
        foreach (var product in project.Products)
        {
            var productQty = product.Qty <= 0 ? 1 : product.Qty;
            foreach (var detail in product.Details)
            {
                var calc = Detail(state, product, detail);
                area += calc.Area * productQty;
                raw += calc.RawArea * productQty;
                var cost = calc.Cost * productQty;
                allMaterialCost += cost;
                if (calc.Material?.Name.Contains("ДСП", StringComparison.OrdinalIgnoreCase) == true) dspCost += cost;
            }
        }

        var payroll = project.Payrolls.Sum(row => row.Mode == "fixed" ? row.Amount : row.Rate * area);
        return new Totals(area, raw, dspCost, payroll, allMaterialCost + payroll);
    }

    public static string Money(double value) => $"{Number(value, 2)} руб.";
    private static readonly CultureInfo NumberCulture = CreateNumberCulture();
    private static CultureInfo CreateNumberCulture()
    {
        var culture = (CultureInfo)CultureInfo.GetCultureInfo("ru-RU").Clone();
        culture.NumberFormat.NumberGroupSeparator = " ";
        return CultureInfo.ReadOnly(culture);
    }
    public static string Number(double value, int digits = 2)
    {
        var decimals = digits <= 0 ? "" : "." + new string('#', digits);
        return value.ToString("#,##0" + decimals, NumberCulture);
    }
    private static bool Contains(string type, string? materialName, params string[] tokens)
    {
        var text = $"{type} {materialName}".ToLowerInvariant();
        return tokens.Any(text.Contains);
    }

}
