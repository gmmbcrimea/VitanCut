using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

internal static class DetailDimensions
{
    public static bool Swap(Product product, Detail detail, Material? material)
    {
        if (material?.Unit != "m2" || detail.IsGeneratedEdgeBand || !product.Details.Contains(detail)) return false;
        (detail.LengthExpr, detail.WidthExpr) = (detail.WidthExpr, detail.LengthExpr);
        // References follow the original dimension, including dependencies within the swapped row.
        foreach (var dependent in product.Details)
        {
            dependent.LengthExpr = FormulaEvaluator.SwapDimensionReferences(dependent.LengthExpr, detail.Id);
            dependent.WidthExpr = FormulaEvaluator.SwapDimensionReferences(dependent.WidthExpr, detail.Id);
        }
        return true;
    }
}
