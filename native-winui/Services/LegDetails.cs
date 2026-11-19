using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

internal static class LegDetails
{
    internal const string Group = "Ножки";
    internal const string Name = "Ножка";
    internal const string HeightExpression = "высота ножки";

    internal static bool IsMaterial(MaterialChoice choice) =>
        string.Equals(choice.Material.Group.Trim(), Group, StringComparison.CurrentCultureIgnoreCase);

    internal static void RecognizeLegacy(AppState state, Product product)
    {
        if (!product.HasLegs) return;
        foreach (var detail in product.Details.Where(detail => !detail.IsGeneratedEdgeBand && detail.Name == Name &&
                     detail.LengthExpr == HeightExpression && detail.WidthExpr == "0"))
            if (state.FindMaterial(detail.Type, detail.MaterialId) is { Unit: "pc" } material &&
                (IsMaterial(new MaterialChoice(detail.Type, material)) ||
                 material.Name.Contains("ножка", StringComparison.CurrentCultureIgnoreCase)))
                detail.IsGeneratedLeg = true;
    }

    internal static Detail Create(MaterialChoice material) => new()
    {
        IsGeneratedLeg = true, Name = Name, Qty = 4, Type = material.Type,
        MaterialId = material.Material.Id, LengthExpr = HeightExpression, WidthExpr = "0", AllowRotation = false
    };
}
