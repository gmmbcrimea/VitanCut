using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class GeneratedEdgeReplacementChecks
{
    public static void Run(Action<bool, string> check, string output)
    {
        var state = new AppState();
        var path = Path.Combine(output, "generated-edge-replacement.json");
        state.Load(path);
        var sheet = state.Database.Materials["ЛДСП"][0];
        sheet.EdgeBandingEnabled = true;
        var edges = Enumerable.Range(0, 4).Select(index => new MaterialChoice("Кромка",
            new Material { Name = "Replacement edge " + index, Unit = "lm", Cost = 10 + index })).ToArray();
        state.Database.Materials["Кромка"] = edges.Select(edge => edge.Material).ToList();
        var first = new Detail { Type = "ЛДСП", MaterialId = sheet.Id, LengthExpr = "600", WidthExpr = "400", Qty = 2 };
        var second = new Detail { Type = "ЛДСП", MaterialId = sheet.Id, LengthExpr = "900", WidthExpr = "500" };
        var manual = new Detail { Type = edges[0].Type, MaterialId = edges[0].Material.Id, LengthExpr = "100" };
        var product = new Product { Details = [first, second, manual] };
        var otherDetail = new Detail { Type = "ЛДСП", MaterialId = sheet.Id, LengthExpr = "100", WidthExpr = "100" };
        EdgeBandingService.SetSide(otherDetail, "top", edges[0]);
        var otherProduct = new Product { Details = [otherDetail] };
        state.Database.Projects.Add(new Project { Products = [product, otherProduct] });
        EdgeBandingService.SetSide(first, "top", edges[0]);
        EdgeBandingService.SetSide(first, "left", edges[1]);
        EdgeBandingService.SetSide(second, "bottom", edges[0]);
        EdgeBandingService.SetSide(second, "right", edges[2]);
        EdgeBandingService.Refresh(state, product);
        var oldRow = product.Details.Single(detail => detail.IsGeneratedEdgeBand && detail.MaterialId == edges[0].Material.Id);
        var targetRow = product.Details.Single(detail => detail.IsGeneratedEdgeBand && detail.MaterialId == edges[1].Material.Id);
        ProjectDetailPricing.Set(targetRow, edges[1].Material, 25);
        var beforeSides = product.Details.SelectMany(detail => detail.EdgeBands).Select(edge => edge.Side).ToArray();
        check(EdgeBandingService.ReplaceGeneratedMaterial(state, product, oldRow, edges[1]),
            "An automatic edge row can replace its material for the current product");
        check(first.EdgeBands.Single(edge => edge.Side == "top").MaterialId == edges[1].Material.Id &&
            second.EdgeBands.Single(edge => edge.Side == "bottom").MaterialId == edges[1].Material.Id &&
            product.Details.SelectMany(detail => detail.EdgeBands).Select(edge => edge.Side).SequenceEqual(beforeSides),
            "Automatic edge replacement updates all matching detail assignments and preserves every selected side");
        check(!product.Details.Contains(oldRow) && product.Details.Count(detail => detail.IsGeneratedEdgeBand) == 2 &&
            product.Details.Contains(targetRow) && EdgeBandingService.Length(state, product, edges[1].Material.Id).Value == 2900,
            "Replacing with an already used edge merges lengths into its existing automatic row without duplicates");
        check(Math.Abs(Calculator.Detail(state, product, targetRow).Cost - 72.5) < .000001 && targetRow.ProjectPriceIsManual == true,
            "A merged edge row retains the existing project-specific price of the destination material");
        check(second.EdgeBands.Single(edge => edge.Side == "right").MaterialId == edges[2].Material.Id &&
            product.Details.Contains(manual) && manual.MaterialId == edges[0].Material.Id &&
            otherDetail.EdgeBands.Single().MaterialId == edges[0].Material.Id,
            "Automatic replacement leaves other edges, manual rows and other products unchanged");
        check(EdgeBandingService.ReplaceGeneratedMaterial(state, product, targetRow, edges[3]),
            "Automatic edge replacement also supports a material not previously used by this product");
        var newRow = product.Details.Single(detail => detail.IsGeneratedEdgeBand && detail.MaterialId == edges[3].Material.Id);
        ProjectDetailPricing.Capture(state, product);
        check(newRow.Name == edges[3].Material.Name && Calculator.Detail(state, product, newRow).Length == 2900 &&
            Math.Abs(Calculator.Detail(state, product, newRow).Cost - 2.9 * edges[3].Material.Cost) < .000001,
            "New automatic edge row immediately uses the replacement name, length and price rather than the old override");
        check(!EdgeBandingService.ReplaceGeneratedMaterial(state, product, newRow, edges[3]),
            "Selecting the current automatic edge material is a no-op");
        foreach (var invalid in new[] { new MaterialChoice("Фурнитура", new Material { Unit = "lm" }),
            new MaterialChoice("Кромка", new Material { Unit = "pc" }),
            new MaterialChoice("Кромка", new Material { Unit = "lm" }) })
        {
            var before = state.ExportJson();
            var rejected = false;
            try { EdgeBandingService.ReplaceGeneratedMaterial(state, product, newRow, invalid); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected && state.ExportJson() == before, "Invalid or missing replacement material is rejected before changing edge assignments");
        }
        check(EdgeBandingService.IsEdgeMaterial(edges[0]) &&
            !EdgeBandingService.IsEdgeMaterial(new MaterialChoice("Фурнитура", edges[0].Material)),
            "Automatic edge material picker accepts only linear materials in the edge category");
        state.Save();
        var restored = new AppState();
        restored.Load(path);
        var restoredProduct = restored.Database.Projects[0].Products[0];
        check(EdgeBandingService.Length(restored, restoredProduct, edges[3].Material.Id).Value == 2900 &&
            restoredProduct.Details.Count(detail => detail.IsGeneratedEdgeBand && detail.MaterialId == edges[3].Material.Id) == 1,
            "Automatic edge replacement and its merged assignments survive restart");
    }
}
