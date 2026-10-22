using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public static class MaterialCatalogOrdering
{
    public static IReadOnlyList<MaterialChoice> Sort(IEnumerable<MaterialChoice> choices, string? mode)
    {
        var sorted = mode switch
        {
            "name-desc" => choices.OrderByDescending(choice => choice.Material.Name, StringComparer.CurrentCultureIgnoreCase),
            "date" => choices.OrderByDescending(choice => choice.Material.CreatedAt)
                .ThenBy(choice => choice.Material.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => choices.OrderBy(choice => choice.Material.Name, StringComparer.CurrentCultureIgnoreCase)
        };
        return sorted.GroupBy(choice => choice.Material.Group.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(group => group.Key.Length == 0)
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .SelectMany(group => group).ToList();
    }
}
