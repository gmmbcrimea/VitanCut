using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class LocalizationChecks
{
    public static void Run(Action<bool, string> check, string output)
    {
        var placeholders = new Regex(@"\{\d+(?:,[^}:]+)?(?::[^}]+)?\}");
        var expectedSave = new Dictionary<string, string> { ["en"] = "Save", ["de"] = "Speichern", ["es"] = "Guardar", ["zh-Hans"] = "保存" };
        var originalLanguage = UiText.Language;
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (var (language, expected) in expectedSave)
            {
                using var stream = typeof(UiText).Assembly.GetManifestResourceStream($"VitanCut.Localization.{language}.json");
                check(stream is not null, "Translation catalog is embedded and available offline: " + language);
                var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(stream!)!;
                check(catalog.Count >= 748 && catalog.Values.All(value => !string.IsNullOrWhiteSpace(value)), "Translation catalog is complete and nonempty: " + language);
                check(catalog.All(pair => placeholders.Matches(pair.Key).Select(match => match.Value).Order()
                    .SequenceEqual(placeholders.Matches(pair.Value).Select(match => match.Value).Order())),
                    "Translations preserve every format placeholder: " + language);
                UiText.Language = language;
                check(UiText.Translate("Перезапустить сейчас", language) != "Перезапустить сейчас"
                    && UiText.Translate("Язык сохранён. Изменение вступит в силу после перезапуска приложения.", language) != "Язык сохранён. Изменение вступит в силу после перезапуска приложения.",
                    "Restart action and confirmation are translated for the selected language: " + language);
                check(UiText.Get("Сохранить") == expected && UiText.Original(expected) == "Сохранить", "Language and command identity translate correctly: " + language);
                var value = "Имя пользователя — {not a format}";
                var formatted = UiText.Format($"{value} · длина");
                check(formatted.Contains(value), "Localized formula labels leave user values and braces untouched: " + language);
                var detail = new Detail { Type = "ЛДСП", LengthExpr = "длина изделия - 32", WidthExpr = "глубина изделия" };
                check(FormulaEvaluator.Evaluate(new Product { Length = 600 }, detail).Value == 568 && detail.LengthExpr == "длина изделия - 32",
                    "Interface language does not rewrite or change formulas: " + language);
                check(CultureInfo.CurrentCulture == originalCulture && UiText.Get("Custom user data untouched") == "Custom user data untouched",
                    "Interface language does not change numeric culture or unknown text: " + language);
            }
            check(UiText.Translate("Сохранить", "unsupported") == "Сохранить" && new AppPreferences().Language == "ru", "Russian remains the default and fallback interface language");
            UiText.Language = "ru";
            check(UiText.Translate("Перезапустить сейчас", "en") == "Restart now" && UiText.Language == "ru",
                "Selected-language confirmation does not change the currently active interface");
            var state = new AppState();
            var path = Path.Combine(output, "language-preferences.json");
            state.Load(path);
            state.Database.Preferences.Language = UiText.NormalizeLanguage("de");
            state.Save();
            var restarted = new AppState();
            restarted.Load(path);
            check(restarted.Database.Preferences.Language == "de", "Selected interface language survives restart");
            var imported = new Database();
            imported.Preferences.Language = "zh-Hans";
            state.ImportJson(JsonSerializer.Serialize(imported));
            check(state.Database.Preferences.Language == "de", "Database import preserves the local interface language");
            var remote = new Database();
            remote.Preferences.Language = "es";
            var rebuilt = CloudEntityMerge.Restore(CloudEntityMerge.Snapshot(remote), state.Database.Preferences);
            check(rebuilt.Preferences.Language == "de", "Cloud entity merge preserves the local interface language");
        }
        finally { UiText.Language = originalLanguage; }
    }
}
