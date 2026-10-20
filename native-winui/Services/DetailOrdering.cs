using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public static class DetailOrdering
{
    public static void SortForSave(Product product) => product.Details = product.Details
        .OrderBy(detail => Priority(detail.Type))
        .ThenBy(detail => detail.Type.Trim(), StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    private static int Priority(string type) => type.Trim().ToLowerInvariant() switch
    {
        "пиломатериалы" or "лдсп" or "дсп" or "двп" or "мдф" or "фанера" => 0,
        "фурнитура" => 1,
        "кромка" => 2,
        _ => 3
    };
}
