using System.Collections.ObjectModel;
using System.Collections.Specialized;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class ProductDetailEditorChecks
{
    internal static void Run(Action<bool, string> check, string output)
    {
        const double chrome = 114;
        var initial = MaterialPickerLayout.Place(1400, 1100, 100, 100, MaterialPickerLayout.RequiredHeight(3, chrome));
        check(initial == (356d, 258d, 108d, 108d), "Material picker opens at the exact height of its collapsed categories");
        var expanded = MaterialPickerLayout.Place(1400, 1100, 100, 100, MaterialPickerLayout.RequiredHeight(8, chrome));
        check(expanded.Height == 498 && expanded.Top == 108,
            "Material picker grows to fit an expanded group without moving if it still fits");
        var narrowed = MaterialPickerLayout.Place(1400, 1100, 100, 100, MaterialPickerLayout.RequiredHeight(1, chrome));
        check(narrowed.Height == 162 && MaterialPickerLayout.RequiredHeight(0, chrome) == 162 &&
            narrowed.Height < initial.Height, "Short or empty searches shrink the picker to a single row plus its controls");
        var limited = MaterialPickerLayout.Place(1000, 700, 980, 685, 3000);
        check(limited.Height == 684 && limited.Top == 8 && limited.Left + limited.Width == 972,
            "Material picker remains within both window edges when the results require scrolling");
        var oppositeCorner = MaterialPickerLayout.Place(1400, 1100, 1250, 1040, 200);
        check(oppositeCorner.Left == 886 && oppositeCorner.Top == 832,
            "Material picker opens above and to the left of a click near the bottom right");
        var differentCursor = MaterialPickerLayout.Place(1400, 1100, 300, 140, 200);
        check(differentCursor.Left == 308 && differentCursor.Top == 148,
            "Moving the cursor moves the picker without centering it in the editor");

        var path = Path.Combine(output, "product-detail-editor.json");
        var state = new AppState();
        state.Load(path);
        var sheet = new Material { Name = "Sheet", Unit = "m2", Cost = 100, EdgeBandingEnabled = true };
        var edge = new Material { Name = "Edge A", Unit = "lm", Cost = 20 };
        var nextEdge = new Material { Name = "Edge B", Unit = "lm", Cost = 30 };
        const string sheets = "\u041b\u0414\u0421\u041f";
        const string edges = "\u041a\u0440\u043e\u043c\u043a\u0430";
        state.CommitMutation(() =>
        {
            state.Database.Materials[sheets] = [sheet];
            state.Database.Materials[edges] = [edge, nextEdge];
        });
        var product = new Product { Details = Enumerable.Range(0, 120).Select(i => new Detail { Name = $"Part {i}" }).ToList() };
        var editor = new ProductDetailEditor(state, product);
        var rows = new ObservableCollection<Row>();
        var changes = new List<NotifyCollectionChangedAction>();
        rows.CollectionChanged += (_, args) => changes.Add(args.Action);
        void Sync() => editor.SyncRows(rows, row => row.Detail, detail => new Row(detail));

        Sync();
        var first = rows[0];
        var last = rows[^1];
        changes.Clear();
        var added = new Detail { Name = "Part 121", LengthExpr = "600", WidthExpr = "400" };
        product.Details.Add(added);
        Sync();
        check(rows.Count == 121 && rows[0] == first && rows[119] == last &&
            changes.SequenceEqual([NotifyCollectionChangedAction.Add]), "Adding row 121 preserves the first 120 editor rows");
        var addedRow = rows[^1];
        changes.Clear();
        var sheetChoice = new MaterialChoice(sheets, sheet);
        var applied = editor.ApplyMaterial(added, sheetChoice);
        Sync();
        check(applied.Status == MaterialEditStatus.Applied && ReferenceEquals(applied.Target, added) &&
            editor.MaterialFor(added)?.Material == sheet && rows[^1] == addedRow && changes.Count == 0 &&
            product.Details.Count(d => d.MaterialId == sheet.Id) == 1,
            "Applying material without a realized view updates only the target and does not reset rows");

        state.Database.Projects.Add(new Project { Products = [product] });
        state.Save();
        var restored = new AppState();
        restored.Load(path);
        check(restored.Database.Projects[0].Products[0].Details[^1].MaterialId == sheet.Id,
            "A material committed without a view survives save and reload");

        var obsolete = new Detail { Id = added.Id };
        check(editor.ApplyMaterial(obsolete, sheetChoice).Status == MaterialEditStatus.MissingDetail &&
            editor.ApplyMaterial(added, new MaterialChoice(sheets, new Material())).Status == MaterialEditStatus.MissingMaterial &&
            added.MaterialId == sheet.Id, "Retired detail instances and missing catalog choices cannot change the draft");

        EdgeBandingService.SetSide(added, "top", new MaterialChoice(edges, edge));
        EdgeBandingService.Refresh(state, product);
        Sync();
        var generated = product.Details.Single(detail => detail.IsGeneratedEdgeBand);
        var replacement = editor.ApplyMaterial(generated, new MaterialChoice(edges, nextEdge));
        Sync();
        check(replacement is { Status: MaterialEditStatus.Applied, Target: { } } &&
            replacement.Target.MaterialId == nextEdge.Id && ReferenceEquals(rows[^1].Detail, replacement.Target) &&
            ReferenceEquals(rows[120], addedRow) && added.EdgeBands.Single().Side == "top",
            "Replacing automatic edge banding returns the new focus target and preserves manual rows and selected edges");

        changes.Clear();
        product.Details.Remove(first.Detail);
        Sync();
        check(changes.SequenceEqual([NotifyCollectionChangedAction.Remove]) && rows[118] == last &&
            editor.ApplyMaterial(first.Detail, sheetChoice).Status == MaterialEditStatus.MissingDetail,
            "Deleting a row keeps surviving rows and rejects pending operations against the deleted detail");

        changes.Clear();
        product.Details.Remove(added);
        product.Details.Insert(0, added);
        Sync();
        check(changes.SequenceEqual([NotifyCollectionChangedAction.Move]) && rows[0] == addedRow,
            "Reordering moves the existing row rather than recreating its editor");

        var updated = new Material { Id = sheet.Id, Name = "Updated sheet", Unit = "m2" };
        state.CommitMutation(() => state.Database.Materials[sheets] = [updated]);
        check(editor.MaterialFor(added)?.Material == updated,
            "Editor material lookup refreshes when the catalog revision changes");

        var legMaterial = new Material { Name = "Adjustable leg", Unit = "pc", Group = LegDetails.Group };
        var otherHardware = new Material { Name = "Handle", Unit = "pc" };
        state.CommitMutation(() => state.Database.Materials["Hardware"] = [legMaterial, otherHardware]);
        var legChoice = new MaterialChoice("Hardware", legMaterial);
        var legDetail = LegDetails.Create(legChoice);
        product.Details.Add(legDetail);
        check(legDetail.IsGeneratedLeg && legDetail.Name == LegDetails.Name && legDetail.Qty == 4 &&
            legDetail.LengthExpr == LegDetails.HeightExpression, "Automatic legs retain their identity and height formula");
        check(editor.ApplyMaterial(legDetail, new MaterialChoice("Hardware", otherHardware)).Status ==
            MaterialEditStatus.InvalidLegMaterial && legDetail.MaterialId == legMaterial.Id,
            "Leg material enforcement rejects an unrelated catalog choice without changing the row");
        check(editor.ApplyMaterial(legDetail, legChoice).Status == MaterialEditStatus.Applied,
            "Automatic legs accept materials from the legs group");
        var legacy = new Detail { Name = LegDetails.Name, MaterialId = legMaterial.Id, Type = "Hardware",
            LengthExpr = LegDetails.HeightExpression, WidthExpr = "0" };
        var manual = new Detail { Name = "Manual leg", MaterialId = legMaterial.Id, Type = "Hardware" };
        var legacyProduct = new Product { HasLegs = true, Details = [legacy, manual] };
        LegDetails.RecognizeLegacy(state, legacyProduct);
        check(legacy.IsGeneratedLeg && !manual.IsGeneratedLeg,
            "Legacy automatic legs are recognized without marking ordinary manually added details");
        state.Save();
        var savedState = new AppState();
        savedState.Load(path);
        check(savedState.Database.Projects[0].Products[0].Details.Single(detail => detail.Id == legDetail.Id).IsGeneratedLeg,
            "The automatic leg marker survives database save and reload");

        var previewPath = Path.Combine(output, "product-detail-preview.json");
        var preview = new AppState();
        preview.Load(previewPath);
        var previewSheet = preview.Database.Materials[sheets].First();
        preview.Database.Materials[sheets].AddRange(Enumerable.Range(1, 101).Select(i => new Material
        {
            Name = $"Kronospan {i:000}", Group = "Kronospan", Unit = "m2", Cost = 1000 + i,
            SheetLength = 2750, SheetWidth = 1830
        }));
        preview.Database.Projects.Add(new Project
        {
            Name = "Product editor test (120 parts)",
            Counterparty = "Local test fixture",
            Products =
            [
                new Product
                {
                    Name = "Product editor test",
                    Length = 900, Depth = 500, Height = 750, Qty = 1,
                    Details = Enumerable.Range(1, 120).Select(i => new Detail
                    {
                        Name = $"Part {i:000}", Type = sheets, MaterialId = previewSheet.Id,
                        LengthExpr = "900", WidthExpr = "500", Qty = 1
                    }).ToList()
                }
            ]
        });
        preview.Save();
    }

    private sealed record Row(Detail Detail);
}
