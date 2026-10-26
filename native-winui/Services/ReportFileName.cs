using System.Globalization;

namespace VitanCut.WinUI.Services;

internal static class ReportFileName
{
    public static string Create(string reportName, string? projectName, DateTime? date = null)
    {
        var invalid = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
        var safeProjectName = string.Concat((projectName ?? "").Trim().Select(character =>
            char.IsControl(character) || invalid.Contains(character) ? '_' : character)).Trim(' ', '.');
        if (safeProjectName.Length > 120) safeProjectName = safeProjectName[..120].TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(safeProjectName)) safeProjectName = UiText.Get("Проект");
        return $"{reportName}_{safeProjectName}_{(date ?? DateTime.Now).ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture)}";
    }
}
