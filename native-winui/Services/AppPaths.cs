namespace VitanCut.WinUI.Services;

public sealed class AppPaths
{
    private AppPaths(string applicationDirectory, string dataRoot, bool isDevelopment)
    {
        ApplicationDirectory = Path.GetFullPath(applicationDirectory);
        DataRoot = Path.GetFullPath(dataRoot);
        IsDevelopment = isDevelopment;
    }

    public string ApplicationDirectory { get; }
    public string DataRoot { get; }
    public bool IsDevelopment { get; }
    public string DatabasePath => Path.Combine(DataRoot, "database.json");
    public string WindowSessionPath => Path.Combine(DataRoot, "window-session.json");
    public string CloudSettingsPath => Path.Combine(DataRoot, "vitan-cut-cloud.json");
    public string CloudBaselinePath => Path.Combine(DataRoot, "vitan-cut-cloud-baseline.json");
    public string CloudSessionPath => Path.Combine(DataRoot, "vitan-cut-cloud-session.dat");
    public string LogsDirectory => Path.Combine(DataRoot, "logs");
    public string RollbackDirectory => Path.Combine(DataRoot, "rollback");
    public string TemporaryDirectory => Path.Combine(DataRoot, "temp");

    public static AppPaths ForCurrentProcess(string applicationDirectory)
    {
        var mode = Environment.GetEnvironmentVariable("VITANCUT_ENVIRONMENT");
        if (!string.Equals(mode, "Development", StringComparison.OrdinalIgnoreCase))
            return ForProduction(applicationDirectory);

        var root = Environment.GetEnvironmentVariable("VITANCUT_DATA_ROOT");
        if (string.IsNullOrWhiteSpace(root))
            throw new InvalidOperationException("Для режима разработки задайте отдельный VITANCUT_DATA_ROOT.");

        var fullRoot = Path.GetFullPath(root);
        var fullApplication = Path.GetFullPath(applicationDirectory);
        if (IsWithin(fullRoot, fullApplication) || IsWithin(fullApplication, fullRoot))
            throw new InvalidOperationException("Каталог тестовых данных должен находиться отдельно от файлов приложения.");

        return new AppPaths(fullApplication, fullRoot, isDevelopment: true);
    }

    public static AppPaths ForDatabaseFile(string databasePath, bool isDevelopment = true)
    {
        var fullPath = Path.GetFullPath(databasePath);
        var root = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("У базы данных отсутствует каталог.");
        return new AppPaths(AppContext.BaseDirectory, root, isDevelopment);
    }

    private static AppPaths ForProduction(string applicationDirectory)
    {
        var baseDirectory = Path.GetFullPath(applicationDirectory);
        var dataRoot = IsProtectedInstallationDirectory(baseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VitanCut", "data")
            : Path.Combine(baseDirectory, "data");
        return new AppPaths(baseDirectory, dataRoot, isDevelopment: false);
    }

    private static bool IsProtectedInstallationDirectory(string directory)
    {
        var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(path => !string.IsNullOrWhiteSpace(path)).Any(path =>
        {
            var root = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullDirectory.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool IsWithin(string candidate, string parent)
    {
        var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }
}
