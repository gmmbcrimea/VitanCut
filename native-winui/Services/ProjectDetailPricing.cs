using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public static class ProjectDetailPricing
{
    public sealed record EditorPrice(string Mode, double Quantity, double Amount);
    public sealed record CatalogChange(string ProductId, string DetailId, string MaterialId, string Name, string Unit,
        double PreviousPrice, double CurrentPrice, string PreviousMode, string CurrentMode,
        double PreviousQuantity, double CurrentQuantity);

    public static IReadOnlyList<CatalogChange> CatalogChanges(AppState state, Project project)
    {
        var changes = new List<CatalogChange>();
        foreach (var product in project.Products)
        foreach (var detail in product.Details)
        {
            if (detail.ProjectPriceIsManual != false || state.FindMaterial(detail.Type, detail.MaterialId) is not { } material ||
                !HasPrice(detail, material) || MaterialPricing.Error(material).Length > 0) continue;
            var previous = UnitPrice(detail, material);
            var current = MaterialPricing.UnitCost(material);
            if (!double.IsFinite(previous)) continue;
            var quantity = MaterialPricing.PriceQuantity(material);
            if (Same(previous, current) && detail.ProjectPriceMode == material.PriceMode && Same(detail.ProjectPriceQuantity, quantity)) continue;
            changes.Add(new(product.Id, detail.Id, material.Id, material.Name, material.Unit, previous, current,
                detail.ProjectPriceMode, material.PriceMode, detail.ProjectPriceQuantity, quantity));
        }
        return changes;
    }

    private static bool Same(double left, double right) => Math.Abs(left - right) <= Math.Max(1, Math.Max(Math.Abs(left), Math.Abs(right))) * 1e-10;

    public static bool HasPrice(Detail detail, Material material) => detail.ProjectUnitPrice.HasValue &&
        detail.ProjectPriceMaterialId == material.Id && detail.ProjectPriceUnit == material.Unit;

    public static double UnitPrice(Detail detail, Material material) => HasPrice(detail, material)
        ? detail.ProjectUnitPrice!.Value : MaterialPricing.UnitCost(material);

    public static void Set(Detail detail, Material material, double price)
    {
        if (!double.IsFinite(price) || price < 0) throw new InvalidOperationException("Цена должна быть конечным неотрицательным числом.");
        var basis = Basis(detail, material);
        detail.ProjectUnitPrice = price;
        detail.ProjectPriceMaterialId = material.Id;
        detail.ProjectPriceUnit = material.Unit;
        detail.ProjectPriceMode = basis.Mode;
        detail.ProjectPriceQuantity = basis.Quantity;
        detail.ProjectPriceIsManual = true;
    }

    private static (string Mode, double Quantity) Basis(Detail detail, Material material)
    {
        if (HasPrice(detail, material) && detail.ProjectPriceMode is "unit" or "sheet" or "roll" &&
            double.IsFinite(detail.ProjectPriceQuantity) && detail.ProjectPriceQuantity > 0)
            return (detail.ProjectPriceMode, detail.ProjectPriceQuantity);
        return MaterialPricing.Error(material).Length == 0
            ? (material.PriceMode, MaterialPricing.PriceQuantity(material)) : ("unit", 1);
    }

    public static EditorPrice Editor(Detail detail, Material material)
    {
        var basis = Basis(detail, material);
        return new(basis.Mode, basis.Quantity, UnitPrice(detail, material) * basis.Quantity);
    }

    public static void SetEnteredPrice(Detail detail, Material material, double amount)
    {
        var basis = Basis(detail, material);
        var unitPrice = amount / basis.Quantity;
        if (!double.IsFinite(amount) || amount < 0 || !double.IsFinite(unitPrice))
            throw new InvalidOperationException("Цена должна быть конечным неотрицательным числом.");
        Set(detail, material, unitPrice);
    }

    public static void UseCatalogPrice(Detail detail, Material material)
    {
        if (MaterialPricing.Error(material) is { Length: > 0 } error) throw new InvalidOperationException(error);
        detail.ProjectUnitPrice = MaterialPricing.UnitCost(material);
        detail.ProjectPriceMaterialId = material.Id;
        detail.ProjectPriceUnit = material.Unit;
        detail.ProjectPriceMode = material.PriceMode;
        detail.ProjectPriceQuantity = MaterialPricing.PriceQuantity(material);
        detail.ProjectPriceIsManual = false;
    }

    public static void Capture(AppState state, Product product)
    {
        foreach (var detail in product.Details)
        {
            if (state.FindMaterial(detail.Type, detail.MaterialId) is not { } material) continue;
            if (!HasPrice(detail, material) && MaterialPricing.Error(material).Length == 0)
                UseCatalogPrice(detail, material);
            else if (HasPrice(detail, material) && string.IsNullOrEmpty(detail.ProjectPriceMode))
            {
                // Older snapshots stored a normalized price only; retain that amount.
                var basis = Basis(detail, material);
                detail.ProjectPriceMode = basis.Mode;
                detail.ProjectPriceQuantity = basis.Quantity;
            }
            // Unknown provenance in older snapshots must never erase a possible manual price.
            if (HasPrice(detail, material) && detail.ProjectPriceIsManual is null) detail.ProjectPriceIsManual = true;
        }
    }

    public static void Clear(Product product)
    {
        foreach (var detail in product.Details)
        {
            detail.ProjectUnitPrice = null;
            detail.ProjectPriceMaterialId = "";
            detail.ProjectPriceUnit = "";
            detail.ProjectPriceMode = "";
            detail.ProjectPriceQuantity = 1;
            detail.ProjectPriceIsManual = null;
        }
    }
}
