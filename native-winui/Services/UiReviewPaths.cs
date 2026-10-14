namespace VitanCut.WinUI.Services;

internal static class UiReviewPaths
{
    public static AppPaths Create(string applicationDirectory, string? requestedDataRoot = null)
    {
        var application = Path.GetFullPath(applicationDirectory);
        var workspace = FindWorkspace(application);
        var data = string.IsNullOrWhiteSpace(requestedDataRoot)
            ? Path.Combine(workspace, "ui-audit", "test-data")
            : Path.GetFullPath(requestedDataRoot, workspace);
        if (!Within(data, workspace) || Within(data, application) || Within(application, data))
            throw new InvalidOperationException("Данные UI-проверки должны храниться внутри Project-UI-Refactored, отдельно от файлов приложения.");
        RejectLinkedAncestors(application, workspace);
        RejectLinkedAncestors(data, workspace);
        if (new FileInfo(Path.Combine(data, "database.json")).LinkTarget is not null)
            throw new InvalidOperationException("UI-проверка не использует базу через символическую ссылку.");
        return AppPaths.ForDatabaseFile(Path.Combine(data, "database.json"), isDevelopment: true);
    }

    private static string FindWorkspace(string application)
    {
        for (var folder = new DirectoryInfo(application); folder is not null; folder = folder.Parent)
        {
            if (folder.Name.Equals("Project-UI-Refactored", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(Path.Combine(folder.FullName, "UI_REFACTOR_PLAN.md")) &&
                File.Exists(Path.Combine(folder.FullName, "UI_WORKSPACE.md"))) return folder.FullName;
        }
        throw new InvalidOperationException("Тестовая UI-сборка запускается только внутри Project-UI-Refactored.");
    }

    private static bool Within(string path, string parent) =>
        (Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);

    private static void RejectLinkedAncestors(string path, string workspace)
    {
        for (var folder = new DirectoryInfo(path); folder is not null && Within(folder.FullName, workspace); folder = folder.Parent)
            if (folder.LinkTarget is not null)
                throw new InvalidOperationException("UI-проверка не использует каталоги через символические ссылки.");
    }
}
