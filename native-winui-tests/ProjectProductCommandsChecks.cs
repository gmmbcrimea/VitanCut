using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class ProjectProductCommandsChecks
{
    internal static void Run(Action<bool, string> check, string output)
    {
        var state = new AppState();
        var path = Path.Combine(output, "project-product-commands.json");
        state.Load(path);
        var material = state.Database.Materials.Values.SelectMany(materials => materials).First(item => item.Unit == "m2");
        var type = state.Database.Materials.Single(group => group.Value.Contains(material)).Key;
        var product = new Product { Details = [new Detail { MaterialId = material.Id, Type = type, LengthExpr = "1000", WidthExpr = "1000" }] };
        var second = new Product { Name = "Second" };
        var third = new Product { Name = "Third" };
        var project = new Project { Products = [product, second, third], Payrolls = [new Payroll { Mode = "area", Rate = 100 }] };
        var catalogProduct = new Product { Id = product.Id, Qty = 7 };
        state.Database.Catalog["Customer"] = [catalogProduct];
        state.Database.Projects.Add(project);
        state.Save();
        var service = new ProjectService(state);
        var before = Calculator.Project(state, project);
        service.SetProductQuantity(project, product.Id, 3);
        var after = Calculator.Project(state, project);
        check(product.Qty == 3 && catalogProduct.Qty == 7 && Math.Abs(after.Area - before.Area * 3) < 0.001 &&
            Math.Abs(after.Payroll - before.Payroll * 3) < 0.001 && Math.Abs(after.Cost - before.Cost * 3) < 0.001,
            "Inline quantity updates project area, material and payroll totals without changing the catalog");
        var restored = new AppState();
        restored.Load(path);
        check(restored.Database.Projects.Single(p => p.Id == project.Id).Products.Single(p => p.Id == product.Id).Qty == 3,
            "Project quantity changed outside the editor survives reopening the database");
        foreach (var invalid in new[] { 0d, -1, 1.5, double.NaN, double.PositiveInfinity })
        {
            var failed = false;
            try { service.SetProductQuantity(project, product.Id, invalid); }
            catch (InvalidOperationException) { failed = true; }
            check(failed && product.Qty == 3, "Invalid inline product quantities cannot change the project");
        }
        var snapshot = state.ExportJson();
        var rejected = false;
        try { service.DeleteProducts(project, [product.Id, "missing"]); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && state.ExportJson() == snapshot, "A stale bulk product selection is rejected without a partial deletion");
        service.DeleteProducts(project, [second.Id, product.Id, second.Id]);
        check(project.Products.Count == 1 && project.Products[0] == third && catalogProduct.Qty == 7,
            "Bulk deletion removes exactly the selected project instances in one operation");
    }
}
