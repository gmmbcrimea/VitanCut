using System.Text.Json;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class WasteAllowanceChecks
{
    private static bool Near(double a, double b) => Math.Abs(a - b) < .000001;

    public static void Run(Action<bool, string> check, string output)
    {
        var state = new AppState();
        var path = Path.Combine(output, "waste-allowances.json");
        state.Load(path);
        var sheet = new Material { Name = "Waste sheet", Unit = "m2", PriceMode = "sheet", Cost = 2000,
            SheetLength = 2000, SheetWidth = 1000, WasteAllowancePercent = 10, EdgeBandingEnabled = true };
        var roll = new Material { Name = "Waste roll", Unit = "lm", PriceMode = "roll", Cost = 1000,
            RollLength = 100, WasteAllowancePercent = 20 };
        var library = new MaterialService(state);
        library.Add("ЛДСП", sheet);
        library.Add("Кромка", roll);
        check(Near(MaterialPricing.UnitCost(sheet), 1100) && MaterialPricing.BaseUnitCost(sheet) == 1000 && sheet.Cost == 2000,
            "Sheet allowance increases effective square-meter price without rewriting base sheet price");
        check(Near(MaterialPricing.UnitCost(roll), 12) && MaterialPricing.BaseUnitCost(roll) == 10,
            "Roll allowance is applied once after converting to price per meter");
        check(Near(MaterialPricing.UnitCost(new Material { Unit = "m2", Cost = 1000, WasteAllowancePercent = 12.5 }), 1125) &&
            Near(MaterialPricing.UnitCost(new Material { Unit = "lm", Cost = 10, WasteAllowancePercent = 20 }), 12),
            "Direct square-meter and linear-meter prices support fractional allowances");
        check(MaterialPricing.UnitCost(new Material { Unit = "pc", Cost = 50, WasteAllowancePercent = 20 }) == 50,
            "Piece materials ignore waste allowance");
        check(JsonSerializer.Deserialize<Material>("{\"Unit\":\"m2\",\"Cost\":100}")!.WasteAllowancePercent == 0,
            "Older catalog materials default to zero allowance");
        var panel = new Detail { Type = "ЛДСП", MaterialId = sheet.Id, LengthExpr = "1000", WidthExpr = "1000" };
        var band = new Detail { Type = "Кромка", MaterialId = roll.Id, LengthExpr = "2000" };
        var product = new Product { Qty = 3, Details = [panel, band] };
        var project = new Project { Products = [product] };
        var projects = new ProjectService(state);
        projects.Add(project);
        check(panel.ProjectUnitPrice == 1000 && panel.ProjectWasteAllowancePercent == 10 &&
            ProjectDetailPricing.Editor(panel, sheet).Amount == 2000 && Near(Calculator.Product(state, product).Cost, 3372),
            "Project snapshots keep base price and allowance separate without doubling the percentage");
        check(Calculator.Detail(state, product, panel).Usage == 1 && Calculator.Detail(state, product, band).Usage == 2,
            "Allowance increases monetary cost only, not physical material quantity");
        sheet.WasteAllowancePercent = 50;
        state.Save();
        var changes = ProjectDetailPricing.CatalogChanges(state, project);
        check(changes.Count == 1 && changes[0].PreviousAllowance == 10 && changes[0].CurrentAllowance == 50 &&
            Near(Calculator.Product(state, product).Cost, 3372),
            "Catalog-only allowance changes are offered without silently repricing the project");
        projects.UpdateCatalogPrices(project, changes);
        check(panel.ProjectWasteAllowancePercent == 50 && Near(Calculator.Product(state, product).Cost, 4572),
            "Accepted catalog refresh updates allowance together with price");
        ProjectDetailPricing.SetEnteredPrice(panel, sheet, 1800, 5);
        check(Near(ProjectDetailPricing.UnitPrice(panel, sheet), 945) && panel.ProjectUnitPrice == 900 &&
            Near(Calculator.Product(state, product).Cost, 2907) && sheet.Cost == 2000 && sheet.WasteAllowancePercent == 50,
            "Project sheet-price and allowance edits recalculate costs without changing the catalog");
        check(panel.ProjectPriceIsManual == true && ProjectDetailPricing.CatalogChanges(state, project).Count == 0,
            "Project allowance edits are protected from catalog refresh");
        var before = JsonSerializer.Serialize(panel);
        foreach (var invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            var rejected = false;
            try { ProjectDetailPricing.SetEnteredPrice(panel, sheet, 1800, invalid); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && JsonSerializer.Serialize(panel) == before, "Invalid project allowance cannot partially change price or provenance");
            check(MaterialPricing.Error(new Material { Unit = "lm", Cost = 10, WasteAllowancePercent = invalid }).Length > 0,
                "Invalid catalog allowance is diagnosed");
        }
        check(MaterialPricing.Error(new Material { Unit = "m2", Cost = double.MaxValue, WasteAllowancePercent = 100 }).Length > 0,
            "Overflowing allowance-adjusted prices are rejected");
        state.Save();
        var reopened = new AppState();
        reopened.Load(path);
        var saved = reopened.Database.Projects.Single().Products.Single();
        check(Near(Calculator.Product(reopened, saved).Cost, 2907) && saved.Details.Single(row => row.MaterialId == sheet.Id).ProjectWasteAllowancePercent == 5,
            "Project allowance survives save and restart without being applied twice");
        var restored = CloudEntityMerge.Restore(CloudEntityMerge.Snapshot(reopened.Database));
        check(restored.Materials["ЛДСП"].Single(material => material.Id == sheet.Id).WasteAllowancePercent == 50 &&
            restored.Projects.Single().Products.Single().Details.Single(row => row.MaterialId == sheet.Id).ProjectWasteAllowancePercent == 5,
            "Cloud records keep catalog and project allowances independent");
        var summary = MaterialSummaryService.Build(state, project);
        check(Near(summary.Rows.Single(row => row.MaterialId == sheet.Id).UnitPrice, 945) && Near(summary.Total, 2907) &&
            Near(CuttingService.BuildDetailingReport(state, project).Summary.Cost, 2907),
            "Summary and detailing use allowance-adjusted project prices consistently");
        ReportDocumentService.SaveXlsx(summary, Path.Combine(output, "waste-summary.xlsx"));
        using (var workbook = new ClosedXML.Excel.XLWorkbook(Path.Combine(output, "waste-summary.xlsx")))
            check(workbook.Worksheet(1).RowsUsed().Any(row => row.Cell(1).GetString() == sheet.Name && Near(row.Cell(4).GetDouble(), 945)),
                "Excel exports the allowance-adjusted unit price");
        new CatalogService(state).SaveProduct("Waste", null, product);
        check(state.Database.Catalog["Waste"].Single().Details.All(detail => detail.ProjectWasteAllowancePercent == 0) && panel.ProjectWasteAllowancePercent == 5,
            "Saving a project product to catalog strips its private allowance without modifying the project");
        ProjectDetailPricing.UseCatalogPrice(panel, sheet);
        check(panel.ProjectWasteAllowancePercent == 50 && panel.ProjectPriceIsManual == false,
            "Explicit catalog reset restores catalog price and allowance");
        var edgePanel = new Detail { Type = "ЛДСП", MaterialId = sheet.Id, LengthExpr = "1000", WidthExpr = "1000" };
        var edgeProduct = new Product { Details = [edgePanel] };
        EdgeBandingService.SetSide(edgePanel, "top", new MaterialChoice("Кромка", roll));
        EdgeBandingService.SetSide(edgePanel, "left", new MaterialChoice("Кромка", roll));
        EdgeBandingService.Refresh(state, edgeProduct);
        ProjectDetailPricing.Capture(state, edgeProduct);
        var generated = edgeProduct.Details.Single(detail => detail.IsGeneratedEdgeBand);
        check(Near(Calculator.Detail(state, edgeProduct, generated).Cost, 24),
            "Automatically generated edge banding includes its material waste allowance");
    }
}
