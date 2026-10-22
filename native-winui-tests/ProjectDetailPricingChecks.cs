using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class ProjectDetailPricingChecks
{
    public static void Run(Action<bool, string> check, string output)
    {
        var state = new AppState();
        var path = Path.Combine(output, "project-detail-prices.json");
        state.Load(path);
        var material = new Material { Name = "Frozen sheet", Unit = "m2", Cost = 2000, PriceMode = "sheet", SheetLength = 2000, SheetWidth = 1000 };
        new MaterialService(state).Add("ЛДСП", material);
        var detail = new Detail { Type = "ЛДСП", MaterialId = material.Id, LengthExpr = "1000", WidthExpr = "500", Qty = 2 };
        var product = new Product { Qty = 3, Details = [detail] };
        var project = new Project { Products = [product] };
        new ProjectService(state).Add(project);
        check(detail.ProjectUnitPrice == 1000 && Calculator.Product(state, product).Cost == 3000,
            "Project creation snapshots normalized catalog prices");
        material.Cost = 4000;
        state.Save();
        check(Calculator.Product(state, product).Cost == 3000 && MaterialPricing.UnitCost(material) == 2000,
            "Catalog price changes do not reprice an existing project");
        ProjectDetailPricing.Set(detail, material, 750);
        check(Calculator.Product(state, product).Cost == 2250 && material.Cost == 4000,
            "Detail-only price overrides preserve catalog prices and include quantities");
        check(CuttingService.BuildDetailingReport(state, project).Summary.Cost == 2250 &&
            MaterialSummaryService.Build(state, project).Rows.Single().UnitPrice == 750,
            "Detailing and material summary use project detail prices");
        new CatalogService(state).SaveProduct("Pricing", null, product);
        check(state.Database.Catalog["Pricing"].Single().Details.Single().ProjectUnitPrice is null && detail.ProjectUnitPrice == 750,
            "Saving a project product to catalog strips overrides without changing the project");
        state.Save();
        var reloaded = new AppState();
        reloaded.Load(path);
        check(reloaded.Database.Projects.Single().Products.Single().Details.Single().ProjectUnitPrice == 750,
            "Project detail price survives save and reload");
        var restored = CloudEntityMerge.Restore(CloudEntityMerge.Snapshot(state.Database));
        check(restored.Projects.Single().Products.Single().Details.Single().ProjectUnitPrice == 750,
            "Cloud project records retain detail prices");
        var second = new Detail { MaterialId = material.Id, Type = "ЛДСП", LengthExpr = "1000", WidthExpr = "500" };
        product.Details.Add(second);
        ProjectDetailPricing.Set(second, material, 500);
        var summary = MaterialSummaryService.Build(state, project);
        check(summary.Rows.Count == 2, "Different project prices for the same material remain distinct in summary");
        foreach (var invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            var rejected = false;
            try { ProjectDetailPricing.Set(detail, material, invalid); } catch (InvalidOperationException) { rejected = true; }
            check(rejected && detail.ProjectUnitPrice == 750, "Invalid override rejected without changing the stored price");
        }
        ProjectDetailPricing.Set(detail, material, 0);
        check(Calculator.Detail(state, product, detail).Cost == 0, "Zero project price is allowed");
        var other = state.Database.Materials["Фурнитура"].First();
        detail.MaterialId = other.Id;
        detail.Type = "Фурнитура";
        ProjectDetailPricing.Capture(state, product);
        check(detail.ProjectUnitPrice == MaterialPricing.UnitCost(other), "Changing material replaces its previous price snapshot");
        PackagePrices(check, output);
        CatalogRefresh(check, output);
    }

    private static void CatalogRefresh(Action<bool, string> check, string output)
    {
        var state = new AppState();
        state.Load(Path.Combine(output, "catalog-project-refresh.json"));
        var material = new Material { Name = "Refresh price", Unit = "pc", Cost = 10 };
        new MaterialService(state).Add("Фурнитура", material);
        var automatic = new Detail { Type = "Фурнитура", MaterialId = material.Id };
        var manual = new Detail { Type = "Фурнитура", MaterialId = material.Id };
        var project = new Project { Products = [new Product { Details = [automatic, manual] }] };
        var projects = new ProjectService(state);
        projects.Add(project);
        ProjectDetailPricing.Set(manual, material, 20);
        material.Cost = 30;
        state.Save();
        var offered = ProjectDetailPricing.CatalogChanges(state, project);
        check(offered.Count == 1 && offered[0].DetailId == automatic.Id && automatic.ProjectPriceIsManual == false && manual.ProjectPriceIsManual == true,
            "Catalog price comparison includes only details never manually edited");
        check(automatic.ProjectUnitPrice == 10 && manual.ProjectUnitPrice == 20 && ProjectDetailPricing.CatalogChanges(state, project).Count == 1,
            "Checking or keeping project prices does not change prices or suppress future checks");
        check(projects.UpdateCatalogPrices(project, offered) == 1 && automatic.ProjectUnitPrice == 30 && manual.ProjectUnitPrice == 20 && material.Cost == 30,
            "Accepted catalog refresh updates automatic prices only, leaving manual prices and catalog unchanged");
        check(ProjectDetailPricing.CatalogChanges(state, project).Count == 0, "Matching prices do not prompt another refresh");
        material.Cost = 40;
        state.Save();
        offered = ProjectDetailPricing.CatalogChanges(state, project);
        material.Cost = 50;
        state.Save();
        var rejected = false;
        try { projects.UpdateCatalogPrices(project, offered); } catch (InvalidOperationException) { rejected = true; }
        check(rejected && automatic.ProjectUnitPrice == 30, "Catalog changes during confirmation invalidate the offered refresh without repricing");
        offered = ProjectDetailPricing.CatalogChanges(state, project);
        ProjectDetailPricing.Set(automatic, material, 50);
        rejected = false;
        try { projects.UpdateCatalogPrices(project, offered); } catch (InvalidOperationException) { rejected = true; }
        check(rejected && automatic.ProjectPriceIsManual == true && automatic.ProjectUnitPrice == 50,
            "Manual edit during confirmation is protected even when it equals the catalog price");
        ProjectDetailPricing.UseCatalogPrice(automatic, material);
        material.Cost = 60;
        state.Save();
        check(ProjectDetailPricing.CatalogChanges(state, project).Count == 1 && automatic.ProjectPriceIsManual == false,
            "Explicit catalog-price reset restores automatic-price tracking");
        var restored = CloudEntityMerge.Restore(CloudEntityMerge.Snapshot(state.Database));
        var details = restored.Projects.Single().Products.Single().Details;
        check(details.Single(row => row.Id == manual.Id).ProjectPriceIsManual == true &&
            details.Single(row => row.Id == automatic.Id).ProjectPriceIsManual == false,
            "Cloud snapshots preserve manual and automatic price provenance");
        automatic.ProjectPriceIsManual = null;
        state.Save();
        check(automatic.ProjectPriceIsManual == true && ProjectDetailPricing.CatalogChanges(state, project).Count == 0,
            "Legacy prices with unknown provenance are conservatively protected from bulk refresh");
        var sheet = new Material { Name = "Refresh sheet basis", Unit = "m2", PriceMode = "sheet", Cost = 2000, SheetLength = 2000, SheetWidth = 1000 };
        new MaterialService(state).Add("ЛДСП", sheet);
        var panel = new Detail { MaterialId = sheet.Id, Type = "ЛДСП", LengthExpr = "1000", WidthExpr = "500" };
        var sheetProject = new Project { Products = [new Product { Details = [panel] }] };
        projects.Add(sheetProject);
        sheet.SheetWidth = 2000;
        state.Save();
        var packageOffer = ProjectDetailPricing.CatalogChanges(state, sheetProject);
        check(packageOffer.Count == 1 && panel.ProjectPriceQuantity == 2 && panel.ProjectUnitPrice == 1000,
            "Changed catalog sheet basis is offered without changing the stored price");
        projects.UpdateCatalogPrices(sheetProject, packageOffer);
        check(panel.ProjectPriceQuantity == 4 && panel.ProjectUnitPrice == 500 && panel.ProjectPriceIsManual == false,
            "Accepted package refresh adopts catalog price and conversion basis together");
    }

    private static void PackagePrices(Action<bool, string> check, string output)
    {
        var state = new AppState();
        var path = Path.Combine(output, "project-package-prices.json");
        state.Load(path);
        var sheets = new Material { Name = "Package sheet", Unit = "m2", PriceMode = "sheet", Cost = 2000, SheetLength = 2000, SheetWidth = 1000 };
        var rolls = new Material { Name = "Package roll", Unit = "lm", PriceMode = "roll", Cost = 10000, RollLength = 500 };
        new MaterialService(state).Add("ЛДСП", sheets);
        new MaterialService(state).Add("Кромка", rolls);
        var panel = new Detail { Type = "ЛДСП", MaterialId = sheets.Id, LengthExpr = "1000", WidthExpr = "500", Qty = 2 };
        var band = new Detail { Type = "Кромка", MaterialId = rolls.Id, LengthExpr = "1000", Qty = 2 };
        var product = new Product { Qty = 3, Details = [panel, band] };
        var project = new Project { Products = [product] };
        new ProjectService(state).Add(project);
        check(ProjectDetailPricing.Editor(panel, sheets) == new ProjectDetailPricing.EditorPrice("sheet", 2, 2000),
            "Project editor inherits the catalog sheet price and sheet area");
        check(ProjectDetailPricing.Editor(band, rolls) == new ProjectDetailPricing.EditorPrice("roll", 500, 10000),
            "Project editor inherits the catalog roll price and roll length");
        ProjectDetailPricing.SetEnteredPrice(panel, sheets, 1500);
        ProjectDetailPricing.SetEnteredPrice(band, rolls, 5000);
        check(panel.ProjectUnitPrice == 750 && band.ProjectUnitPrice == 10 && Calculator.Product(state, product).Cost == 2310,
            "Entered sheet and roll prices normalize and recalculate project detail costs");
        check(sheets.Cost == 2000 && rolls.Cost == 10000, "Editing package prices never writes catalog prices");
        sheets.SheetWidth = 2000;
        rolls.RollLength = 250;
        state.Save();
        var reopened = new AppState();
        reopened.Load(path);
        var saved = reopened.Database.Projects.Single().Products.Single();
        var savedPanel = saved.Details.Single(row => row.MaterialId == sheets.Id);
        var savedBand = saved.Details.Single(row => row.MaterialId == rolls.Id);
        check(ProjectDetailPricing.Editor(savedPanel, sheets).Amount == 1500 &&
            ProjectDetailPricing.Editor(savedBand, rolls).Amount == 5000 && Calculator.Product(reopened, saved).Cost == 2310,
            "Stored sheet area, roll length and entered prices survive restart and catalog dimension changes");
        var cloud = CloudEntityMerge.Restore(CloudEntityMerge.Snapshot(reopened.Database));
        check(cloud.Projects.Single().Products.Single().Details.Single(row => row.MaterialId == sheets.Id).ProjectPriceQuantity == 2,
            "Cloud project snapshots preserve the package price basis");
        ProjectDetailPricing.UseCatalogPrice(savedPanel, sheets);
        check(ProjectDetailPricing.Editor(savedPanel, sheets) == new ProjectDetailPricing.EditorPrice("sheet", 4, 2000),
            "Catalog reset explicitly adopts the current sheet dimensions and price");
        ProjectDetailPricing.SetEnteredPrice(savedPanel, sheets, 0);
        check(ProjectDetailPricing.UnitPrice(savedPanel, sheets) == 0, "Zero sheet price is supported");
        var legacy = new Detail { MaterialId = sheets.Id, Type = "ЛДСП", ProjectUnitPrice = 75,
            ProjectPriceMaterialId = sheets.Id, ProjectPriceUnit = "m2" };
        ProjectDetailPricing.Capture(state, new Product { Details = [legacy] });
        check(ProjectDetailPricing.Editor(legacy, sheets) == new ProjectDetailPricing.EditorPrice("sheet", 4, 300) && legacy.ProjectUnitPrice == 75,
            "Legacy normalized prices acquire display metadata without being repriced");
        var unitMaterial = state.Database.Materials["Фурнитура"].First();
        var unitDetail = new Detail { MaterialId = unitMaterial.Id, Type = "Фурнитура" };
        ProjectDetailPricing.SetEnteredPrice(unitDetail, unitMaterial, 123);
        check(ProjectDetailPricing.Editor(unitDetail, unitMaterial) == new ProjectDetailPricing.EditorPrice("unit", 1, 123),
            "Piece and per-unit materials keep direct price entry");
        var perMeter = new Material { Unit = "lm", PriceMode = "unit", Cost = 45 };
        var meterDetail = new Detail();
        ProjectDetailPricing.SetEnteredPrice(meterDetail, perMeter, 25);
        check(ProjectDetailPricing.Editor(meterDetail, perMeter) == new ProjectDetailPricing.EditorPrice("unit", 1, 25),
            "Per-meter materials keep the catalog per-meter pricing mode");
    }
}
