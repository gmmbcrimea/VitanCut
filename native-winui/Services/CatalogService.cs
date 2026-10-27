using VitanCut.WinUI.Models;
using System.Text.Json;

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

    public IReadOnlyList<CustomerProductItem> Browse(string? query = null) => state.Database.Catalog
        .SelectMany(pair => pair.Value.Select(product => new CustomerProductItem(pair.Key, null, product)))
        .Where(item => UiTextSearch.Matches(query ?? "", item.Product.Name, item.Counterparty, item.Product.Group ?? ""))
        .OrderBy(item => item.Product.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Counterparty).ToList();

    public IReadOnlyList<string> GetProductGroups() => new[] { "" }.Concat(Browse().Select(item => item.Product.Group?.Trim() ?? "")
        .Where(group => group.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(group => group, StringComparer.CurrentCultureIgnoreCase)).ToList();

    private CustomerProductItem ResolveProduct(string id) => Browse().FirstOrDefault(item => item.Product.Id == id)
        ?? throw new InvalidOperationException("Изделие каталога уже удалено или изменено в облаке.");

    public void SetProductGroup(IEnumerable<string> productIds, string group)
    {
        var products = productIds.Distinct().Select(ResolveProduct).ToList();
        state.CommitMutation(() => { foreach (var item in products) item.Product.Group = group.Trim(); });
    }

    public void TransferProducts(IEnumerable<string> productIds, string targetCounterparty, bool copy)
    {
        if (!GetCounterparties().Contains(targetCounterparty)) throw new InvalidOperationException("Выберите заказчика");
        var products = productIds.Distinct().Select(ResolveProduct).Where(item => item.Counterparty != targetCounterparty).ToList();
        var transfers = products.Select(item => (Source: item, Product: copy ? CloneForCatalog(item.Product) : item.Product)).ToList();
        state.CommitMutation(() =>
        {
            state.Database.Catalog.TryAdd(targetCounterparty, []);
            foreach (var transfer in transfers)
            {
                if (!copy) state.Database.Catalog[transfer.Source.Counterparty].Remove(transfer.Source.Product);
                state.Database.Catalog[targetCounterparty].Add(transfer.Product);
            }
        });
    }

    public IReadOnlyList<Project> UsingProjects(Product product) => state.Database.Projects.Where(project => project.Products.Any(item =>
        item.SourceCatalogProductId == product.Id || item.Id == product.Id ||
        (item.SourceCatalogProductId.Length == 0 && item.Name == product.Name && item.Details.Count > 0 &&
         item.Details.Select(detail => detail.Id).SequenceEqual(product.Details.Select(detail => detail.Id))))).ToList();

    public void AddCounterparty(string name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            state.Database.Counterparties.Contains(name, StringComparer.CurrentCultureIgnoreCase)) return;
        state.CommitMutation(() => state.Database.Counterparties.Add(name.Trim()));
    }

    public void RenameCounterparty(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) return;
        newName = newName.Trim();
        state.CommitMutation(() =>
        {
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
                if (!state.Database.Catalog.TryGetValue(newName, out var target)) state.Database.Catalog[newName] = products;
                else target.AddRange(products);
            }
        });
    }

    public void DeleteCounterparty(string name)
    {
        const string unassignedName = "Без заказчика";
        state.CommitMutation(() =>
        {
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
        });
    }

    public void SaveProduct(string counterparty, Product? previous, Product saved)
    {
        if (saved.Details.Any(detail => detail.ProjectUnitPrice.HasValue))
            saved = JsonSerializer.Deserialize<Product>(JsonSerializer.Serialize(saved))!;
        ProjectDetailPricing.Clear(saved);
        var products = GetProducts(counterparty).ToList();
        if (previous is null)
        {
            saved.Id = Ids.NewId();
            if (saved.CreatedAt == DateTimeOffset.MinValue) saved.CreatedAt = DateTimeOffset.Now;
            products.Add(saved);
        }
        else
        {
            var index = products.FindIndex(item => item.Id == previous.Id);
            if (index < 0) throw new InvalidOperationException("Изделие каталога уже удалено или изменено в облаке.");
            saved.Id = products[index].Id;
            saved.Group = products[index].Group;
            saved.CreatedAt = products[index].CreatedAt;
            products[index] = saved;
        }
        state.CommitMutation(() => state.Database.Catalog[counterparty] = products);
    }

    public void DeleteProduct(string counterparty, Product product)
    {
        if (!state.Database.Catalog.TryGetValue(counterparty, out var products)) return;
        var index = products.FindIndex(item => item.Id == product.Id);
        if (index < 0) return;
        state.CommitMutation(() => products.RemoveAt(index));
    }

    public void CopyProduct(string sourceCounterparty, Product source, string targetCounterparty)
    {
        if (string.Equals(sourceCounterparty, targetCounterparty, StringComparison.CurrentCultureIgnoreCase)) return;
        var copy = CloneForCatalog(source);
        SaveProduct(targetCounterparty, null, copy);
    }

    private static Product CloneForCatalog(Product source)
    {
        var copy = JsonSerializer.Deserialize<Product>(JsonSerializer.Serialize(source))
            ?? throw new InvalidDataException("Не удалось скопировать изделие.");
        copy.Id = Ids.NewId();
        copy.CreatedAt = DateTimeOffset.Now;
        copy.SourceCatalogProductId = "";
        ProjectDetailPricing.Clear(copy);
        var detailIds = copy.Details.ToDictionary(detail => detail.Id, _ => Ids.NewId(), StringComparer.OrdinalIgnoreCase);
        foreach (var detail in copy.Details)
        {
            detail.LengthExpr = FormulaEvaluator.RemapReferences(detail.LengthExpr, detailIds);
            detail.WidthExpr = FormulaEvaluator.RemapReferences(detail.WidthExpr, detailIds);
            detail.Id = detailIds[detail.Id];
        }
        foreach (var size in copy.FixedSizes) size.Id = Ids.NewId();
        return copy;
    }
}
