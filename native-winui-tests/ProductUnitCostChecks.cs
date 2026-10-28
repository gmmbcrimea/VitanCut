using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class ProductUnitCostChecks
{
    public static void Run(Action<bool, string> check, string output)
    {
        var state = new AppState();
        state.Load(Path.Combine(output, "product-unit-cost.json"));
        var sheet = new Material { Name = "ЛДСП тест", Unit = "m2", Cost = 100 };
        var hardware = new Material { Name = "Unit hardware", Unit = "pc", Cost = 20 };
        var edge = new Material { Name = "Unit edge", Unit = "lm", Cost = 10 };
        state.Database.Materials["ЛДСП"] = [sheet];
        state.Database.Materials["Фурнитура"] = [hardware];
        state.Database.Materials["Кромка"] = [edge];
        var sheetDetail = new Detail { Type = "ЛДСП", MaterialId = sheet.Id, LengthExpr = "1000", WidthExpr = "500", Qty = 3 };
        var product = new Product { Qty = 2, Details = [sheetDetail,
            new Detail { Type = "Фурнитура", MaterialId = hardware.Id, Qty = 2 },
            new Detail { Type = "Кромка", MaterialId = edge.Id, LengthExpr = "2000" }] };
        var project = new Project { Products = [product], Payrolls = [new Payroll { Mode = "area", Rate = 500 },
            new Payroll { Mode = "area", Rate = 100 }, new Payroll { Mode = "fixed", Amount = 1000, Rate = 9999 }] };
        state.Database.Projects.Add(project);
        check(Calculator.ProductUnitCost(state, project, product) == 1110,
            "Unit price includes all materials plus all square-meter payroll rates for one product area");
        check(Calculator.ProductUnitCost(state, project, product) * product.Qty + 1000 == Calculator.Project(state, project).Cost,
            "Unit price reconciles with project totals while fixed payroll remains project-wide");
        foreach (var quantity in new[] { 1d, 5d, 0d })
        {
            product.Qty = quantity;
            check(Calculator.ProductUnitCost(state, project, product) == 1110,
                "Unit price does not multiply by product quantity or divide by zero: " + quantity);
        }
        product.Qty = 2;
        var before = state.ExportJson();
        Calculator.ProductUnitCost(state, project, product);
        check(before == state.ExportJson() && !sheetDetail.ProjectUnitPrice.HasValue,
            "Unit price display does not capture or change saved project and catalog prices");
        ProjectDetailPricing.SetEnteredPrice(sheetDetail, sheet, 200, 10);
        check(Math.Abs(Calculator.ProductUnitCost(state, project, product) - 1290) < .000001 && sheet.Cost == 100,
            "Unit price respects project-only price overrides and waste allowance without editing the catalog");
        project.Payrolls.Clear();
        check(Math.Abs(Calculator.ProductUnitCost(state, project, product) - 390) < .000001,
            "Without payroll the unit price contains only material costs");
        sheetDetail.LengthExpr = "2000";
        project.Payrolls.Add(new Payroll { Mode = "area", Rate = 100 });
        check(Math.Abs(Calculator.ProductUnitCost(state, project, product) - 1020) < .000001,
            "Unit price updates after a dimension or payroll-rate change");
        check(Calculator.ProductUnitCost(state, project, new Product()) == 0,
            "An empty product has zero material and area payroll cost");
    }
}
