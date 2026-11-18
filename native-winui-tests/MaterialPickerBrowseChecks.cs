using System.Collections.Specialized;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class MaterialPickerBrowseChecks
{
    internal static void Run(Action<bool, string> check)
    {
        const string sheets = "Sheets";
        const string edgeCategory = "\u041a\u0440\u043e\u043c\u043a\u0430";
        static MaterialPickerOption Entry(string category, string name, string group, string unit = "m2") =>
            new(new MaterialChoice(category, new Material { Name = name, Group = group, Unit = unit }), name, "unit / price");
        var sheetsInGroup = Enumerable.Range(1, 101).Select(i => Entry(sheets, $"Board {i:000}", "Kronospan")).ToArray();
        var edge = Entry(edgeCategory, "White edge", "", "lm");
        var ungrouped = Entry(sheets, "Ungrouped board", "");
        MaterialPickerOption[] options = [.. sheetsInGroup, ungrouped, edge, Entry(edgeCategory, "Not a linear edge", "", "pc")];
        var browser = new MaterialPickerBrowse(options, [sheets, "Empty"], "Add material", false, edgeCategory);
        var actions = new List<NotifyCollectionChangedAction>();
        ((INotifyCollectionChanged)browser.Rows).CollectionChanged += (_, args) => actions.Add(args.Action);
        var category = browser.Rows.Single(row => row.Label == sheets);
        check(browser.Rows.All(row => row.Key.Kind == MaterialPickerRowKind.Category && !row.Expanded),
            "Material picker opens with a single set of collapsed categories");
        browser.Toggle(category);
        var heading = browser.Rows.Single(row => row.Label == "Kronospan");
        check(ReferenceEquals(browser.Rows[browser.Rows.IndexOf(category) + 1], heading) &&
            browser.Rows.Any(row => row.Option == ungrouped) && browser.Rows.Any(row => row.Key.Category == sheets && row.Key.Kind == MaterialPickerRowKind.Add),
            "Expanding a category shows its groups, ungrouped materials and add action");
        actions.Clear();
        browser.Toggle(heading);
        var index = browser.Rows.IndexOf(heading);
        check(index >= 0 && browser.Rows.Skip(index + 1).Take(101).Select(row => row.Option).SequenceEqual(sheetsInGroup) &&
            actions.Count == 101 && actions.All(action => action == NotifyCollectionChangedAction.Add),
            "Expanding a long group inserts contiguous material rows immediately below the same header without reset");
        var listHeight = MaterialPickerLayout.ContentHeight(browser.Rows.Count, 114);
        check(listHeight == 114 + browser.Rows.Count * MaterialPickerLayout.RowHeight &&
            MaterialPickerLayout.Place(1400, 1000, 40, 80, listHeight).Height == 984,
            "Long lists are bounded by the editor without adding virtual space or changing row pitch");
        actions.Clear();
        browser.Toggle(heading);
        check(!heading.Expanded && browser.Rows[browser.Rows.IndexOf(heading) + 1].Option == ungrouped &&
            actions.Count == 101 && actions.All(action => action == NotifyCollectionChangedAction.Remove),
            "Collapsing a long group removes just its options, retaining the neighbouring row");
        browser.Filter("Kronospan 053");
        check(browser.MatchCount == 1 && browser.Rows.Where(row => row.Option is not null).Single().Option == sheetsInGroup[52],
            "Search matches group and material words together and opens their matching branch");
        browser.Filter("");
        check(category.Expanded && !heading.Expanded && browser.Rows.Contains(heading),
            "Clearing search restores the browsing expansion state");
        browser.Toggle(browser.Rows.Single(row => row.Label == "Empty"));
        check(browser.Rows.Any(row => row.Key is { Kind: MaterialPickerRowKind.Add, Category: "Empty" }),
            "Empty categories still allow creating a material");
        var edges = new MaterialPickerBrowse(options, [sheets], "Add material", true, edgeCategory);
        edges.Toggle(edges.Rows.Single());
        check(edges.Rows.Count == 2 && edges.Rows[1].Option == edge,
            "The generated edge picker shows only valid linear edge materials and no add action");
    }
}
