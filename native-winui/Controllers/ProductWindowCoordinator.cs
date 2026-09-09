using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Opens product editor windows and persists their result in the appropriate collection.
/// </summary>
public sealed class ProductWindowCoordinator(OwnedWindowActivator windows, ProjectService projects, CatalogService catalog)
{
    public void OpenProjectProduct(Project project, Product? product, Action onSaved)
    {
        var window = new Views.ProductWindow(project, product, catalog);
        window.ProductSaved += (saved, isNew) =>
        {
            projects.SaveProduct(project, isNew || product is null ? null : product, saved);
            onSaved();
        };
        windows.Activate(window);
    }

    public void OpenCatalogProduct(string counterparty, Product? product, Action onSaved)
    {
        var catalogProject = new Project { Name = "Каталог", Counterparty = counterparty, Address = string.Empty };
        var window = new Views.ProductWindow(catalogProject, product, catalog, catalogEditor: true);
        window.ProductSaved += (saved, isNew) =>
        {
            catalog.SaveProduct(counterparty, isNew || product is null ? null : product, saved);
            onSaved();
        };
        windows.Activate(window);
    }
}