using System.Collections.ObjectModel;
using System.Collections.Specialized;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class CatalogListUpdatesChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var original = Enumerable.Range(0, 120).Select(i => new CustomerProductItem("Customer", null,
            new Product { Name = $"Product {i:000}" })).ToList();
        var items = new ObservableCollection<CustomerProductItem>(original);
        var events = new List<NotifyCollectionChangedAction>();
        items.CollectionChanged += (_, e) => events.Add(e.Action);
        var updated = original.Select(item => item with { }).ToArray();
        updated[98] = original[98] with { Product = new Product { Id = original[98].Product.Id, Name = original[98].Product.Name } };
        CatalogListUpdates.Apply(items, updated);
        check(events.SequenceEqual([NotifyCollectionChangedAction.Replace]) && ReferenceEquals(items[0], original[0]) &&
            ReferenceEquals(items[119], original[119]) && items[98] == updated[98],
            "Saving a catalog product replaces only its row, retaining the list and other realized items");
        events.Clear();
        var reordered = new[] { updated[98] }.Concat(updated.Where((_, i) => i != 98)).ToArray();
        CatalogListUpdates.Apply(items, reordered);
        check(!events.Contains(NotifyCollectionChangedAction.Reset) && items.SequenceEqual(reordered) &&
            items[0].Product.Id == original[98].Product.Id,
            "Renaming a catalog product moves its stable identity without resetting the list");
        events.Clear();
        CatalogListUpdates.Apply(items, reordered.Where(item => item.Product.Id != original[98].Product.Id).ToList());
        check(events.SequenceEqual([NotifyCollectionChangedAction.Remove]) && items.Count == 119,
            "A saved product leaving the current search or group removes only that result");
        var extra = new CustomerProductItem("Customer", null, new Product { Name = "Another product" });
        events.Clear();
        CatalogListUpdates.Apply(items, items.Append(extra).ToList());
        check(events.SequenceEqual([NotifyCollectionChangedAction.Add]) && items[^1] == extra,
            "Adding a catalog product appends its row without resetting existing results");
        events.Clear();
        CatalogListUpdates.Apply(items, []);
        check(items.Count == 0 && !events.Contains(NotifyCollectionChangedAction.Reset),
            "An empty catalog result is reconciled without replacing the collection");
    }
}
