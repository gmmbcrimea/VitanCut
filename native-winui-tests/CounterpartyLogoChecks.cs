using System.Text.Json;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class CounterpartyLogoChecks
{
    internal static void Run(Action<bool, string> check, string output)
    {
        var path = Path.Combine(output, "counterparty-logos.json");
        var state = new AppState();
        state.Load(path);
        var catalog = new CatalogService(state);
        const string logo = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl6VJ8AAAAASUVORK5CYII=";
        catalog.SaveCounterparty(null, "Logo customer", logo);
        check(catalog.LogoFor("LOGO CUSTOMER") == logo && state.Database.Counterparties.Contains("Logo customer"),
            "A customer can be created with an embedded logo and looked up case-insensitively");
        var project = new Project { Counterparty = "Logo customer" };
        var product = new Product();
        state.Database.Projects.Add(project);
        state.Database.Catalog["Logo customer"] = [product];
        catalog.SaveCounterparty("Logo customer", "Renamed", logo);
        check(catalog.LogoFor("Renamed") == logo && catalog.LogoFor("Logo customer") == "" &&
            project.Counterparty == "Renamed" && state.Database.Catalog["Renamed"].Single() == product,
            "Renaming keeps the customer logo and retargets project and catalog links");
        var restored = new AppState();
        restored.Load(path);
        check(new CatalogService(restored).LogoFor("Renamed") == logo,
            "Customer logos survive database save and reload without access to the source image");
        var cloud = CloudEntityMerge.Restore(CloudEntityMerge.Snapshot(state.Database));
        check(cloud.CounterpartyLogos["Renamed"] == logo &&
            CloudEntityMerge.Restore(CloudEntityMerge.Snapshot(new Database())).CounterpartyLogos.Count == 0,
            "Cloud snapshots round-trip logos and remain compatible with databases without logos");
        var imported = new AppState();
        imported.Load(Path.Combine(output, "logo-import.json"));
        imported.ImportJson(state.ExportJson());
        check(new CatalogService(imported).LogoFor("Renamed") == logo, "JSON import and export preserve customer logos");
        var count = state.Database.Counterparties.Count;
        catalog.SaveCounterparty("Renamed", "Renamed", "");
        check(catalog.LogoFor("Renamed") == "" && state.Database.Counterparties.Count == count,
            "Removing a logo without renaming updates the existing customer rather than adding a duplicate");
        catalog.SaveCounterparty("Renamed", "Renamed", logo);
        catalog.DeleteCounterparty("Renamed");
        check(catalog.LogoFor("Renamed") == "" && project.Counterparty == "Без заказчика",
            "Deleting a customer removes only that logo while preserving the project in unassigned customers");
        check(JsonSerializer.Deserialize<Database>("{\"Counterparties\":[\"Old customer\"]}")!.CounterpartyLogos.Count == 0,
            "Legacy databases do not require a logo migration");
        var invalid = false;
        try { catalog.SaveCounterparty(null, "Bad logo", @"C:\private\logo.png"); }
        catch (InvalidOperationException) { invalid = true; }
        check(invalid && !state.Database.Counterparties.Contains("Bad logo"),
            "Logo storage rejects external file paths before changing the database");
    }
}
