using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

/// <summary>
/// Manages counterparties and their reusable catalog products.
/// </summary>
public sealed class CatalogService(AppState state)
{
    public IReadOnlyList<string> GetCounterparties()
    {
        state.Database.Counterparties = state.Database.Counterparties
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name)
            .ToList();
        return state.Database.Counterparties;
    }

    public IReadOnlyList<Product> GetProducts(string counterparty)
    {
        if (state.Database.Catalog.TryGetValue(counterparty, out var products)) return products;
        return [];
    }

    public void AddCounterparty(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            state.Database.Counterparties.Contains(name, StringComparer.CurrentCultureIgnoreCase)) return;
        state.Database.Counterparties.Add(name.Trim());
        state.Save();
    }

    public void RenameCounterparty(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        newName = newName.Trim();
        state.Database.Counterparties.RemoveAll(name => string.Equals(name, oldName, StringComparison.CurrentCultureIgnoreCase));
        if (!state.Database.Counterparties.Contains(newName, StringComparer.CurrentCultureIgnoreCase))
            state.Database.Counterparties.Add(newName);

        foreach (var project in state.Database.Projects.Where(project => string.Equals(project.Counterparty, oldName, StringComparison.CurrentCultureIgnoreCase)))
        {
            project.Counterparty = newName;
            project.UpdatedAt = DateTimeOffset.Now;
        }

        if (state.Database.Catalog.Remove(oldName, out var products))
        {
            if (!state.Database.Catalog.TryGetValue(newName, out var target))
                state.Database.Catalog[newName] = products;
            else
                target.AddRange(products);
        }
        state.Save();
    }

    public void DeleteCounterparty(string name)
    {
        const string unassignedName = "Без заказчика";
        state.Database.Counterparties.RemoveAll(item => string.Equals(item, name, StringComparison.CurrentCultureIgnoreCase));
        foreach (var project in state.Database.Projects.Where(project => string.Equals(project.Counterparty, name, StringComparison.CurrentCultureIgnoreCase)))
        {
            project.Counterparty = unassignedName;
            project.UpdatedAt = DateTimeOffset.Now;
        }
        if (!state.Database.Counterparties.Contains(unassignedName, StringComparer.CurrentCultureIgnoreCase))
            state.Database.Counterparties.Add(unassignedName);
        if (name != unassignedName && state.Database.Catalog.Remove(name, out var catalogProducts))
        {
            if (!state.Database.Catalog.TryGetValue(unassignedName, out var target)) state.Database.Catalog[unassignedName] = target = [];
            target.AddRange(catalogProducts);
        }
        state.Save();
    }

    public void SaveProduct(string counterparty, Product? previous, Product saved)
    {
        var products = GetProducts(counterparty).ToList();
        if (previous is null)
        {
            saved.Id = Ids.NewId();
            products.Add(saved);
        }
        else
        {
            var index = products.IndexOf(previous);
            if (index >= 0) products[index] = saved;
        }
        state.Database.Catalog[counterparty] = products;
        state.Save();
    }

    public void DeleteProduct(string counterparty, Product product)
    {
        if (!state.Database.Catalog.TryGetValue(counterparty, out var products)) return;
        var index = products.IndexOf(product);
        if (index < 0) return;
        products.RemoveAt(index);
        try { state.Save(); }
        catch { products.Insert(index, product); throw; }
    }
}
