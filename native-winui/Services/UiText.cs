using System.Globalization;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VitanCut.WinUI.Services;

public static class UiText
{
    public static string Language { get; set; } = "ru";
    public static string NormalizeLanguage(string? language) => language is "en" or "de" or "es" or "zh-Hans" ? language : "ru";
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> Catalogs = new();
    private static readonly ConcurrentDictionary<string, List<(Regex Pattern, string Translation)>> Templates = new();
    private static readonly Regex Placeholder = new(@"\{\d+(?:,[^}:]+)?(?::[^}]+)?\}");

    public static string Get(string source) => Translate(source, Language);
    public static string Format(FormattableString source) => string.Format(CultureInfo.CurrentCulture,
        Translate(source.Format, Language), source.GetArguments());

    public static string Original(string text)
    {
        if (NormalizeLanguage(Language) == "ru") return text;
        return Catalog(Language).FirstOrDefault(pair => pair.Value == text).Key ?? text;
    }

    public static string Translate(string source, string language)
    {
        if (string.IsNullOrEmpty(source) || NormalizeLanguage(language) == "ru") return source;
        var catalog = Catalog(language);
        if (catalog.TryGetValue(source, out var translation)) return translation;
        if (source.Length > 8192) return source;
        // Service errors may reach dialogs after interpolation; preserve captured user values.
        foreach (var (pattern, translated) in TemplateCatalog(language))
        {
            Match match;
            try { match = pattern.Match(source); }
            catch (RegexMatchTimeoutException) { continue; }
            if (!match.Success) continue;
            var values = match.Groups.Cast<Group>().Skip(1).Select(group => (object)group.Value).ToArray();
            try { return string.Format(CultureInfo.CurrentCulture, translated, values); }
            catch (FormatException) { return source; }
        }
        return source;
    }

    private static Dictionary<string, string> Catalog(string language)
    {
        language = NormalizeLanguage(language);
        if (Catalogs.TryGetValue(language, out var catalog)) return catalog;
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream($"VitanCut.Localization.{language}.json");
        catalog = stream is null ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new();
        Catalogs.TryAdd(language, catalog);
        return catalog;
    }

    private static List<(Regex Pattern, string Translation)> TemplateCatalog(string language)
    {
        if (Templates.TryGetValue(language, out var result)) return result;
        result = new();
        foreach (var (key, translation) in Catalog(language).Where(pair => Placeholder.IsMatch(pair.Key)).OrderByDescending(pair => pair.Key.Length))
        {
            var parts = Placeholder.Split(key);
            var pattern = "^" + string.Join("(.*?)", parts.Select(Regex.Escape)) + "$";
            result.Add((new Regex(pattern, RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50)), translation));
        }
        Templates.TryAdd(language, result);
        return result;
    }
}
