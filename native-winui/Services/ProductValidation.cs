using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public static class ProductValidation
{
    public static string? Error(AppState state, Product product)
    {
        if (string.IsNullOrWhiteSpace(product.Name)) return "Укажите название изделия.";
        if (new[] { product.Length, product.Depth, product.Height }.Any(value => !double.IsFinite(value) || value <= 0))
            return "Габариты изделия должны быть положительными числами.";
        if (!PositiveInteger(product.Qty)) return "Количество изделий должно быть целым положительным числом.";
        if (product.HasLegs && (!double.IsFinite(product.LegHeight) || product.LegHeight <= 0)) return "Укажите высоту ножки.";
        if (product.FixedSizes.Any(size => !double.IsFinite(size.Value) || size.Value < 0)) return "Проверьте значения фиксированных размеров.";
        var names = product.FixedSizes.Select(size => FormulaText.NormalizeWhitespace(size.Name)).Where(name => name.Length > 0).ToList();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count) return "Названия фиксированных размеров должны быть уникальными.";
        foreach (var detail in product.Details)
        {
            if (!PositiveInteger(detail.Qty)) return $"{detail.Name}: количество должно быть целым положительным числом.";
            var calc = Calculator.Detail(state, product, detail);
            if (calc.Material is null) return $"{detail.Name}: выберите материал.";
            if (calc.Error.Length > 0) return $"{detail.Name}: {calc.Error}";
            if ((calc.Material.Unit is "m2" or "lm") && calc.Length <= 0 || calc.Material.Unit == "m2" && calc.Width <= 0)
                return $"{detail.Name}: размер должен быть больше нуля.";
        }
        return null;
    }

    private static bool PositiveInteger(double value) => double.IsFinite(value) && value > 0 && value == Math.Floor(value);
}
