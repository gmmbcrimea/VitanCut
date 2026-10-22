using System.Text.Encodings.Web;
using System.Text.Json;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public sealed class AppState
{
    public AppState(AppPaths? paths = null) => Paths = paths;

    private bool _canSave;
    private long _revision;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public Database Database { get; private set; } = new();
    public string DatabasePath { get; private set; } = "";
    public AppPaths? Paths { get; private set; }
    public long Revision => Interlocked.Read(ref _revision);

    public void Load(string? path = null)
    {
        _canSave = false;
        if (path is null)
        {
            Paths ??= AppPaths.ForCurrentProcess(AppContext.BaseDirectory);
            DatabasePath = Paths.IsDevelopment ? Paths.DatabasePath : ResolveProductionDatabasePath();
            if (!Paths.IsDevelopment && IsProtectedInstallationDirectory(AppContext.BaseDirectory))
                MigrateInstalledData(Path.Combine(AppContext.BaseDirectory, "data"), Paths.DataRoot);
            if (!Paths.IsDevelopment && !string.Equals(Path.GetDirectoryName(DatabasePath), Paths.DataRoot, StringComparison.OrdinalIgnoreCase))
                Paths = AppPaths.ForDatabaseFile(DatabasePath, isDevelopment: false);
        }
        else
        {
            DatabasePath = Path.GetFullPath(path);
            Paths = AppPaths.ForDatabaseFile(DatabasePath);
        }
        AtomicJsonPairFile.Recover(Paths!.CloudSettingsPath + ".pair-journal.json",
            Paths.CloudSettingsPath, Paths.CloudBaselinePath, DatabasePath);
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
        Interlocked.Exchange(ref _revision, 0);
    }

    public void Save()
    {
        if (!_canSave) throw new InvalidOperationException("База не открыта. Сохранение отменено.");
        Normalize();
        WriteDatabase(DatabasePath, SerializeDatabase(Database), DatabasePath + ".bak");
        Interlocked.Increment(ref _revision);
    }

    public void CommitMutation(Action mutation)
    {
        if (!_canSave) throw new InvalidOperationException("База не открыта. Изменение отменено.");
        var previous = SerializeDatabase(Database);
        try
        {
            mutation();
            Save();
        }
        catch
        {
            Database = ReadDatabase(previous);
            Normalize();
            throw;
        }
    }

    public string CreateCloudSnapshot()
    {
        if (!_canSave) throw new InvalidOperationException("База не открыта.");
        return SerializeDatabase(Database);
    }

    public void ReplaceFromCloudSnapshot(string json, Action<string>? durableCommit = null)
    {
        if (!_canSave) throw new InvalidOperationException("База не открыта.");
        var imported = ReadDatabase(json);
        var previous = Database;
        var local = Database.Preferences;
        imported.Preferences ??= new AppPreferences();
        imported.Preferences.Theme = local.Theme;
        imported.Preferences.Language = local.Language;
        imported.Preferences.AccentColor = local.AccentColor;
        imported.Preferences.CompactMode = local.CompactMode;
        imported.Preferences.AcrylicBackgroundEnabled = local.AcrylicBackgroundEnabled;
        imported.Preferences.AnimationsEnabled = local.AnimationsEnabled;
        imported.Preferences.CursorRevealEnabled = local.CursorRevealEnabled;
        imported.Preferences.DetailingExportFormat = local.DetailingExportFormat;
        imported.Preferences.DetailingIncludeImages = local.DetailingIncludeImages;
        imported.Preferences.CloudPublishIntervalMinutes = local.CloudPublishIntervalMinutes;
        imported.Preferences.RotationDefaultsMigrated = local.RotationDefaultsMigrated;
        imported.Preferences.SystemThemeDefaultMigrated = local.SystemThemeDefaultMigrated;
        imported.Preferences.ThemeDefaultsVersion = local.ThemeDefaultsVersion;
        try
        {
            Database = imported;
            Normalize();
            if (durableCommit is null) Save();
            else
            {
                durableCommit(SerializeDatabase(Database));
                Interlocked.Increment(ref _revision);
            }
        }
        catch
        {
            Database = previous;
            Normalize();
            throw;
        }
    }

    public string ExportJson()
    {
        return SerializeDatabase(Database, indented: true);
    }

    public void ImportJson(string json)
    {
        var imported = ReadDatabase(json);
        imported.Preferences ??= new AppPreferences();
        imported.Preferences.Language = Database.Preferences.Language;
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

    private string SerializeDatabase(Database database, bool indented = false)
    {
        if (!indented) return JsonSerializer.Serialize(database, _jsonOptions);
        var options = new JsonSerializerOptions(_jsonOptions) { WriteIndented = true };
        return JsonSerializer.Serialize(database, options);
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
                    detail.EdgeBands ??= [];
                }
            }
        }

        foreach (var product in Database.Catalog.Values.SelectMany(products => products))
        {
            product.Id = Empty(product.Id, Ids.NewId());
            product.FixedSizes ??= [];
            product.Details ??= [];
            foreach (var detail in product.Details)
            {
                detail.Id = Empty(detail.Id, Ids.NewId());
                detail.EdgeBands ??= [];
            }
        }

        // Missing JSON properties already use Detail.AllowRotation's default; preserve explicit false.
        Database.Preferences.RotationDefaultsMigrated = true;

        foreach (var material in Database.Materials.SelectMany(item => item.Value))
        {
            material.Id = Empty(material.Id, Ids.NewId());
            material.Unit = Empty(material.Unit, "pc");
        }

        EnsureUnique(Database.Projects.Select(project => project.Id), "project");
        EnsureUnique(Database.Materials.SelectMany(group => group.Value).Select(material => material.Id), "material");
        EnsureUnique(Database.Catalog.Values.SelectMany(products => products).Select(product => product.Id), "catalog product");
        foreach (var project in Database.Projects)
        {
            EnsureUnique(project.Products.Select(product => product.Id), $"product in project '{project.Name}'");
            foreach (var product in project.Products)
                EnsureUnique(product.Details.Select(detail => detail.Id), $"detail in product '{product.Name}'");
        }
        foreach (var product in Database.Catalog.Values.SelectMany(products => products))
            EnsureUnique(product.Details.Select(detail => detail.Id), $"detail in product '{product.Name}'");
    }

    private static void EnsureUnique(IEnumerable<string> values, string kind)
    {
        if (values.GroupBy(value => value, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidDataException($"В базе обнаружены повторяющиеся идентификаторы: {kind}.");
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

    private static string ResolveProductionDatabasePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("VITANCUT_DATABASE_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) return Path.GetFullPath(overridePath);
        var baseDir = AppContext.BaseDirectory;
        if (IsProtectedInstallationDirectory(baseDir))
        {
            var applicationDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VitanCut", "data");
            MigrateInstalledData(Path.Combine(baseDir, "data"), applicationDataDirectory);
            return Path.Combine(applicationDataDirectory, "database.json");
        }
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

    private static bool IsProtectedInstallationDirectory(string directory)
    {
        var programFiles = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return programFiles.Where(path => !string.IsNullOrWhiteSpace(path)).Any(path =>
        {
            var fullProgramFiles = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullDirectory.StartsWith(fullProgramFiles, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static void MigrateInstalledData(string installedDataDirectory, string applicationDataDirectory)
    {
        if (Directory.Exists(applicationDataDirectory) || !Directory.Exists(installedDataDirectory)) return;
        try
        {
            foreach (var source in Directory.EnumerateFiles(installedDataDirectory, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(applicationDataDirectory, Path.GetRelativePath(installedDataDirectory, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: false);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException) { }
    }
}
