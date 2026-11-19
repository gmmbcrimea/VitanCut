using System.Text.Json;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class MaterialPickerHistoryChecks
{
    internal static void Run(Action<bool, string> check, string output)
    {
        var path = Path.Combine(output, "material-history.json");
        var history = new MaterialPickerHistory(path);
        var a = new MaterialChoice("Sheet", new Material { Id = "a", Name = "Alpha" });
        var b = new MaterialChoice("Sheet", new Material { Id = "b", Name = "Beta" });
        var z = new MaterialChoice("Sheet", new Material { Id = "z", Name = "Zeta" });
        MaterialChoice[] source = [z, a, b];
        check(history.Sort(source, "alphabetical").SequenceEqual([a, b, z]) &&
            history.Sort(source, "recent").SequenceEqual([a, b, z]),
            "Material choices default to alphabetical order including unused materials");
        history.Record(z);
        history.Record(b);
        check(history.Sort(source, "recent").SequenceEqual([b, z, a]) &&
            history.Sort(source, "alphabetical").SequenceEqual([a, b, z]),
            "Recent ordering follows successful selections without changing alphabetical ordering");
        check(history.Preferred([a, b, z]) == b, "Default choice uses the latest available material even with alphabetical list order");
        check(history.Preferred([a, b, z], a.Type, a.Material.Id) == a,
            "An existing edge assignment takes priority over a more recently used material");
        check(history.Preferred([a, z]) == z && history.Preferred([]) is null,
            "Default edge selection skips missing materials and handles an empty catalog");
        check(new MaterialPickerHistory(path).Preferred([a, b, z]) == b,
            "Default edge selection remembers the last use after restart");
        history.Record(z);
        check(new MaterialPickerHistory(path).Sort(source, "recent").SequenceEqual([z, b, a]),
            "Reusing a material moves it to the top and history survives restart");
        var sameIdEdge = new MaterialChoice("Edge", new Material { Id = "a", Name = "Another", Unit = "lm" });
        history.Record(sameIdEdge);
        check(history.Sort([a, sameIdEdge], "recent").SequenceEqual([sameIdEdge, a]),
            "Recent choices distinguish equal material IDs in different categories");
        history.Record(new MaterialChoice("Sheet", new Material { Id = "deleted", Name = "Deleted" }));
        check(history.Sort(source, "recent").SequenceEqual([z, b, a]),
            "Missing history entries do not reintroduce deleted catalog materials");
        File.WriteAllText(path, "invalid json");
        check(new MaterialPickerHistory(path).Sort(source, "recent").SequenceEqual([a, b, z]),
            "A damaged history falls back to alphabetical ordering without breaking the picker");
        var blocked = new MaterialPickerHistory(output);
        blocked.Record(z);
        check(blocked.Sort(source, "recent")[0] == z, "Unavailable history storage does not block a valid material selection");

        var state = new AppState();
        var db = Path.Combine(output, "material-order.json");
        state.Load(db);
        check(state.Database.Preferences.MaterialPickerOrder == "alphabetical", "Old databases use alphabetical material ordering");
        state.Database.Preferences.MaterialPickerOrder = "recent";
        state.Save();
        var reloaded = new AppState();
        reloaded.Load(db);
        check(reloaded.Database.Preferences.MaterialPickerOrder == "recent", "Material order preference survives restart");
        reloaded.ImportJson(JsonSerializer.Serialize(new Database()));
        check(reloaded.Database.Preferences.MaterialPickerOrder == "recent" &&
            CloudEntityMerge.Restore(CloudEntityMerge.Snapshot(new Database()), reloaded.Database.Preferences)
                .Preferences.MaterialPickerOrder == "recent",
            "Database import and cloud merge preserve local material ordering");
        reloaded.ReplaceFromCloudSnapshot(JsonSerializer.Serialize(new Database()));
        check(reloaded.Database.Preferences.MaterialPickerOrder == "recent",
            "Replacing a cloud snapshot preserves material picker preferences");
        var beforeHistory = reloaded.ExportJson();
        var revision = reloaded.Revision;
        var catalogOrder = reloaded.Database.Materials.SelectMany(group => group.Value.Select(material => material.Id)).ToArray();
        history.Record(z);
        history.Sort(reloaded.MaterialChoices(), "recent");
        check(reloaded.ExportJson() == beforeHistory && reloaded.Revision == revision &&
            reloaded.Database.Materials.SelectMany(group => group.Value.Select(material => material.Id)).SequenceEqual(catalogOrder),
            "Picker history and sorting never mutate the material catalog, project prices or database revision");
        var isolated = new MaterialPickerHistory(Path.Combine(output, "isolated-history.json"));
        check(isolated.Sort(source, "recent").SequenceEqual([a, b, z]), "Test history cannot inherit another data root's recent choices");
    }
}
