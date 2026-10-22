using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public static class MaterialPricing
{
    public static string Error(Material material)
    {
        if (!double.IsFinite(material.Cost) || material.Cost < 0) return "Цена должна быть конечным неотрицательным числом.";
        if (material.PriceMode == "unit") return "";
        if (material.PriceMode == "sheet" && material.Unit == "m2")
            return Positive(material.SheetLength) && Positive(material.SheetWidth) && Positive(SheetArea(material)) &&
                double.IsFinite(material.Cost / SheetArea(material))
                ? "" : "Для цены за лист укажите положительные длину и ширину листа.";
        if (material.PriceMode == "roll" && material.Unit == "lm")
            return Positive(material.RollLength) && double.IsFinite(material.Cost / material.RollLength)
                ? "" : "Укажите положительную длину бухты в метрах.";
        return "Способ задания цены не соответствует единице измерения материала.";
    }

    public static double UnitCost(Material material)
    {
        if (Error(material).Length > 0) return 0;
        return material.Cost / PriceQuantity(material);
    }

    public static double PriceQuantity(Material material) => material.PriceMode switch
    {
        "sheet" => SheetArea(material),
        "roll" => material.RollLength,
        _ => 1
    };

    private static double SheetArea(Material material) => (material.SheetLength / 1000d) * (material.SheetWidth / 1000d);
    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
}
