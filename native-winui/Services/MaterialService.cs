using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

/// <summary>
/// Centralizes material groups and material mutations.
/// </summary>
public sealed class MaterialService(AppState state)
{
    public bool IsUsed(MaterialChoice choice) => state.Database.Projects.SelectMany(project => project.Products)
        .Concat(state.Database.Catalog.Values.SelectMany(products => products))
        .SelectMany(product => product.Details).Any(detail => detail.Type == choice.Type && detail.MaterialId == choice.Material.Id ||
            detail.EdgeBands.Any(edge => edge.Type == choice.Type && edge.MaterialId == choice.Material.Id));

    public void Validate(Material material, Material? except = null)
    {
        if (string.IsNullOrWhiteSpace(material.Name)) throw new InvalidOperationException("Введите название материала.");
        if (ExistsName(material.Name, except)) throw new InvalidOperationException("Материал с таким названием уже существует.");
        if (!double.IsFinite(material.Cost) || material.Cost < 0) throw new InvalidOperationException("Себестоимость должна быть неотрицательным числом.");
        if (material.Unit is not ("m2" or "lm" or "pc")) throw new InvalidOperationException("Выберите единицу измерения.");
        if (material.Unit == "m2" && (!double.IsFinite(material.SheetLength) || !double.IsFinite(material.SheetWidth) || material.SheetLength < 0 || material.SheetWidth < 0))
            throw new InvalidOperationException("Размеры листа должны быть неотрицательными числами.");
    }
    public bool ExistsName(string name, Material? except = null) =>
        !string.IsNullOrWhiteSpace(name) && state.Database.Materials.Values
            .SelectMany(materials => materials)
            .Any(material => material != except && string.Equals(material.Name.Trim(), name.Trim(), StringComparison.CurrentCultureIgnoreCase));

    public string CreateUniqueName(string baseName)
    {
        if (!ExistsName(baseName)) return baseName;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (!ExistsName(candidate)) return candidate;
        }
    }

    public IReadOnlyList<string> GetGroups()
    {
        foreach (var group in state.Database.Materials.Keys.Where(group => !state.Database.MaterialGroups.Contains(group)).ToList())
            state.Database.MaterialGroups.Add(group);
        return state.Database.MaterialGroups;
    }

    public IReadOnlyList<MaterialChoice> GetChoices(string? group) =>
        !string.IsNullOrWhiteSpace(group) && state.Database.Materials.TryGetValue(group, out var materials)
            ? materials.Select(material => new MaterialChoice(group, material)).ToList()
            : [];

    public void Add(string group, Material material)
    {
        Validate(material);
        if (material.CreatedAt == DateTimeOffset.MinValue) material.CreatedAt = DateTimeOffset.Now;
        group = string.IsNullOrWhiteSpace(group) ? "Прочее" : group.Trim();
        state.CommitMutation(() =>
        {
            state.Database.Materials.TryAdd(group, []);
            if (!state.Database.MaterialGroups.Contains(group)) state.Database.MaterialGroups.Add(group);
            state.Database.Materials[group].Add(material);
        });
    }

    public MaterialChoice Save(MaterialChoice choice, string group)
    {
        if (!state.Database.Materials.TryGetValue(choice.Type, out var sourceMaterials))
            throw new InvalidOperationException("Материал уже удалён или изменён в облаке.");
        var material = sourceMaterials.FirstOrDefault(item => item.Id == choice.Material.Id)
            ?? throw new InvalidOperationException("Материал уже удалён или изменён в облаке.");
        Validate(material, material);
        group = string.IsNullOrWhiteSpace(group) ? choice.Type : group.Trim();
        var changed = !string.Equals(group, choice.Type, StringComparison.Ordinal);
        var details = state.Database.Projects.SelectMany(project => project.Products)
            .Concat(state.Database.Catalog.Values.SelectMany(products => products)).SelectMany(product => product.Details)
            .Where(detail => detail.Type == choice.Type && detail.MaterialId == material.Id).ToList();
        state.CommitMutation(() =>
        {
            if (changed)
            {
                sourceMaterials.Remove(material);
                state.Database.Materials.TryAdd(group, []);
                if (!state.Database.MaterialGroups.Contains(group)) state.Database.MaterialGroups.Add(group);
                state.Database.Materials[group].Add(material);
                foreach (var detail in details) detail.Type = group;
            }
            foreach (var product in state.Database.Projects.SelectMany(project => project.Products)
                .Concat(state.Database.Catalog.Values.SelectMany(products => products)))
            {
                foreach (var edge in product.Details.SelectMany(detail => detail.EdgeBands)
                    .Where(edge => edge.Type == choice.Type && edge.MaterialId == material.Id)) edge.Type = group;
                EdgeBandingService.Refresh(state, product);
            }
        });
        return new MaterialChoice(group, material);
    }

    public void Delete(MaterialChoice choice)
    {
        if (IsUsed(choice)) throw new InvalidOperationException("Материал используется в проекте или каталоге.");
        if (!state.Database.Materials.TryGetValue(choice.Type, out var materials)) return;
        var index = materials.FindIndex(item => item.Id == choice.Material.Id);
        if (index < 0) return;
        state.CommitMutation(() => materials.RemoveAt(index));
    }
}
