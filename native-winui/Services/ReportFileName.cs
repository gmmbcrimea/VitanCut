using System.Globalization;

namespace VitanCut.WinUI.Services;

internal static class ReportFileName
{
    public static string Create(string reportName, string? projectName, DateTime? date = null,
        string? address = null, string? format = null)
    {
        var project = Safe(projectName);
        if (project.Length == 0) project = UiText.Get("Проект");
        var location = Safe(address);
        var stamp = (date ?? DateTime.Now).ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture);
        var selected = InterfaceOptions.FileNameFormat(format);
        if (selected is "project-address-date" or "project-address")
        {
            project = project[..Math.Min(project.Length, 90)];
            location = location[..Math.Min(location.Length, 90)];
        }
        var parts = selected switch
        {
            "project-address-date" => new[] { project, location, stamp },
            "address-date" => new[] { location.Length == 0 ? project : location, stamp },
            "project-address" => new[] { project, location },
            _ => new[] { project, stamp }
        };
        return reportName + "_" + string.Join("_", parts.Where(part => part.Length > 0));
    }

    private static string Safe(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
        var safeProjectName = string.Concat((value ?? "").Trim().Select(character =>
            char.IsControl(character) || invalid.Contains(character) ? '_' : character)).Trim(' ', '.');
        if (safeProjectName.Length > 120) safeProjectName = safeProjectName[..120].TrimEnd(' ', '.');
        return safeProjectName;
    }
}
