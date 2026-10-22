using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public static class MaterialPricing
{
    public static double ConvertPrice(Material material, string fromMode, string toMode, double price)
    {
        if (!double.IsFinite(price) || price < 0)
            throw new InvalidOperationException("Цена должна быть конечным неотрицательным числом.");
        if (fromMode == toMode) return price;
        double Quantity(string mode)
        {
            var basis = new Material { Unit = material.Unit, PriceMode = mode, Cost = 0,
                SheetLength = material.SheetLength, SheetWidth = material.SheetWidth, RollLength = material.RollLength };
            if (Error(basis) is { Length: > 0 } error) throw new InvalidOperationException(error);
            return PriceQuantity(basis);
        }
        var converted = price / Quantity(fromMode) * Quantity(toMode);
        if (!double.IsFinite(converted))
            throw new InvalidOperationException("Цена должна быть конечным неотрицательным числом.");
        return converted;
    }

    public static string Error(Material material)
    {
        if (!double.IsFinite(material.Cost) || material.Cost < 0) return "Цена должна быть конечным неотрицательным числом.";
        if (!double.IsFinite(material.WasteAllowancePercent) || material.WasteAllowancePercent < 0)
            return "Допуск на обрезки должен быть конечным неотрицательным числом.";
        string error;
        if (material.PriceMode == "unit") error = "";
        else if (material.PriceMode == "sheet" && material.Unit == "m2")
            error = Positive(material.SheetLength) && Positive(material.SheetWidth) && Positive(SheetArea(material)) &&
                double.IsFinite(material.Cost / SheetArea(material))
                ? "" : "Для цены за лист укажите положительные длину и ширину листа.";
        else if (material.PriceMode == "roll" && material.Unit == "lm")
            error = Positive(material.RollLength) && double.IsFinite(material.Cost / material.RollLength)
                ? "" : "Укажите положительную длину бухты в метрах.";
        else error = "Способ задания цены не соответствует единице измерения материала.";
        if (error.Length > 0) return error;
        return double.IsFinite(material.Cost / PriceQuantity(material) * (1 + AllowancePercent(material) / 100))
            ? "" : "Цена должна быть конечным неотрицательным числом.";
    }

    public static double UnitCost(Material material)
    {
        if (Error(material).Length > 0) return 0;
        return material.Cost / PriceQuantity(material) * (1 + AllowancePercent(material) / 100);
    }

    public static double BaseUnitCost(Material material) => Error(material).Length > 0 ? 0 : material.Cost / PriceQuantity(material);
    public static double AllowancePercent(Material material) => material.Unit is "m2" or "lm" ? material.WasteAllowancePercent : 0;

    public static double PriceQuantity(Material material) => material.PriceMode switch
    {
        "sheet" => SheetArea(material),
        "roll" => material.RollLength,
        _ => 1
    };

    private static double SheetArea(Material material) => (material.SheetLength / 1000d) * (material.SheetWidth / 1000d);
    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
}
