using System.Collections.ObjectModel;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

internal sealed class ProductDetailEditor(AppState state, Product product)
{
    private Dictionary<(string Type, string Id), MaterialChoice> _materials = [];
    private Database? _database;
    private long _revision = -1;

    internal bool Contains(Detail target) => product.Details.Contains(target);

    internal MaterialChoice? MaterialFor(Detail detail)
    {
        if (string.IsNullOrEmpty(detail.MaterialId)) return null;
        var revision = state.Revision;
        var database = state.Database;
        if (!ReferenceEquals(database, _database) || _revision != revision)
        {
            var materials = new Dictionary<(string, string), MaterialChoice>();
            foreach (var choice in state.MaterialChoices())
                materials.TryAdd((choice.Type, choice.Material.Id), choice);
            _materials = materials;
            _database = database;
            _revision = revision;
        }
        return _materials.GetValueOrDefault((detail.Type, detail.MaterialId));
    }

    internal MaterialEditResult ApplyMaterial(Detail target, MaterialChoice choice)
    {
        if (!Contains(target)) return new(MaterialEditStatus.MissingDetail);
        if (state.FindMaterial(choice.Type, choice.Material.Id) is not { } material)
            return new(MaterialEditStatus.MissingMaterial);
        if (target.IsGeneratedLeg && !LegDetails.IsMaterial(new MaterialChoice(choice.Type, material)))
            return new(MaterialEditStatus.InvalidLegMaterial);

        if (!target.IsGeneratedEdgeBand)
        {
            target.Type = choice.Type;
            target.MaterialId = material.Id;
            return new(MaterialEditStatus.Applied, target);
        }

        var replacement = new MaterialChoice(choice.Type, material);
        if (!EdgeBandingService.ReplaceGeneratedMaterial(state, product, target, replacement))
            return new(MaterialEditStatus.Unchanged, target);
        return new(MaterialEditStatus.Applied, product.Details.FirstOrDefault(detail =>
            detail.IsGeneratedEdgeBand && detail.Type == choice.Type && detail.MaterialId == material.Id));
    }

    internal void SyncRows<TRow>(ObservableCollection<TRow> rows, Func<TRow, Detail> detailOf, Func<Detail, TRow> createRow)
        where TRow : class
    {
        var details = product.Details;
        var desired = details.ToHashSet();
        for (var index = rows.Count - 1; index >= 0; index--)
            if (!desired.Contains(detailOf(rows[index]))) rows.RemoveAt(index);

        // Keeping existing rows in place preserves their realized controls and input state.
        for (var index = 0; index < details.Count; index++)
        {
            var detail = details[index];
            if (index < rows.Count && ReferenceEquals(detailOf(rows[index]), detail)) continue;
            var existing = index + 1;
            while (existing < rows.Count && !ReferenceEquals(detailOf(rows[existing]), detail)) existing++;
            if (existing < rows.Count) rows.Move(existing, index);
            else rows.Insert(index, createRow(detail));
        }
    }
}

internal enum MaterialEditStatus { Applied, Unchanged, MissingDetail, MissingMaterial, InvalidLegMaterial }

internal readonly record struct MaterialEditResult(MaterialEditStatus Status, Detail? Target = null);
