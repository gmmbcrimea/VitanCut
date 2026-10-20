using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class EdgeBandingChecks
{
    public static void Run(Action<bool, string> check, string output)
    {
        var path = Path.Combine(output, "edge-banding", "database.json");
        var state = new AppState();
        state.Load(path);
        var sheet = state.Database.Materials["ЛДСП"][0];
        sheet.EdgeBandingEnabled = true;
        var edge = new MaterialChoice("Кромка", state.Database.Materials["Кромка"][0]);
        var otherEdge = new MaterialChoice("Кромка", state.Database.Materials["Кромка"][1]);
        var source = new Detail { Name = "Полка", MaterialId = sheet.Id, Qty = 2, LengthExpr = "длина изделия", WidthExpr = "400" };
        var manual = new Detail { Name = "Ручная кромка", Type = edge.Type, MaterialId = edge.Material.Id, LengthExpr = "100" };
        var product = new Product { Name = "Проверка кромления", Length = 600, Qty = 3, Details = [source, manual] };
        var project = new Project { Name = "Проверка кромления", Products = [product] };
        state.Database.Projects.Add(project);
        EdgeBandingService.SetSide(source, "top", edge);
        EdgeBandingService.SetSide(source, "bottom", edge);
        EdgeBandingService.SetSide(source, "left", edge);
        EdgeBandingService.Refresh(state, product);
        var generated = product.Details.Single(detail => detail.IsGeneratedEdgeBand);
        var id = generated.Id;
        check(Calculator.Detail(state, product, generated).Length == 3200, "Edge bands sum selected lengths and widths for every source detail");
        check(Calculator.Detail(state, product, generated).Usage == 3.2 && generated.Qty == 1 &&
            generated.Name == edge.Material.Name && generated.Type == edge.Type, "Generated edge material has its own name, type and linear usage");
        EdgeBandingService.SetSide(source, "top", edge);
        EdgeBandingService.Refresh(state, product);
        check(generated.Id == id && product.Details.Count(detail => detail.IsGeneratedEdgeBand) == 1 &&
            Calculator.Detail(state, product, generated).Length == 3200, "Repeated side selection neither duplicates usage nor changes generated detail identity");
        product.Length = 800;
        check(Calculator.Detail(state, product, generated).Length == 4000, "Edge band calculation follows source formulas without a stale stored length");
        source.Qty = 4;
        check(Calculator.Detail(state, product, generated).Length == 8000, "Edge band calculation follows source quantity");
        EdgeBandingService.SetSide(source, "bottom", null);
        check(Calculator.Detail(state, product, generated).Length == 4800, "Unselecting an edge subtracts its length");
        EdgeBandingService.SetSide(source, "left", otherEdge);
        EdgeBandingService.Refresh(state, product);
        check(product.Details.Count(detail => detail.IsGeneratedEdgeBand) == 2 &&
            Calculator.Detail(state, product, generated).Length == 3200 &&
            EdgeBandingService.Length(state, product, otherEdge.Material.Id).Value == 1600, "Different edge materials keep independent totals and replacing an edge subtracts its old material");
        var expectedCost = (Calculator.Detail(state, product, source).Cost + 3.2 * edge.Material.Cost +
            1.6 * otherEdge.Material.Cost + Calculator.Detail(state, product, manual).Cost) * product.Qty;
        check(Math.Abs(Calculator.Product(state, product).Cost - expectedCost) < .000001,
            "Edge cost includes source quantity and product quantity exactly once");
        var report = CuttingService.BuildDetailingReport(state, project);
        check(report.Products[0].Rows.Any(row => row.DetailName == generated.Name && row.Qty == 3), "Detailing includes generated band rows and product quantity");
        state.Save();
        File.Copy(path, Path.Combine(output, "edge-banding-ui.json"));
        var library = new MaterialService(state);
        var moved = library.Save(edge, "Кромка перенесена");
        check(source.EdgeBands.Single(item => item.Side == "top").Type == moved.Type && generated.Type == moved.Type &&
            Calculator.Detail(state, product, generated).Length == 3200, "Moving edge material to another category preserves assignments and cost inputs");
        sheet.EdgeBandingEnabled = false;
        library.Save(new MaterialChoice("ЛДСП", sheet), "ЛДСП");
        check(!product.Details.Any(detail => detail.IsGeneratedEdgeBand) && source.EdgeBands.Count == 2 &&
            library.IsUsed(moved), "Disabling edge banding removes generated usage but preserves assignments and deletion protection");
        sheet.EdgeBandingEnabled = true;
        library.Save(new MaterialChoice("ЛДСП", sheet), "ЛДСП");
        check(product.Details.Count(detail => detail.IsGeneratedEdgeBand) == 2 && product.Details.Contains(manual),
            "Reenabling restores edge usage without removing manual band details");
        state.Save();
        var reloaded = new AppState();
        reloaded.Load(path);
        var restored = reloaded.Database.Projects[0].Products[0];
        check(restored.Details[0].EdgeBands.Count == 2 && reloaded.Database.Materials["ЛДСП"][0].EdgeBandingEnabled &&
            EdgeBandingService.Length(reloaded, restored, moved.Material.Id).Value == 3200, "Material checkbox, selected sides and generated band rows survive restart");
        new CatalogService(state).SaveProduct("Источник", null, product);
        new CatalogService(state).CopyProduct("Источник", product, "Копия");
        var copy = state.Database.Catalog["Копия"].Single();
        EdgeBandingService.SetSide(copy.Details.First(detail => detail.Name == "Полка"), "top", null);
        check(source.EdgeBands.Any(item => item.Side == "top") && copy.Id != product.Id,
            "Catalog copy owns independent edge assignments");
        EdgeBandingService.RemoveMaterial(product, moved.Material.Id);
        EdgeBandingService.Refresh(state, product);
        check(!product.Details.Any(detail => detail.IsGeneratedEdgeBand && detail.MaterialId == moved.Material.Id) && product.Details.Contains(manual),
            "Removing generated band material clears only its automatic assignments");
        product.Details.Remove(source);
        EdgeBandingService.Refresh(state, product);
        check(product.Details.SequenceEqual([manual]), "Deleting source detail removes its band usage without touching manual entries");
        check(!new Material().EdgeBandingEnabled && new Detail().EdgeBands.Count == 0, "Existing materials and details default to no edge banding");
    }
}
