using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class MaterialGroupingChecks
{
    public static void Run(Action<bool, string> check, string output)
    {
        var state = new AppState();
        var path = Path.Combine(output, "material-group-drag", "database.json");
        state.Load(path);
        var library = new MaterialService(state);
        var first = new Material { Name = "Drag first", Group = "A", Cost = 100 };
        var second = new Material { Name = "Drag second", Group = "A", Cost = 200 };
        var target = new Material { Name = "Drag target", Group = "B" };
        var foreign = new Material { Name = "Drag foreign", Group = "Other category" };
        library.Add("Drag category", first);
        library.Add("Drag category", second);
        library.Add("Drag category", target);
        library.Add("Foreign category", foreign);
        check(library.MoveToGroup("Drag category", [first.Id, second.Id], " b ") && first.Group == "B" && second.Group == "B",
            "Material drag moves multiple selected materials into an existing group within their category");
        check(state.Database.Materials["Drag category"].Contains(first) && first.Cost == 100 && second.Cost == 200 &&
            foreign.Group == "Other category", "Material grouping preserves identities, categories, prices and other categories");
        var revision = state.Revision;
        check(!library.MoveToGroup("Drag category", [first.Id], "B") && state.Revision == revision,
            "Dropping a material into its current group does not save or change revision");
        void RejectMove(string category, string[] ids, string group, string message)
        {
            var before = state.ExportJson();
            var rejected = false;
            try { library.MoveToGroup(category, ids, group); }
            catch (InvalidOperationException) { rejected = true; }
            check(rejected && before == state.ExportJson(), message);
        }
        RejectMove("Drag category", [first.Id, foreign.Id], "B", "Cross-category material drag is rejected without a partial move");
        RejectMove("Drag category", [first.Id, "deleted-id"], "B", "Deleted drag sources are rejected without a partial move");
        RejectMove("Drag category", [first.Id], "Other category", "A group in another category is not a valid drop target");
        RejectMove("Missing category", [first.Id], "B", "A deleted category cannot receive dragged materials");
        check(library.MoveToGroup("Drag category", [first.Id], "") && first.Group == "" && second.Group == "B",
            "Dropping into the ungrouped section clears only the selected material group");
        var restored = new AppState();
        restored.Load(path);
        check(restored.Database.Materials["Drag category"].Single(material => material.Id == first.Id).Group == "" &&
            restored.Database.Materials["Drag category"].Single(material => material.Id == second.Id).Group == "B",
            "Dragged material groups survive restart");
    }
}
