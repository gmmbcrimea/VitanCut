using System.Text.Json;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class CatalogBrowserChecks
{
    public static void Run(Action<bool, string> check, string output)
    {
        var legacy = JsonSerializer.Deserialize<Product>("{\"Name\":\"Legacy\"}")!;
        check(legacy.Group == "" && legacy.CreatedAt == DateTimeOffset.MinValue, "Legacy catalog products default to ungrouped with unknown creation date");
        var path = Path.Combine(output, "catalog-browser.json");
        var state = new AppState(); state.Load(path);
        var catalog = new CatalogService(state);
        catalog.AddCounterparty("A"); catalog.AddCounterparty("B");
        var firstDetail = new Detail { Name = "Side", LengthExpr = "800", WidthExpr = "400" };
        var first = new Product { Name = "Cabinet", Details = [firstDetail,
            new() { LengthExpr = $"[[detail:{firstDetail.Id}:length]] - 10", WidthExpr = "300" }] };
        var second = new Product { Name = "Desk" };
        catalog.SaveProduct("A", null, first); catalog.SaveProduct("B", null, second);
        var created = first.CreatedAt;
        check(created != DateTimeOffset.MinValue && catalog.GetProductGroups().SequenceEqual([""]), "New catalog products get a creation date and start ungrouped");
        catalog.SetProductGroup([first.Id, second.Id], " Furniture ");
        check(first.Group == "Furniture" && second.Group == "Furniture" && catalog.GetProducts("A").Contains(first) && catalog.GetProducts("B").Contains(second),
            "Bulk grouping across customers changes groups without changing ownership");
        check(catalog.Browse("Furniture").Count == 2 && catalog.Browse("A Cabinet").Single().Product == first,
            "Catalog search matches products, customer labels and group names across the whole catalog");
        var edited = JsonSerializer.Deserialize<Product>(JsonSerializer.Serialize(first))!;
        edited.Name = "Cabinet edited"; edited.Group = "";
        catalog.SaveProduct("A", first, edited);
        first = edited;
        check(first.Group == "Furniture" && first.CreatedAt == created, "Editing a catalog product preserves group and original creation date");
        var projects = new ProjectService(state);
        var project = new Project { Counterparty = "B" }; projects.Add(project);
        var added = projects.AddCatalogProduct(project, first.Id, "A");
        check(added.SourceCatalogProductId == first.Id && catalog.UsingProjects(first).Single() == project,
            "All-customer project picker records a stable catalog origin for product information");
        catalog.TransferProducts([first.Id], "B", copy: false);
        check(!catalog.GetProducts("A").Any() && catalog.GetProducts("B").Contains(first) && first.Group == "Furniture" &&
            first.CreatedAt == created && catalog.UsingProjects(first).Single() == project,
            "Customer move preserves catalog identity, group, creation date and project usage");
        catalog.TransferProducts([first.Id], "A", copy: true);
        var copy = catalog.GetProducts("A").Single();
        check(copy.Id != first.Id && copy.Group == first.Group && !ReferenceEquals(copy.Details[0], first.Details[0]) &&
            copy.Details[0].Id != first.Details[0].Id && copy.Details[1].LengthExpr == $"[[detail:{copy.Details[0].Id}:length]] - 10",
            "Customer copy creates independent details and remaps internal formula references");
        catalog.SetProductGroup([first.Id], "");
        check(first.Group == "" && second.Group == "Furniture" && catalog.GetProducts("B").Contains(first), "Removing a product from a group does not affect customer or other products");
        var rejected = false;
        try { catalog.SetProductGroup([second.Id, "missing"], "Wrong"); } catch (InvalidOperationException) { rejected = true; }
        check(rejected && second.Group == "Furniture", "Stale bulk grouping validates all products before any mutation");
        rejected = false;
        try { catalog.TransferProducts([second.Id, "missing"], "A", false); } catch (InvalidOperationException) { rejected = true; }
        check(rejected && catalog.GetProducts("B").Contains(second), "Stale customer transfer cannot partially move selected products");
        state.Save();
        var restarted = new AppState(); restarted.Load(path);
        check(new CatalogService(restarted).GetProducts("B").Single(item => item.Id == second.Id).Group == "Furniture",
            "Catalog groups persist across restart");
        var cloud = CloudEntityMerge.Restore(CloudEntityMerge.Snapshot(state.Database));
        check(cloud.Catalog["A"].Single().Group == "Furniture" && cloud.Catalog["B"].Single(item => item.Id == first.Id).CreatedAt == created &&
            cloud.Projects.Single().Products.Single().SourceCatalogProductId == first.Id,
            "Cloud entities preserve product groups, creation dates and project origin without a new entity type");
    }
}
