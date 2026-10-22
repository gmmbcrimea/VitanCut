using System.Collections.ObjectModel;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public static class MaterialListUpdates
{
    public static bool ApplySaved(ObservableCollection<MaterialChoice> items, MaterialChoice saved,
        IReadOnlyList<MaterialChoice> ordered, bool nameChanged)
    {
        var index = items.ToList().FindIndex(item => item.Material.Id == saved.Material.Id);
        if (index < 0 || items.Count != ordered.Count ||
            !items.Where(item => item.Material.Id != saved.Material.Id).Select(item => item.Material.Id)
                .SequenceEqual(ordered.Where(item => item.Material.Id != saved.Material.Id).Select(item => item.Material.Id)))
            return false;

        // Price/dimension edits do not change list labels: leave their containers untouched.
        if (nameChanged || !ReferenceEquals(items[index].Material, saved.Material)) items[index] = saved;
        var target = ordered.ToList().FindIndex(item => item.Material.Id == saved.Material.Id);
        if (index != target) items.Move(index, target);
        return true;
    }
}
