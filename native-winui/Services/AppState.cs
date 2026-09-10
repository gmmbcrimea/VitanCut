using System.Text.Encodings.Web;
using System.Text.Json;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public sealed class AppState
{
    private bool _canSave;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public Database Database { get; private set; } = new();
    public string DatabasePath { get; private set; } = "";

    public void Load(string? path = null)
    {
        _canSave = false;
        DatabasePath = path is null ? ResolveDatabasePath() : Path.GetFullPath(path);
        if (!File.Exists(DatabasePath))
        {
            Database = new Database();
            Normalize();
            _canSave = true;
            Save();
            return;
        }

        try
        {
            Database = ReadDatabase(File.ReadAllText(DatabasePath));
        }
        catch (Exception error) when (error is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"Не удалось открыть базу: {DatabasePath}. Исходный файл сохранён. {error.Message}", error);
        }

        Normalize();
        _canSave = true;
    }

    public void Save()
    {
        if (!_canSave) throw new InvalidOperationException("База не открыта. Сохранение отменено.");
        Normalize();
        WriteDatabase(DatabasePath, JsonSerializer.Serialize(Database, _jsonOptions), DatabasePath + ".bak");
    }

    public string CreateCloudSnapshot()
    {
        if (!_canSave) throw new InvalidOperationException("База не открыта.");
        Normalize();
        return JsonSerializer.Serialize(Database, _jsonOptions);
    }

    public void ReplaceFromCloudSnapshot(string json)
    {
        if (!_canSave) throw new InvalidOperationException("База не открыта.");
        var imported = ReadDatabase(json);
        Database = imported;
        Normalize();
        Save();
    }

    public string ExportJson()
    {
        Normalize();
        var options = new JsonSerializerOptions(_jsonOptions) { WriteIndented = true };
        return JsonSerializer.Serialize(Database, options);
    }

    public void ImportJson(string json)
    {
        var imported = ReadDatabase(json);
        var previous = Database;
        try
        {
            Database = imported;
            Normalize();
            Save();
        }
        catch
        {
            Database = previous;
            throw;
        }
    }

    private Database ReadDatabase(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.EnumerateObject().Any(property =>
                new[] { "projects", "materials", "catalog" }.Contains(property.Name, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("Файл не содержит базу проектов, материалов или каталога.");
        return JsonSerializer.Deserialize<Database>(json, _jsonOptions)
            ?? throw new InvalidDataException("Файл не похож на базу Vitan-Cut.");
    }

    public IEnumerable<MaterialChoice> MaterialChoices() =>
        Database.Materials.SelectMany(group => group.Value.Select(material => new MaterialChoice(group.Key, material)));

    public Material? FindMaterial(string type, string materialId)
    {
        return Database.Materials.TryGetValue(type, out var list)
            ? list.FirstOrDefault(item => item.Id == materialId)
            : null;
    }

    private void Normalize()
    {
        Database.Counterparties ??= ["Частный заказчик", "Дилер"];
        Database.MaterialGroups ??= ["ЛДСП", "Фурнитура", "Кромка", "Прочее"];
        Database.Materials ??= new Database().Materials;
        Database.Catalog ??= [];
        Database.Projects ??= [];
        Database.Preferences ??= new AppPreferences();
        Database.Preferences.Theme = Database.Preferences.Theme is "light" or "dark" or "auto" ? Database.Preferences.Theme : "auto";
        if (Database.Preferences.ThemeDefaultsVersion < 1)
        {
            Database.Preferences.Theme = "auto";
            Database.Preferences.SystemThemeDefaultMigrated = true;
            Database.Preferences.ThemeDefaultsVersion = 1;
        }
        Database.Preferences.CutTrim = Math.Max(0, Database.Preferences.CutTrim);
        Database.Preferences.CutGap = Math.Max(0, Database.Preferences.CutGap);
        Database.Preferences.CloudPublishIntervalMinutes = Database.Preferences.CloudPublishIntervalMinutes is 5 or 15 or 30 or 60
            ? Database.Preferences.CloudPublishIntervalMinutes
            : 15;

        foreach (var group in Database.MaterialGroups)
        {
            Database.Materials.TryAdd(group, []);
        }

        foreach (var project in Database.Projects)
        {
            project.Id = Empty(project.Id, Ids.NewId());
            if (project.CreatedAt == default) project.CreatedAt = DateTimeOffset.Now;
            if (project.UpdatedAt == default) project.UpdatedAt = project.CreatedAt;
            project.Products ??= [];
            project.Payrolls ??= [];
            project.CutLayout ??= [];
            project.CutPlans ??= [];
            project.AdditionalCutPlans ??= [];
            foreach (var plan in project.CutPlans) plan.Layout ??= [];
            foreach (var plan in project.AdditionalCutPlans) plan.Layout ??= [];
            foreach (var product in project.Products)
            {
                product.Id = Empty(product.Id, Ids.NewId());
                product.Qty = product.Qty <= 0 ? 1 : product.Qty;
                product.FixedSizes ??= [];
                product.Details ??= [];
                foreach (var detail in product.Details)
                {
                    detail.Id = Empty(detail.Id, Ids.NewId());
                    detail.Qty = detail.Qty <= 0 ? 1 : detail.Qty;
                }
            }
        }

        foreach (var product in Database.Catalog.Values.SelectMany(products => products))
        {
            product.Id = Empty(product.Id, Ids.NewId());
            product.FixedSizes ??= [];
            product.Details ??= [];
            foreach (var detail in product.Details) detail.Id = Empty(detail.Id, Ids.NewId());
        }

        // Missing JSON properties already use Detail.AllowRotation's default; preserve explicit false.
        Database.Preferences.RotationDefaultsMigrated = true;

        foreach (var material in Database.Materials.SelectMany(item => item.Value))
        {
            material.Id = Empty(material.Id, Ids.NewId());
            material.Unit = Empty(material.Unit, "pc");
        }
    }

    private static string Empty(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private void WriteDatabase(string path, string json, string? backupPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, json);
            _ = ReadDatabase(File.ReadAllText(temp));
            if (File.Exists(path)) File.Replace(temp, path, backupPath);
            else File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static string ResolveDatabasePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("VITANCUT_DATABASE_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) return Path.GetFullPath(overridePath);
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "data", "database.json"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "data", "database.json")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "dist", "win-unpacked", "data", "database.json")),
            Path.Combine(Environment.CurrentDirectory, "data", "database.json"),
            Path.Combine(Environment.CurrentDirectory, "dist", "win-unpacked", "data", "database.json")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[^2];
    }
}
