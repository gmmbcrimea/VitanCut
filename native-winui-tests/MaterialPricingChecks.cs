using System.Text.Json;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class MaterialPricingChecks
{
    public static void Run(Action<bool, string> check, string output)
    {
        PriceModeConversions(check);
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
        var summary = MaterialSummaryService.Build(state, project);
        check(summary.Rows.Count == 3 && summary.Total == 2160 && summary.Issues.Count == 0,
            "Material summary aggregates generated banding and manual details without payroll");
        check(summary.Rows.Single(row => row.MaterialId == sheet.Id).Quantity == 3 &&
            summary.Rows.Single(row => row.MaterialId == sheet.Id).UnitPrice == 500 &&
            summary.Rows.Single(row => row.MaterialId == roll.Id).Quantity == 15 &&
            summary.Rows.Single(row => row.MaterialId == piece.Id).Quantity == 12,
            "Material summary includes product quantities and normalized sheet/roll prices");
        var repeated = MaterialSummaryService.Build(state, new Project { Products = [product, product] });
        check(repeated.Rows.Count == 3 && repeated.Total == 4320, "Material summary merges materials across multiple products");
        var summaryPath = Path.Combine(output, "material-summary.xlsx");
        ReportDocumentService.SaveXlsx(summary, summaryPath);
        using (var workbook = new ClosedXML.Excel.XLWorkbook(summaryPath))
        {
            var worksheet = workbook.Worksheet(1);
            check(worksheet.Cell(7, 5).GetDouble() == 2160 && worksheet.Cell(4, 3).DataType == ClosedXML.Excel.XLDataType.Number,
                "Summary Excel export preserves numeric quantities and total");
        }
        var missing = MaterialSummaryService.Build(state, new Project { Products = [new Product
            { Details = [new Detail { Type = "Missing", MaterialId = "missing" }] }] });
        check(missing.Issues.Count == 1, "Summary reports missing materials instead of silently omitting them");
        check(MaterialSummaryService.Build(state, new Project()).Rows.Count == 0, "Empty project summary is supported");
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
        // These checks exercise live catalog pricing, not a project's frozen prices.
        ProjectDetailPricing.Clear(product);
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
    private static void PriceModeConversions(Action<bool, string> check)
    {
        var sheet = new Material { Unit = "m2", SheetLength = 2000, SheetWidth = 2000, WasteAllowancePercent = 15 };
        var total = MaterialPricing.ConvertPrice(sheet, "unit", "sheet", 100);
        check(total == 400, "Two-meter by two-meter sheet converts 100 per square meter into 400 per sheet");
        check(MaterialPricing.ConvertPrice(sheet, "sheet", "unit", total) == 100,
            "Sheet price converts back to the original square-meter price");
        sheet.SheetWidth = 1000;
        check(MaterialPricing.ConvertPrice(sheet, "unit", "sheet", 100) == 200,
            "Two-square-meter sheet converts 100 per square meter into 200 per sheet");
        check(sheet.WasteAllowancePercent == 15, "Price-mode conversion leaves the waste allowance unchanged");
        var roll = new Material { Unit = "lm", RollLength = 50 };
        check(MaterialPricing.ConvertPrice(roll, "unit", "roll", 25) == 1250 &&
            MaterialPricing.ConvertPrice(roll, "roll", "unit", 1250) == 25,
            "Meter and roll prices convert in both directions using roll length");
        foreach (var material in new[] { sheet, roll })
        {
            var package = material.Unit == "m2" ? "sheet" : "roll";
            var amount = 123.456789;
            for (var i = 0; i < 100; i++)
                amount = MaterialPricing.ConvertPrice(material, package, "unit", MaterialPricing.ConvertPrice(material, "unit", package, amount));
            check(Math.Abs(amount - 123.456789) < .00000001, "Repeated price-mode switches preserve fractional prices without rounding drift");
            check(MaterialPricing.ConvertPrice(material, "unit", package, 0) == 0, "Zero price converts without introducing a charge");
        }
        foreach (var invalid in new[] { 0d, -1d, double.NaN, double.PositiveInfinity })
        {
            sheet.SheetWidth = invalid;
            roll.RollLength = invalid;
            var sheetRejected = false;
            var rollRejected = false;
            try { MaterialPricing.ConvertPrice(sheet, "unit", "sheet", 100); } catch (InvalidOperationException) { sheetRejected = true; }
            try { MaterialPricing.ConvertPrice(roll, "roll", "unit", 100); } catch (InvalidOperationException) { rollRejected = true; }
            check(sheetRejected && rollRejected, "Missing or invalid dimensions cannot silently reinterpret an unchanged price");
        }
        sheet.SheetWidth = 2000;
        var overflowRejected = false;
        try { MaterialPricing.ConvertPrice(sheet, "unit", "sheet", double.MaxValue); } catch (InvalidOperationException) { overflowRejected = true; }
        check(overflowRejected, "Price-mode conversion rejects numeric overflow");
        check(MaterialPricing.ConvertPrice(new Material { Unit = "pc" }, "unit", "unit", 50) == 50,
            "Piece prices and unchanged price modes remain untouched");
    }
}
