using System.Collections.ObjectModel;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

internal static class CatalogListUpdates
{
    internal static (string Counterparty, string Id) Key(CustomerProductItem item) => (item.Counterparty, item.Product.Id);

    internal static void Apply(ObservableCollection<CustomerProductItem> items, IReadOnlyList<CustomerProductItem> ordered)
    {
        var retained = ordered.Select(Key).ToHashSet();
        for (var i = items.Count - 1; i >= 0; i--)
            if (!retained.Contains(Key(items[i]))) items.RemoveAt(i);
        for (var i = 0; i < ordered.Count; i++)
        {
            var updated = ordered[i];
            var key = Key(updated);
            if (i >= items.Count || Key(items[i]) != key)
            {
                var previous = i + 1;
                while (previous < items.Count && Key(items[previous]) != key) previous++;
                if (previous < items.Count) items.Move(previous, i);
                else items.Insert(i, updated);
            }
            if (!ReferenceEquals(items[i].Product, updated.Product) || !ReferenceEquals(items[i].Project, updated.Project))
                items[i] = updated;
        }
    }
}
