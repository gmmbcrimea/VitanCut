using System.Globalization;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public static class EdgeBandingService
{
    public static bool IsEdgeMaterial(MaterialChoice choice) =>
        string.Equals(choice.Type, "Кромка", StringComparison.CurrentCultureIgnoreCase) && choice.Material.Unit == "lm";

    public static bool IsEnabled(AppState state, Detail detail) => !detail.IsGeneratedEdgeBand &&
        state.FindMaterial(detail.Type, detail.MaterialId) is { Unit: "m2", EdgeBandingEnabled: true };

    public static void SetSide(Detail detail, string side, MaterialChoice? edge)
    {
        if (side is not ("top" or "bottom" or "left" or "right")) throw new ArgumentException("Неизвестная грань.", nameof(side));
        if (edge is not null && edge.Material.Unit != "lm") throw new InvalidOperationException("Кромка должна измеряться в погонных метрах.");
        detail.EdgeBands.RemoveAll(item => item.Side == side);
        if (edge is not null) detail.EdgeBands.Add(new DetailEdgeBand { Side = side, Type = edge.Type, MaterialId = edge.Material.Id });
    }

    public static FormulaResult Length(AppState state, Product product, string materialId)
    {
        var total = 0d;
        foreach (var source in product.Details.Where(detail => IsEnabled(state, detail)))
        {
            var edges = source.EdgeBands.Where(edge => edge.MaterialId == materialId).DistinctBy(edge => edge.Side).ToList();
            foreach (var width in new[] { false, true })
            {
                var count = edges.Count(edge => width ? edge.Side is "left" or "right" : edge.Side is "top" or "bottom");
                if (count == 0) continue;
                var dimension = FormulaEvaluator.Evaluate(product, source, width);
                if (!dimension.IsValid) return dimension;
                var quantity = source.Qty <= 0 ? 1 : source.Qty;
                total += dimension.Value * count * quantity;
            }
        }
        return double.IsFinite(total) ? new(total) : new(0, "Слишком большая длина кромки.");
    }

    public static void ReplaceMaterial(Detail detail, MaterialChoice edge)
    {
        if (edge.Material.Unit != "lm") throw new InvalidOperationException("Кромка должна измеряться в погонных метрах.");
        foreach (var assignment in detail.EdgeBands)
        {
            assignment.Type = edge.Type;
            assignment.MaterialId = edge.Material.Id;
        }
    }

    public static bool ReplaceGeneratedMaterial(AppState state, Product product, Detail generated, MaterialChoice replacement)
    {
        if (!generated.IsGeneratedEdgeBand || !product.Details.Contains(generated))
            throw new InvalidOperationException("Выберите автоматически сформированную строку кромки.");
        if (!IsEdgeMaterial(replacement) || state.FindMaterial(replacement.Type, replacement.Material.Id)?.Unit != "lm")
            throw new InvalidOperationException("Выберите материал категории «Кромка» в погонных метрах.");
        var assignments = product.Details.SelectMany(detail => detail.EdgeBands)
            .Where(edge => edge.MaterialId == generated.MaterialId && edge.Type == generated.Type).ToList();
        if (assignments.Count == 0 || generated.MaterialId == replacement.Material.Id && generated.Type == replacement.Type) return false;
        foreach (var assignment in assignments)
        {
            assignment.Type = replacement.Type;
            assignment.MaterialId = replacement.Material.Id;
        }
        Refresh(state, product);
        return true;
    }

    public static void Refresh(AppState state, Product product)
    {
        var edges = product.Details.Where(detail => IsEnabled(state, detail)).SelectMany(detail => detail.EdgeBands)
            .Where(edge => state.FindMaterial(edge.Type, edge.MaterialId)?.Unit == "lm")
            .DistinctBy(edge => edge.MaterialId).ToList();
        var ids = edges.Select(edge => edge.MaterialId).ToHashSet();
        product.Details.RemoveAll(detail => detail.IsGeneratedEdgeBand && !ids.Contains(detail.MaterialId));
        foreach (var edge in edges)
        {
            var generated = product.Details.FirstOrDefault(detail => detail.IsGeneratedEdgeBand && detail.MaterialId == edge.MaterialId);
            if (generated is null)
            {
                generated = new Detail { IsGeneratedEdgeBand = true, AllowRotation = false };
                product.Details.Add(generated);
            }
            generated.Name = state.FindMaterial(edge.Type, edge.MaterialId)!.Name;
            generated.Type = edge.Type;
            generated.MaterialId = edge.MaterialId;
            generated.Qty = 1;
            generated.LengthExpr = Length(state, product, edge.MaterialId).Value.ToString("R", CultureInfo.InvariantCulture);
            generated.WidthExpr = "0";
        }
    }

    public static void RemoveMaterial(Product product, string materialId)
    {
        foreach (var detail in product.Details) detail.EdgeBands.RemoveAll(edge => edge.MaterialId == materialId);
    }
}
