namespace VitanCut.WinUI.Services;

internal static class UiTextSearch
{
    public static bool Matches(string query, params string[] labels) =>
        query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word => labels.Any(label => label.Contains(word, StringComparison.CurrentCultureIgnoreCase)));
}
