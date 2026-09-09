using System.Text.Json;
using System.Text.Json.Serialization;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

/// <summary>
/// Splits a database into independently mergeable records. A project stays atomic so a
/// manually adjusted cut plan can never be silently combined with another layout.
/// </summary>
public static class CloudEntityMerge
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static IReadOnlyDictionary<CloudEntityKey, CloudEntity> Snapshot(Database database)
    {
        var result = new Dictionary<CloudEntityKey, CloudEntity>();
        Add(result, "cut-settings", "root", CutSettings.From(database.Preferences));
        Add(result, "counterparties", "root", database.Counterparties);
        Add(result, "material-groups", "root", database.MaterialGroups);

        foreach (var (group, materials) in database.Materials)
        foreach (var material in materials)
            Add(result, "material", material.Id, new MaterialEnvelope(group, material));

        foreach (var project in database.Projects)
            Add(result, "project", project.Id, project);

        foreach (var (counterparty, products) in database.Catalog)
        foreach (var product in products)
            Add(result, "catalog-product", product.Id, new CatalogProductEnvelope(counterparty, product));

        return result;
    }

    public static Database DeserializeDatabase(string payload) => Deserialize<Database>(payload);
    public static string SerializeDatabase(Database database) => JsonSerializer.Serialize(database, Json);

    public static Database Restore(IReadOnlyDictionary<CloudEntityKey, CloudEntity> entities, AppPreferences? localPreferences = null)
    {
        var preferences = localPreferences is null
            ? new AppPreferences()
            : JsonSerializer.Deserialize<AppPreferences>(JsonSerializer.Serialize(localPreferences, Json), Json)!;
        if (entities.TryGetValue(new CloudEntityKey("cut-settings", "root"), out var cutSettings))
            CutSettings.Apply(preferences, Deserialize<CutSettings>(cutSettings.Payload));

        var database = new Database
        {
            Counterparties = ReadOrDefault<List<string>>(entities, "counterparties", "root", ["Частный заказчик", "Дилер"]),
            MaterialGroups = ReadOrDefault<List<string>>(entities, "material-groups", "root", ["ЛДСП", "Фурнитура", "Кромка", "Прочее"]),
            Preferences = preferences,
            Materials = [],
            Catalog = [],
            Projects = []
        };

        foreach (var entity in entities.Values.Where(entity => entity.Key.Type == "material"))
        {
            var envelope = Deserialize<MaterialEnvelope>(entity.Payload);
            database.Materials.TryAdd(envelope.Group, []);
            database.Materials[envelope.Group].Add(envelope.Material);
            if (!database.MaterialGroups.Contains(envelope.Group)) database.MaterialGroups.Add(envelope.Group);
        }

        foreach (var entity in entities.Values.Where(entity => entity.Key.Type == "project"))
            database.Projects.Add(Deserialize<Project>(entity.Payload));

        foreach (var entity in entities.Values.Where(entity => entity.Key.Type == "catalog-product"))
        {
            var envelope = Deserialize<CatalogProductEnvelope>(entity.Payload);
            database.Catalog.TryAdd(envelope.Counterparty, []);
            database.Catalog[envelope.Counterparty].Add(envelope.Product);
            if (!database.Counterparties.Contains(envelope.Counterparty, StringComparer.CurrentCultureIgnoreCase))
                database.Counterparties.Add(envelope.Counterparty);
        }

        return database;
    }

    public static CloudEntityMergeResult Merge(Database baseline, Database local, Database remote)
    {
        var baseMap = Snapshot(baseline);
        var localMap = Snapshot(local);
        var remoteMap = Snapshot(remote);
        var merged = new Dictionary<CloudEntityKey, CloudEntity>();
        var conflicts = new List<CloudEntityConflict>();

        foreach (var key in baseMap.Keys.Union(localMap.Keys).Union(remoteMap.Keys))
        {
            baseMap.TryGetValue(key, out var baselineEntity);
            localMap.TryGetValue(key, out var localEntity);
            remoteMap.TryGetValue(key, out var remoteEntity);
            var localChanged = !Same(baselineEntity, localEntity);
            var remoteChanged = !Same(baselineEntity, remoteEntity);

            if (localChanged && remoteChanged && !Same(localEntity, remoteEntity))
            {
                conflicts.Add(new CloudEntityConflict(key, localEntity, remoteEntity));
                if (localEntity is not null) merged[key] = localEntity;
                continue;
            }

            var selected = localChanged ? localEntity : remoteChanged ? remoteEntity : baselineEntity;
            if (selected is not null) merged[key] = selected;
        }

        return new CloudEntityMergeResult(Restore(merged, local.Preferences), conflicts);
    }

    private static void Add(IDictionary<CloudEntityKey, CloudEntity> target, string type, string id, object payload)
    {
        var key = new CloudEntityKey(type, id);
        target[key] = new CloudEntity(key, JsonSerializer.Serialize(payload, Json));
    }

    private static bool Same(CloudEntity? left, CloudEntity? right) =>
        left is null ? right is null : right is not null && string.Equals(left.Payload, right.Payload, StringComparison.Ordinal);

    private static T ReadOrDefault<T>(IReadOnlyDictionary<CloudEntityKey, CloudEntity> entities, string type, string id, T fallback)
        => entities.TryGetValue(new CloudEntityKey(type, id), out var entity) ? Deserialize<T>(entity.Payload) : fallback;

    private static T Deserialize<T>(string payload) => JsonSerializer.Deserialize<T>(payload, Json)
        ?? throw new InvalidDataException("Облачная запись имеет неверный формат.");

    private sealed record MaterialEnvelope(string Group, Material Material);
    private sealed record CatalogProductEnvelope(string Counterparty, Product Product);
}

public sealed record CutSettings(string Palette, string ExportFormat, double Trim, double Gap, double UsefulRemainder)
{
    public static CutSettings From(AppPreferences preferences) => new(
        preferences.CutPalette,
        preferences.CutExportFormat,
        preferences.CutTrim,
        preferences.CutGap,
        preferences.CutUsefulRemainder);

    public static void Apply(AppPreferences preferences, CutSettings settings)
    {
        preferences.CutPalette = settings.Palette;
        preferences.CutExportFormat = settings.ExportFormat;
        preferences.CutTrim = settings.Trim;
        preferences.CutGap = settings.Gap;
        preferences.CutUsefulRemainder = settings.UsefulRemainder;
    }
}

public sealed record CloudEntityKey(string Type, string Id);
public sealed record CloudEntity(CloudEntityKey Key, string Payload);
public sealed record CloudEntityConflict(CloudEntityKey Key, CloudEntity? Local, CloudEntity? Remote)
{
    public string DisplayName => Key.Type switch
    {
        "project" => "Проект",
        "material" => "Материал",
        "catalog-product" => "Изделие каталога",
        "cut-settings" => "Настройки раскроя",
        "counterparties" => "Список заказчиков",
        "material-groups" => "Категории материалов",
        _ => Key.Type
    };
}

public sealed record CloudEntityMergeResult(Database Database, IReadOnlyList<CloudEntityConflict> Conflicts);
