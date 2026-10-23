using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class ProjectCatalogPickerChecks
{
    public static void Run(Action<bool, string> check, string output)
    {
        var state = new AppState();
        var path = Path.Combine(output, "project-catalog-picker.json");
        state.Load(path);
        var projects = new ProjectService(state);
        var catalog = new CatalogService(state);
        var material = state.Database.Materials.First().Value.First();
        var type = state.Database.Materials.First().Key;
        var first = new Detail { Name = "Side", Type = type, MaterialId = material.Id, LengthExpr = "600", WidthExpr = "400" };
        var second = new Detail { Type = type, MaterialId = material.Id, LengthExpr = $"[[detail:{first.Id}:length]] - 20", WidthExpr = "300" };
        var template = new Product { Name = "Catalog cabinet", Qty = 7, Image = "test-image", Details = [first, second],
            FixedSizes = [new() { Name = "Custom", Value = 42 }] };
        catalog.SaveProduct("Customer A", null, template);
        var project = new Project { Counterparty = "Customer A" };
        projects.Add(project);
        var copy = projects.AddCatalogProduct(project, template.Id);
        check(project.Products.Contains(copy) && copy.Id != template.Id && copy.Qty == 1 && copy.Image == template.Image,
            "Catalog picker directly adds one independent product to the project");
        check(!ReferenceEquals(copy.Details, template.Details) && !ReferenceEquals(copy.Details[0], first) &&
            !ReferenceEquals(copy.FixedSizes[0], template.FixedSizes[0]), "Catalog picker deeply copies details and fixed dimensions");
        check(copy.Details[1].LengthExpr == $"[[detail:{copy.Details[0].Id}:length]] - 20",
            "Catalog picker preserves internal detail formula references");
        check(copy.Details[0].ProjectUnitPrice.HasValue && !first.ProjectUnitPrice.HasValue,
            "Catalog picker captures project prices without modifying the catalog");
        copy.Details[0].Name = "Edited side";
        check(first.Name == "Side" && template.Qty == 7, "Project edits do not change the catalog template");
        var again = projects.AddCatalogProduct(project, template.Id);
        check(again.Id != copy.Id && project.Products.Count == 2, "Repeated catalog selection creates distinct product instances");
        var otherProject = new Project { Counterparty = "Customer B" };
        projects.Add(otherProject);
        var rejected = false;
        try { projects.AddCatalogProduct(otherProject, template.Id); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && otherProject.Products.Count == 0, "Catalog picker cannot import a different customer's product");
        catalog.DeleteProduct("Customer A", template);
        rejected = false;
        try { projects.AddCatalogProduct(project, template.Id); }
        catch (InvalidOperationException) { rejected = true; }
        check(rejected && project.Products.Count == 2, "Stale catalog selection fails without adding a product");
        var reloaded = new AppState();
        reloaded.Load(path);
        check(reloaded.Database.Projects.Single(item => item.Id == project.Id).Products.Count == 2,
            "Catalog picker additions persist across application restart");
    }
}
