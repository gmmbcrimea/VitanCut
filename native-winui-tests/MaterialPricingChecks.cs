using System.Text.Json;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class MaterialPricingChecks
{
    public static void Run(Action<bool, string> check, string output)
    {
        var path = Path.Combine(output, "material-pricing.json");
        var state = new AppState();
        state.Load(path);
        var library = new MaterialService(state);
        var sheet = new Material { Name = "Sheet price test", Unit = "m2", Cost = 2000, PriceMode = "sheet",
            SheetLength = 2000, SheetWidth = 2000, EdgeBandingEnabled = true, Group = "Brand", CreatedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z") };
        var roll = new Material { Name = "Roll price test", Unit = "lm", Cost = 10000, PriceMode = "roll", RollLength = 500 };
        var piece = new Material { Name = "Piece price test", Unit = "pc", Cost = 30 };
        library.Add("Price sheets", sheet);
        library.Add("Кромка", roll);
        library.Add("Price pieces", piece);
        var panel = new Detail { Name = "Panel", Type = "Price sheets", MaterialId = sheet.Id, LengthExpr = "1000", WidthExpr = "500", Qty = 2 };
        var linear = new Detail { Name = "Linear", Type = "Кромка", MaterialId = roll.Id, LengthExpr = "1000", Qty = 2 };
        var hardware = new Detail { Name = "Hardware", Type = "Price pieces", MaterialId = piece.Id, Qty = 4 };
        var product = new Product { Details = [panel, linear, hardware], Qty = 3 };
        var project = new Project { Products = [product] };
        state.Database.Projects.Add(project);
        check(MaterialPricing.UnitCost(sheet) == 500 && Calculator.Detail(state, product, panel).Cost == 500,
            "Sheet price is normalized by sheet area in square meters");
        check(MaterialPricing.UnitCost(roll) == 20 && Calculator.Detail(state, product, linear).Usage == 2 && Calculator.Detail(state, product, linear).Cost == 40,
            "Roll price uses roll length in meters and detail length in millimeters");
        check(Calculator.Detail(state, product, hardware).Cost == 120, "Piece pricing is unchanged");
        EdgeBandingService.SetSide(panel, "top", new MaterialChoice("Кромка", roll));
        EdgeBandingService.SetSide(panel, "left", new MaterialChoice("Кромка", roll));
        EdgeBandingService.Refresh(state, product);
        var band = product.Details.Single(detail => detail.IsGeneratedEdgeBand);
        check(Calculator.Detail(state, product, band).Cost == 60, "Generated edge banding uses price per roll");
        check(Calculator.Product(state, product).Cost == 2160 && Calculator.Project(state, project).Cost == 2160,
            "Normalized sheet and roll prices flow into product and project totals with quantities");
        var detailing = CuttingService.BuildDetailingReport(state, project);
        check(detailing.Products[0].Rows.Sum(row => row.Cost) == 2160, "Detailing report uses the same normalized prices");
        sheet.PriceMode = "unit";
        sheet.Cost = 500;
        roll.PriceMode = "unit";
        roll.Cost = 20;
        check(Calculator.Product(state, product).Cost == 2160, "Equivalent unit prices produce exactly the same totals");
        sheet.PriceMode = "sheet";
        sheet.Cost = 2000;
        roll.PriceMode = "roll";
        roll.Cost = 10000;
        library.Save(new MaterialChoice("Price sheets", sheet), "Price sheets");
        state.Save();
        var reloaded = new AppState();
        reloaded.Load(path);
        var savedSheet = reloaded.Database.Materials["Price sheets"].Single();
        var savedRoll = reloaded.Database.Materials["Кромка"].Single(material => material.Id == roll.Id);
        check(savedSheet.PriceMode == "sheet" && savedSheet.Group == "Brand" && savedSheet.CreatedAt == sheet.CreatedAt &&
            savedRoll.PriceMode == "roll" && savedRoll.RollLength == 500 &&
            Calculator.Product(reloaded, reloaded.Database.Projects[0].Products[0]).Cost == 2160,
            "Price modes, roll length, group and creation date survive save and restart");
        var legacy = JsonSerializer.Deserialize<Material>("{\"Unit\":\"lm\",\"Cost\":25}")!;
        check(legacy.PriceMode == "unit" && MaterialPricing.UnitCost(legacy) == 25, "Legacy materials keep unit pricing without migration");
        foreach (var value in new[] { 0d, -1, double.NaN, double.PositiveInfinity, double.Epsilon })
        {
            roll.RollLength = value;
            var rejected = false;
            try { library.Validate(roll, roll); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && Calculator.Detail(state, product, linear).Error.Length > 0,
                "Invalid roll length is rejected and diagnosed in calculations: " + value);
        }
        roll.RollLength = 500;
        sheet.SheetWidth = 0;
        check(MaterialPricing.Error(sheet).Length > 0 && Calculator.Detail(state, product, panel).Error.Length > 0,
            "Sheet pricing requires nonzero sheet dimensions");
        sheet.SheetWidth = 2000;
        piece.PriceMode = "roll";
        check(MaterialPricing.Error(piece).Length > 0, "Unsupported pricing mode cannot be applied to piece materials");
        roll.RollLength = 250;
        check(MaterialPricing.UnitCost(roll) == 40, "Changing roll length immediately updates effective meter price");
    }
}
