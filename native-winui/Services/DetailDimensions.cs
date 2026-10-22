using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

internal static class DetailDimensions
{
    public static bool Swap(Detail detail, Material? material)
    {
        if (material?.Unit != "m2" || detail.IsGeneratedEdgeBand) return false;
        (detail.LengthExpr, detail.WidthExpr) = (detail.WidthExpr, detail.LengthExpr);
        return true;
    }
}
