using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class CutWorkflowChecks
{
    public static void Run(Action<bool, string> check, string output)
    {
        CutPlacement Part(string id, double x, double y, double w, double h, bool rotate = true) =>
            new("product", id, "Изделие", id, 1, id, w, h, x, y, w, h, false, rotate);
        CutReport Report(params CutSheet[] sheets) => new("Test", "", "", [new("Sheet", false, 620, 420, sheets.ToList(), "material")], [], 10, 0);
        CutSheet Sheet(int number, params CutPlacement[] parts) => new(number, 620, 420, 0, parts.ToList());
        for (var count = 0; count <= 6; count++)
        {
            var parts = Enumerable.Range(0, count).Select(i => Part($"small-{i}", 10 + i * 70, 10, 60, 80)).ToArray();
            var small = Report(Sheet(1, parts));
            var results = CutOptimization.Calculate(small);
            check(results.Count == 4 && results.All(v =>
                v.Report.Groups.SelectMany(g => g.Sheets).SelectMany(s => s.Placements).Select(p => p.InstanceId).Order()
                    .SequenceEqual(parts.Select(p => p.InstanceId).Order()) &&
                v.Report.Groups.SelectMany(g => g.Sheets).All(s => CutEditing.Valid(s.Placements, 620, 420, 10, 0))),
                $"Recursive search handles {count} parts without out-of-range access or lost parts");
        }
        var original = Report(Sheet(1, Part("a", 10, 10, 200, 400), Part("b", 210, 10, 200, 400), Part("c", 410, 10, 200, 400)),
            Sheet(2, Part("marker", 10, 10, 100, 100)));
        original = original with
        {
            Groups = original.Groups.Select(group => group with
            {
                Sheets = group.Sheets.Select(sheet => sheet with
                {
                    Placements = sheet.Placements.Select((part, index) => part with { DisplayNumber = index + 11 }).ToList()
                }).ToList()
            }).ToList()
        };
        original = CutEditing.Renumber(original);
        var ids = new HashSet<string> { "a", "b", "c" };
        var rotated = CutEditing.Rotate(original, ids);
        check(rotated.Success && rotated.NewSheets == 1, "Rotating a full sheet proposes one overflow sheet");
        check(original.Groups[0].Sheets.Count == 2 && original.Groups[0].Sheets[0].Placements.All(p => !p.Rotated), "Rotation proposal leaves original intact until confirmation");
        check(rotated.Report.Groups[0].Sheets[2].Placements.Single().InstanceId == "marker", "New sheet is inserted immediately after the affected sheet");
        check(rotated.Report.Groups[0].Sheets.SelectMany(s => s.Placements).Where(p => ids.Contains(p.InstanceId)).All(p => p.Rotated), "All selected parts rotate including overflow");
        check(rotated.Report.Groups[0].Sheets.All(s => CutEditing.Valid(s.Placements, 620, 420, 10, 0)), "Rotation proposal has no overlaps and stays within trim");
        check(rotated.Report.Groups[0].Sheets.SelectMany(s => s.Placements)
                .All(p => original.Groups[0].Sheets.SelectMany(s => s.Placements).Single(source => source.InstanceId == p.InstanceId).DisplayNumber == p.DisplayNumber),
            "Rotation preserves the displayed number of every detail instance");
        var locked = Report(Sheet(1, Part("locked", 10, 10, 100, 100, false)));
        check(!CutEditing.Rotate(locked, new HashSet<string> { "locked" }).Success, "Group rotation cannot bypass a rotation lock");
        var oversized = Report(Sheet(1, Part("wide", 10, 10, 500, 100)));
        check(!CutEditing.Rotate(oversized, new HashSet<string> { "wide" }).Success, "Oversized rotation is rejected instead of adding useless sheets");
        var nearest = CutEditing.Nearest([Part("moving", 100, 100, 50, 50)], [Part("obstacle", 0, 0, 200, 200)], 500, 500, 0, 10);
        check(nearest is { } n && Math.Abs(n.X * n.X + n.Y * n.Y - 12100) < .001, "Overlapping drop moves to the closest legal position, not the drag origin");
        var snap = CutEditing.Snap(Part("snap", 103, 107, 50, 50), [Part("other", 100, 100, 80, 80)], 500, 500, 0, 10);
        check(snap == (100, 100), "Snapping includes matching neighbour corners");
        var groupMove = Report(Sheet(1, Part("left", 10, 10, 80, 80), Part("right", 100, 20, 60, 60)), Sheet(2));
        var moved = CutEditing.Move(groupMove, new HashSet<string> { "left", "right" }, "material", 2, "left", 250, 200);
        var movedParts = moved.Report.Groups[0].Sheets[1].Placements;
        check(moved.Success && moved.NewSheets == 0 && movedParts.Count == 2 && groupMove.Groups[0].Sheets[0].Placements.Count == 2,
            "Cross-sheet group transfer is transactional");
        check(movedParts[1].X - movedParts[0].X == 90 && movedParts[1].Y - movedParts[0].Y == 10, "Group transfer preserves relative positions when they fit");
        check(!CutEditing.Move(groupMove, new HashSet<string> { "left" }, "wrong", 2, "left", 10, 10).Success, "Transfer cannot change material");
        var emptied = CutEditing.Move(groupMove, new HashSet<string> { "left", "right" }, "material", 2, "left", 250, 200);
        var emptySource = emptied.Report.Groups[0].Sheets[0];
        check(emptied.Success && emptySource.Placements.Count == 0, "Moving every detail can leave an empty source sheet for confirmation");
        var withoutEmpty = CutEditing.RemoveEmptySheets(emptied.Report, [new CutSheetReference("material", 1)]);
        check(withoutEmpty.Groups[0].Sheets.Count == 1 && withoutEmpty.Groups[0].Sheets.Single().Number == 1 && withoutEmpty.Groups[0].Sheets.Single().Placements.Count == 2,
            "Confirmed deletion removes only the empty source sheet and renumbers the map");
        var outsideDrop = CutEditing.MoveToNewSheet(groupMove, new HashSet<string> { "left", "right" }, "material", 1, "left");
        check(outsideDrop.Success && outsideDrop.NewSheets == 1 && outsideDrop.Report.Groups[0].Sheets.Count == 3 &&
                outsideDrop.Report.Groups[0].Sheets[1].Placements.Select(part => part.InstanceId).Order().SequenceEqual(["left", "right"]),
            "Dropping outside a sheet proposes a new sheet immediately after the source");
        var unordered = Report(Sheet(1,
            Part("bottom", 10, 210, 180, 100),
            Part("right", 310, 10, 120, 180) with { Rotated = true, Length = 120, Width = 180 },
            Part("middle", 200, 10, 100, 100)));
        var ordered = CutEditing.OrderSheet(unordered, "material", 1);
        check(ordered.Success && CutEditing.Valid(ordered.Report.Groups[0].Sheets[0].Placements, 620, 420, 10, 0) &&
            ordered.Report.Groups[0].Sheets[0].Placements.Min(part => part.X) == 10 && ordered.Report.Groups[0].Sheets[0].Placements.Min(part => part.Y) == 10 &&
            ordered.Report.Groups[0].Sheets[0].Placements.All(part => unordered.Groups[0].Sheets[0].Placements.Single(before => before.InstanceId == part.InstanceId).Rotated == part.Rotated),
            "Ordering a sheet compacts from the top-left without changing orientations");
        var isolatedOrder = Report(
            Sheet(1, Part("first", 300, 200, 100, 80), Part("second", 10, 10, 120, 60) with { Rotated = true, Length = 60, Width = 120 }),
            Sheet(2, Part("untouched", 240, 180, 100, 100)));
        var isolatedOrdered = CutEditing.OrderSheet(isolatedOrder, "material", 1);
        check(isolatedOrdered.Success &&
            isolatedOrdered.Report.Groups[0].Sheets[0].Placements.All(part => isolatedOrder.Groups[0].Sheets[0].Placements.Single(before => before.InstanceId == part.InstanceId).Rotated == part.Rotated) &&
            isolatedOrdered.Report.Groups[0].Sheets[1].Placements.SequenceEqual(isolatedOrder.Groups[0].Sheets[1].Placements),
            "Ordering recalculates only the selected sheet and preserves each part orientation");
        var crowded = Report(Sheet(1,
            Part("large", 450, 140, 400, 240),
            Part("small-a", 10, 10, 80, 60), Part("small-b", 100, 10, 80, 60), Part("small-c", 190, 10, 80, 60),
            Part("small-d", 10, 80, 80, 60), Part("small-e", 100, 80, 80, 60), Part("small-f", 190, 80, 80, 60)));
        var crowdedOrdered = CutEditing.OrderSheet(crowded, "material", 1);
        var large = crowdedOrdered.Report.Groups[0].Sheets[0].Placements.Single(part => part.InstanceId == "large");
        check(crowdedOrdered.Success && large.X <= 170 && large.Y == 10 &&
            crowdedOrdered.Report.Groups[0].Sheets[0].Placements.All(part => crowded.Groups[0].Sheets[0].Placements.Single(before => before.InstanceId == part.InstanceId).Rotated == part.Rotated),
            "Ordering compares dense layouts containing large and small details without changing orientations");
        var overflowMove = CutEditing.Move(original, new HashSet<string> { "marker" }, "material", 1, "marker", 10, 10);
        check(overflowMove.Success && overflowMove.NewSheets == 1 && overflowMove.Report.Groups[0].Sheets[1].Placements.Single().InstanceId == "marker",
            "Dropping onto a full sheet proposes the immediately following sheet");
        var signature = CutPlanService.Signature(original);
        var saved = CutPlanService.Capture(rotated.Report, signature, "Manual", "User");
        var restored = CutPlanService.Restore(original, saved);
        check(restored is not null && restored.Groups[0].Sheets.Count == 3 && restored.Groups[0].Sheets[2].Placements.Single().InstanceId == "marker",
            "Saved layout restores sheet membership and insertion order");
        var corrupt = CutPlanService.Copy(saved); corrupt.Layout.RemoveAt(0);
        check(CutPlanService.Restore(original, corrupt) is null, "Saved variants cannot lose a detail");
        corrupt = CutPlanService.Copy(saved); corrupt.Layout[0].Length = 1;
        check(CutPlanService.Restore(original, corrupt) is null, "Saved variants cannot shrink a detail");
        corrupt = CutPlanService.Copy(saved); corrupt.Layout[0].X = double.NaN;
        check(CutPlanService.Restore(original, corrupt) is null, "Saved variants reject nonfinite positions");
        check(CutPlanService.Restore(original with { Gap = 6 }, saved) is null, "Changed kerf invalidates saved variants without deleting them");
        var variants = CutOptimization.Calculate(original);
        check(variants.Count == 4 && variants.Any(v => v.Algorithm == "MaxRects") && variants.Any(v => v.Algorithm == "Гильотинный"), "Four results include both packing families");
        var variantCuts = variants.Select(v => CuttingMetrics.ForSheets(v.Report.Groups.SelectMany(group => group.Sheets), v.Report.Trim).Count).ToList();
        check(variantCuts.SequenceEqual(variantCuts.Order()), "Best result is selected by cut count first");
        check(variantCuts[0] <= CuttingMetrics.ForSheets(original.Groups.SelectMany(group => group.Sheets), original.Trim).Count, "Optimization never loses the baseline cut-count result");
        foreach (var variant in variants)
        {
            var parts = variant.Report.Groups.SelectMany(g => g.Sheets).SelectMany(s => s.Placements).ToList();
            check(parts.Count == 4 && parts.Select(p => p.InstanceId).Distinct().Count() == 4 && variant.Report.Groups[0].Sheets.All(s => CutEditing.Valid(s.Placements, 620, 420, 10, 0)), "Valid complete variant: " + variant.Name);
        }
        var fractional = Report(Sheet(1, Part("fraction", 10, 10, 599.99, 399.99, false)));
        // A crowded earlier sheet has another instance of the part visible on a sparse sheet.
        var sameFirst = Part("same-first", 10, 10, 400, 120) with { DetailId = "same" };
        var sameLast = Part("same-last", 10, 10, 400, 120) with { DetailId = "same" };
        var scoped = Report(Sheet(1, sameFirst, Part("block-bottom", 10, 136, 600, 274), Part("block-right", 416, 10, 194, 120)), Sheet(2, sameLast)) with { Gap = 6 };
        scoped = CutEditing.Renumber(scoped);
        var localIds = CutEditing.IdenticalIds(scoped, "same-last");
        var localTurn = CutEditing.Rotate(scoped, localIds);
        check(localIds.SetEquals(["same-last"]) && localTurn.Success && localTurn.NewSheets == 0 &&
            localTurn.Report.Groups[0].Sheets[0].Placements.SequenceEqual(scoped.Groups[0].Sheets[0].Placements),
            "Sheet-local rotation ignores identical parts on other crowded sheets");
        var globalTurn = CutEditing.Rotate(scoped, CutEditing.IdenticalIds(scoped, "same-last", allSheets: true));
        check(globalTurn.Success && globalTurn.NewSheets == 1 && globalTurn.SheetSummary.Contains("лист 1"),
            "Explicit all-sheet rotation reports which original sheet needs overflow");
        var sparseParts = new List<CutPlacement>
        {
            Part("fixed-a", 10, 10, 382, 130), Part("fixed-b", 10, 146, 382, 130),
            Part("fixed-c", 416, 262, 374, 120), Part("fixed-d", 416, 388, 374, 120),
            Part("fixed-e", 416, 514, 374, 70), Part("fixed-f", 416, 590, 412, 60)
        };
        sparseParts.AddRange(Enumerable.Range(0, 8).Select(i => Part($"row-{i}", 10, 912 + i * 126, 374, 120) with { DetailId = "row" }));
        var sparse = new CutReport("Sparse", "", "", [new("Sheet", false, 2800, 2070, [new(3, 2800, 2070, 0, sparseParts)], "sparse")], [], 10, 6);
        var rowIds = CutEditing.IdenticalIds(sparse, "row-0");
        var rowTurn = CutEditing.Rotate(sparse, rowIds);
        var rows = rowTurn.Report.Groups[0].Sheets.SelectMany(s => s.Placements).Where(p => rowIds.Contains(p.InstanceId)).ToList();
        check(rowTurn.Success && rowTurn.NewSheets == 0 && rows.Count == 8 && rows.All(p => p.Rotated) &&
            rows.GroupBy(p => Math.Round(p.Y, 6)).Count() <= 3 &&
            rows.GroupBy(p => Math.Round(p.Y, 6)).All(row => row.Count() < 2 || row.OrderBy(p => p.X).Zip(row.OrderBy(p => p.X).Skip(1))
                .All(pair => Math.Abs(pair.Second.X - pair.First.X - pair.First.Length - 6) < .00001)),
            "Group rotation aligns equal parts into rows instead of a staircase");
        check(rowTurn.Report.Groups[0].Sheets.All(s => CutEditing.Valid(s.Placements, 2800, 2070, 10, 6)) &&
            rowTurn.Report.Groups[0].Sheets.SelectMany(s => s.Placements).Where(p => !rowIds.Contains(p.InstanceId))
                .SequenceEqual(sparseParts.Where(p => !rowIds.Contains(p.InstanceId))),
            "Aligned rotation respects kerf and keeps unselected parts and their numbers in place");
        var smallPositions = new (double X, double Y)[] { (10, 282), (398, 10), (398, 136), (10, 408), (10, 534), (10, 660), (10, 786) };
        var smallRow = smallPositions.Select((p, i) => Part($"small-row-{i}", p.X, p.Y, 400, 120) with { DetailId = "small-row" }).ToList();
        var populated = sparse with { Groups = [sparse.Groups[0] with { Sheets = [new(3, 2800, 2070, 0, sparseParts.Concat(smallRow).ToList())] }] };
        check(smallRow.All(p => CutEditing.Rotate(populated, new HashSet<string> { p.InstanceId }) is { Success: true, NewSheets: 0 }),
            "Each small part rotates into the existing free area without an extra sheet");
        var smallGroupTurn = CutEditing.Rotate(populated, CutEditing.IdenticalIds(populated, "small-row-0"));
        check(smallGroupTurn.Success && smallGroupTurn.NewSheets == 0 && smallGroupTurn.Report.Groups[0].Sheets
            .All(s => CutEditing.Valid(s.Placements, 2800, 2070, 10, 6)),
            "Seven small parts rotate together on the sparse sheet without overflow");
        check(CutOptimization.Calculate(fractional).All(v => v.Report.Groups[0].Sheets.SelectMany(s => s.Placements).Single() is { Rotated: false, Length: 599.99 }), "Quantization fallback preserves exact dimensions and rotation locks");
        var random = new Random(1729);
        for (var sample = 0; sample < 8; sample++)
        {
            var sheets = Enumerable.Range(0, 16).Select(i => Sheet(i + 1,
                Part($"{sample}-{i}", 10, 10, random.Next(40, 560), random.Next(30, 370), i % 3 != 0))).ToArray();
            var input = Report(sheets) with { Gap = 4 };
            var textured = input.Groups[0] with { TextureDirection = true, MaterialId = "texture",
                Sheets = sheets.Select(s => s with { Placements = s.Placements.Select(p => p with { InstanceId = "texture-" + p.InstanceId }).ToList() }).ToList() };
            input = input with { Groups = [input.Groups[0], textured] };
            var complete = CutOptimization.Calculate(input).All(v => v.Report.Groups.All(g =>
                g.Sheets.All(s => CutEditing.Valid(s.Placements, 620, 420, 10, 4)) &&
                g.Sheets.SelectMany(s => s.Placements).Select(p => p.InstanceId).Order().SequenceEqual(
                    input.Groups.First(x => x.MaterialId == g.MaterialId).Sheets.SelectMany(s => s.Placements).Select(p => p.InstanceId).Order()) &&
                g.Sheets.SelectMany(s => s.Placements).All(p => !p.Rotated || p.AllowRotation && !g.TextureDirection)));
            check(complete, $"Random multi-material packing preserves parts, kerf and texture: {sample}");
        }
        var twice = Report(original.Groups[0].Sheets[0], Sheet(2,
            Part("d", 10, 10, 200, 400), Part("e", 210, 10, 200, 400), Part("f", 410, 10, 200, 400)),
            Sheet(3, Part("marker", 10, 10, 100, 100)));
        var doubleRotation = CutEditing.Rotate(twice, new HashSet<string> { "a", "b", "c", "d", "e", "f" });
        check(doubleRotation.Success && doubleRotation.NewSheets == 2 && doubleRotation.Report.Groups[0].Sheets.Count == 5 &&
            doubleRotation.Report.Groups[0].Sheets[1].Placements.All(p => ids.Contains(p.InstanceId)) &&
            doubleRotation.Report.Groups[0].Sheets[3].Placements.All(p => !ids.Contains(p.InstanceId)) &&
            doubleRotation.Report.Groups[0].Sheets[4].Placements.Single().InstanceId == "marker",
            "Rotating multiple source sheets inserts overflow after each original source");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { CutOptimization.Calculate(original, cancelled.Token); check(false, "Optimization supports cancellation"); }
        catch (OperationCanceledException) { check(true, "Optimization supports cancellation"); }
        var database = new AppState(); database.Load(Path.Combine(output, "cut-variants.json"));
        var project = new Project { CutPlans = [saved], ActiveCutPlanId = saved.Id };
        database.Database.Projects.Add(project); database.Save(); database.Load(database.DatabasePath);
        check(database.Database.Projects[0].ActiveCutPlanId == saved.Id && CutPlanService.Restore(original, database.Database.Projects[0].CutPlans.Single()) is not null,
            "Custom variants and active selection survive database restart");
        ReportDocumentService.SaveXlsx(rotated.Report, Path.Combine(output, "cut-variant-rotated.xlsx"));
        using var book = new ClosedXML.Excel.XLWorkbook(Path.Combine(output, "cut-variant-rotated.xlsx"));
        check(book.Worksheets.Count == 3 && book.Worksheets.All(s => s.Pictures.Count == 1), "Export follows the edited variant including inserted sheets");
        var ui = new AppState(); ui.Load(Path.Combine(output, "cut-ui.json"));
        ui.Database.Preferences.Theme = "dark"; ui.Database.Preferences.CutGap = 0; ui.Database.Preferences.CutTrim = 10;
        var material = new Material { Name = "Тестовый лист", Unit = "m2", SheetLength = 620, SheetWidth = 420 };
        ui.Database.Materials = new() { ["Листы"] = [material] };
        var uiProject = new Project { Name = "Проверка группового раскроя", Products = [new Product { Name = "Тест",
            Details = [new Detail { Name = "Бок", Type = "Листы", MaterialId = material.Id, Qty = 3, LengthExpr = "200", WidthExpr = "400", AllowRotation = true },
                new Detail { Name = "Маркер", Type = "Листы", MaterialId = material.Id, Qty = 1, LengthExpr = "100", WidthExpr = "100", AllowRotation = true }] }] };
        ui.Database.Projects = [uiProject]; ui.Save();
        var baseline = CuttingService.BuildCutReport(ui, uiProject, false);
        var plan = CutPlanService.Capture(baseline, CutPlanService.Signature(baseline), "Saved", "Test");
        var service = new ProjectService(ui);
        service.SaveCutPlans(uiProject, [plan], plan.Id, plan.InputSignature);
        check(CuttingService.BuildCutReport(ui, uiProject).Groups[0].Sheets.Count == baseline.Groups[0].Sheets.Count, "Active project variant is used by report generation");
        uiProject.Products[0].Details[0].LengthExpr = "201";
        try { service.SaveCutPlans(uiProject, [plan], plan.Id, plan.InputSignature); check(false, "Stale editor cannot overwrite recalculated project"); }
        catch (InvalidOperationException) { check(true, "Stale editor cannot overwrite recalculated project"); }
        uiProject.Products[0].Details[0].LengthExpr = "200"; ui.Save();

        var split = new AppState(); split.Load(Path.Combine(output, "split-cut-reports.json"));
        var dsp = new Material { Name = "ЛДСП графит", Unit = "m2", SheetLength = 1000, SheetWidth = 800 };
        var dvp = new Material { Name = "ДВП белый", Unit = "m2", SheetLength = 1000, SheetWidth = 800 };
        var glass = new Material { Name = "Стекло 4 мм", Unit = "m2", SheetLength = 1000, SheetWidth = 800 };
        split.Database.Materials = new() { ["Листы"] = [dsp, dvp, glass] };
        var splitProject = new Project { Products = [new Product { Details =
        [
            new() { Name = "Корпус", Type = "Листы", MaterialId = dsp.Id, Qty = 1, LengthExpr = "600", WidthExpr = "500" },
            new() { Name = "Задняя стенка", Type = "Листы", MaterialId = dvp.Id, Qty = 1, LengthExpr = "600", WidthExpr = "500" },
            new() { Name = "Полка стеклянная", Type = "Листы", MaterialId = glass.Id, Qty = 1, LengthExpr = "500", WidthExpr = "300" }
        ] }] };
        split.Database.Projects = [splitProject]; split.Save();
        var mainCut = CuttingService.BuildCutReport(split, splitProject, false, CutReportScope.MainDsp);
        var additionalCut = CuttingService.BuildCutReport(split, splitProject, false, CutReportScope.Additional);
        check(mainCut.Groups.Select(g => g.MaterialName).SequenceEqual(["ЛДСП графит"]) && mainCut.Unplaced.Count == 0 &&
            additionalCut.Groups.Select(g => g.MaterialName).Order().SequenceEqual(new[] { "ДВП белый", "Стекло 4 мм" }.Order()) && additionalCut.Unplaced.Count == 0,
            "Main cut contains only materials with ДСП in the name; DVP and glass are additional");
        var splitProjects = new ProjectService(split);
        var mainPlan = CutPlanService.Capture(mainCut, CutPlanService.Signature(mainCut), "ДСП", "Test");
        var additionalPlan = CutPlanService.Capture(additionalCut, CutPlanService.Signature(additionalCut), "Доп.", "Test");
        splitProjects.SaveCutPlans(splitProject, [mainPlan], mainPlan.Id, mainPlan.InputSignature, CutReportScope.MainDsp);
        splitProjects.SaveCutPlans(splitProject, [additionalPlan], additionalPlan.Id, additionalPlan.InputSignature, CutReportScope.Additional);
        check(splitProject.CutPlans.Single().Id == mainPlan.Id && splitProject.AdditionalCutPlans.Single().Id == additionalPlan.Id &&
            splitProject.ActiveCutPlanId == mainPlan.Id && splitProject.ActiveAdditionalCutPlanId == additionalPlan.Id,
            "Main and additional cut plans are stored independently in the project");

        var families = new AppState(); families.Load(Path.Combine(output, "family-priority.json"));
        families.Database.Preferences.CutTrim = 0; families.Database.Preferences.CutGap = 0;
        var familyMaterial = new Material { Name = "ЛДСП для приоритета", Unit = "m2", SheetLength = 120, SheetWidth = 120 };
        families.Database.Materials = new() { ["Листы"] = [familyMaterial] };
        var familyProject = new Project { Products = [new Product { Name = "Изделие", Details =
        [
            new() { Name = "Полка", Type = "Листы", MaterialId = familyMaterial.Id, Qty = 4, LengthExpr = "60", WidthExpr = "60" },
            new() { Name = "Боковина", Type = "Листы", MaterialId = familyMaterial.Id, Qty = 1, LengthExpr = "70", WidthExpr = "60" }
        ] }] };
        families.Database.Projects = [familyProject]; families.Save();
        var familyReport = CuttingService.BuildCutReport(families, familyProject, false);
        var shelfSheets = familyReport.Groups.Single().Sheets.Where(sheet => sheet.Placements.Any(part => part.DetailName == "Полка")).ToList();
        check(shelfSheets.Count == 1 && shelfSheets.Single().Placements.All(part => part.DetailName == "Полка") && shelfSheets.Single().Placements.Count == 4,
            "Packing keeps a family of details together instead of scattering it into other sheets' gaps");
        check(CutOptimization.Calculate(familyReport).All(variant => variant.Report.Groups.Single().Sheets
            .Where(sheet => sheet.Placements.Any(part => part.DetailName == "Полка"))
            .All(sheet => sheet.Placements.All(part => part.DetailName == "Полка"))),
            "All optimization variants keep the same family priority");
        check(familyReport.Groups.Single().Sheets.Count == 2 && familyReport.Groups.Single().Sheets
            .Single(sheet => sheet.Placements.Any(part => part.DetailName == "Полка")).Placements.Count == 4,
            "Small same-number details share one sheet while another detail uses remaining layout capacity");

        var measure = CuttingMetrics.Analyze(familyReport, 20);
        check(measure.Cuts.Count > 0 && measure.Cuts.Length > 0 && measure.Remainders.All(item => item.Length >= 20 && item.Width >= 20),
            "Cut metrics include trim-aware length and useful rectangular remainders");
        check(CuttingMetrics.LinesForSheet(familyReport.Groups.Single().Sheets.First(), familyReport.Trim).Count ==
            CuttingMetrics.ForSheet(familyReport.Groups.Single().Sheets.First(), familyReport.Trim).Count,
            "Visible cut guides use the same cuts as the optimization metric");
        var realCuts = Report(Sheet(1,
            Part("left", 10, 10, 200, 170),
            Part("right", 210, 10, 200, 270))) with { Trim = 10 };
        var realLines = CuttingMetrics.LinesForSheet(realCuts.Groups[0].Sheets[0], realCuts.Trim);
        check(realLines.All(line => !realCuts.Groups[0].Sheets[0].Placements.Any(part =>
            line.IsVertical
                ? line.Position > part.X + .001 && line.Position < part.X + part.Length - .001 && line.End > part.Y + .001 && line.Start < part.Y + part.Width - .001
                : line.Position > part.Y + .001 && line.Position < part.Y + part.Width - .001 && line.End > part.X + .001 && line.Start < part.X + part.Length - .001)),
            "Real cutting guides never pass through a detail");
        check(realLines.First(line => !line.IsTrim).IsVertical,
            "Crosscuts take priority over longitudinal cuts when both are safe");
        var smallFamilies = Report(Sheet(1,
            Enumerable.Range(0, 12).Select(i => Part($"aligned-{i}", 10 + (i % 6) * 80, 10 + (i / 6) * 100, 70, 90)
                with { DetailId = "aligned", DisplayNumber = 17 }).ToArray()));
        var alignedVariants = CutOptimization.Calculate(smallFamilies);
        check(alignedVariants.All(v => v.Report.Groups[0].Sheets.Count == 1 &&
            v.Report.Groups[0].Sheets[0].Placements.Count == 12 &&
            v.Report.Groups[0].Sheets[0].Placements.All(p => p.DisplayNumber == 17) &&
            CuttingMetrics.ForSheet(v.Report.Groups[0].Sheets[0], 10).Count <= CuttingMetrics.ForSheet(smallFamilies.Groups[0].Sheets[0], 10).Count),
            "Small identical parts stay on one sheet in every engine without adding cuts or changing numbers");
        var leftovers = CuttingMetrics.RemaindersForSheet(realCuts.Groups[0].Sheets[0], 10, 0, 20);
        check(leftovers.Count > 0 && leftovers.All(r => realLines.All(line => line.IsVertical
            ? !(line.Position > r.X + .001 && line.Position < r.X + r.Length - .001 && line.End > r.Y + .001 && line.Start < r.Y + r.Width - .001)
            : !(line.Position > r.Y + .001 && line.Position < r.Y + r.Width - .001 && line.End > r.X + .001 && line.Start < r.X + r.Length - .001))),
            "Useful remainder overlays remain whole after the displayed saw passes");
        var stripParts = Enumerable.Range(0, 8)
            .Select(index => Part($"strip-{index}", 10, 10 + index * 126, 374, 120) with { DetailId = "strip" })
            .ToArray();
        var stripSheet = new CutSheet(1, 2800, 2070, 0, stripParts.ToList());
        var stripLines = CuttingMetrics.LinesForSheet(stripSheet, 10);
        var stripRemainders = CuttingMetrics.RemaindersForSheet(stripSheet, 10, 6, 200);
        check(stripRemainders.Any(remainder => remainder.X >= 384 && remainder.Length >= 2_300 && remainder.Width >= 2_000) &&
            stripLines.Where(line => !line.IsVertical && !line.IsTrim).All(line => line.End <= 384.001),
            "A narrow strip is cut off first so the remaining large sheet area stays useful");

        var totalsState = new AppState(); totalsState.Load(Path.Combine(output, "project-totals.json"));
        var totalsDsp = new Material { Name = "ЛДСП", Unit = "m2", Cost = 1000 };
        var totalsHardware = new Material { Name = "Ручка", Unit = "pc", Cost = 50 };
        totalsState.Database.Materials = new() { ["Листы"] = [totalsDsp], ["Фурнитура"] = [totalsHardware] };
        var totalsProject = new Project { Products = [new Product { Details =
        [
            new() { Name = "Корпус", Type = "Листы", MaterialId = totalsDsp.Id, Qty = 1, LengthExpr = "1000", WidthExpr = "500" },
            new() { Name = "Ручка", Type = "Фурнитура", MaterialId = totalsHardware.Id, Qty = 2 }
        ] }], Payrolls = [new Payroll { Mode = "fixed", Amount = 100 }] };
        var totals = Calculator.Project(totalsState, totalsProject);
        check(totals.ProductCost == 500 && totals.Cost == 700,
            "Project materials metric counts only DSP while total includes all materials and payroll");
    }
}
