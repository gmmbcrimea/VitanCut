using System.Text.Json;
using System.Net;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

var output = Path.Combine(Path.GetTempPath(), "VitanCut-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(output);
var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks++;
    Console.WriteLine("PASS: " + name);
}
void Reject(Action action, string name)
{
    try { action(); } catch (Exception error) when (error is InvalidDataException or InvalidOperationException or JsonException) { Check(true, name); return; }
    Check(false, name);
}

foreach (var viewport in new[] { 640d, 998d, 1200d, 1840d })
{
    var columns = DetailTableLayout.ColumnWidths(viewport);
    Check(Math.Abs(columns.Sum() + 60 - Math.Max(viewport, DetailTableLayout.MinimumWidth)) < .001,
        $"Detail columns fill viewport {viewport} without content-dependent widths");
    Check(columns[2] == 72 && columns[5] == 56 && columns[6] == 150 && columns.Min() >= 56,
        $"Detail fixed columns remain stable at {viewport}");
}

Check(AccentPalette.Options.Select(x => x.Id).Distinct().Count() == AccentPalette.Options.Count, "Accent palette IDs are unique");
Check(AppUpdateService.TryParseVersion("v1.2.3", out var parsedUpdateVersion) && parsedUpdateVersion == new Version(1, 2, 3), "Update tags accept a v-prefixed semantic version");
Check(AppUpdateService.TryParseVersion("1.2.3-beta.1", out parsedUpdateVersion) && parsedUpdateVersion == new Version(1, 2, 3), "Update tags ignore prerelease metadata when comparing versions");
Check(!AppUpdateService.TryParseVersion("latest", out _), "Invalid GitHub release tag is rejected");
Check(AppUpdateService.FormatVersion(new Version(2, 4, 0, 0)) == "2.4.0", "Update status formats local version consistently");
var updateScript = AppUpdateService.BuildUpdateScript("C:\\Users\\gmmbc\\OneDrive\\Заметки\\VitanCut", "C:\\Users\\gmmbc\\OneDrive\\Заметки\\VitanCut", "C:\\Users\\gmmbc\\OneDrive\\Заметки\\VitanCut\\VitanCut.WinUI.exe", "C:\\Temp\\update", 1234);
Check(updateScript.Contains("$source = 'C:\\Users\\gmmbc\\OneDrive\\Заметки\\VitanCut'") && updateScript.Contains("Get-Process -Id 1234") && updateScript.Contains("Start-Process -FilePath $executable"),
    "Updater script preserves Unicode paths and waits before relaunching");
Check(CloudSyncService.DescribeFailure(HttpStatusCode.BadRequest, "{\"code\":400,\"error_code\":\"invalid_credentials\",\"msg\":\"Invalid login credentials\"}") == "Неверный email или пароль.",
    "Cloud authentication errors use a human message");
Check(CloudSyncService.DescribeFailure(HttpStatusCode.Unauthorized, "{}") == "Сеанс входа истёк. Войдите в Supabase снова.",
    "Cloud session errors use a human message");
Check(CloudSyncService.DescribeFailure((HttpStatusCode)429, "{}") == "Слишком много запросов к Supabase. Подождите немного и повторите попытку.",
    "Cloud rate limits use a human message");
Check(CloudSyncService.DescribeFailure(HttpStatusCode.ServiceUnavailable, "{}") == "Сервис Supabase временно недоступен. Попробуйте позже.",
    "Cloud service errors use a human message");
Check(AccentPalette.Find("unknown").Id == "system" && AccentPalette.Find(null).Id == "system", "Unknown accent falls back to system");
var accentState = new AppState();
var accentPath = Path.Combine(output, "accent-preferences.json");
accentState.Load(accentPath);
Check(accentState.Database.Preferences.CursorRevealEnabled, "Cursor reveal defaults on for existing databases");
accentState.Database.Preferences.CursorRevealEnabled = false;
accentState.Save();
accentState.Load(accentPath);
Check(!accentState.Database.Preferences.CursorRevealEnabled, "Disabled cursor reveal survives restart");
accentState.Database.Preferences.CursorRevealEnabled = true;
accentState.Save();
accentState.Load(accentPath);
Check(accentState.Database.Preferences.CursorRevealEnabled, "Enabled cursor reveal survives restart");
accentState.Database.Preferences.CloudPublishIntervalMinutes = 30;
accentState.Save();
accentState.Load(accentPath);
Check(accentState.Database.Preferences.CloudPublishIntervalMinutes == 30, "Cloud publish interval survives restart");
accentState.ImportJson("""{"projects":[],"materials":{},"catalog":{},"preferences":{"cloudPublishIntervalMinutes":7}}""");
Check(accentState.Database.Preferences.CloudPublishIntervalMinutes == 15, "Unsupported cloud publish interval falls back safely");
foreach (var accent in AccentPalette.Options)
{
    accentState.Database.Preferences.AccentColor = AccentPalette.Find(accent.Id).Id;
    accentState.Save();
    accentState.Load(accentPath);
    Check(accentState.Database.Preferences.AccentColor == accent.Id, $"Accent survives restart: {accent.Id}");
    if (accent.Id == "system") continue;
    var light = AccentPalette.UseLightForeground(accent.R, accent.G, accent.B);
    Check(AccentPalette.ForegroundContrast(accent.R, accent.G, accent.B, light) >= 4.5, $"Accent text contrast across gradient: {accent.Id}");
}

var state = new AppState();
var rotationState = new AppState();
var rotationPath = Path.Combine(output, "rotation.json");
rotationState.Load(rotationPath);
rotationState.ImportJson("""
{"projects":[{"name":"Rotation check","products":[{"details":[{"id":"locked","allowRotation":false},{"id":"default"}]}]}]}
""");
var legacyDetails = rotationState.Database.Projects[0].Products[0].Details;
Check(!legacyDetails[0].AllowRotation && legacyDetails[1].AllowRotation, "Legacy import preserves explicit rotation lock and defaults only missing flags");
rotationState.Load(rotationPath);
Check(!rotationState.Database.Projects[0].Products[0].Details[0].AllowRotation, "Rotation lock survives save and restart");
var rotationMaterial = new Material { Name = "Rotation sheet", Unit = "m2", SheetLength = 400, SheetWidth = 800 };
rotationState.Database.Materials["Sheets"] = [rotationMaterial];
var rotationDetail = new Detail { Name = "Rotation part", Type = "Sheets", MaterialId = rotationMaterial.Id, LengthExpr = "600", WidthExpr = "300", AllowRotation = false };
var rotationProject = new Project { Products = [new Product { Details = [rotationDetail] }] };
Check(CuttingService.BuildCutReport(rotationState, rotationProject).Unplaced.Count == 1, "Locked part cannot fit by silently rotating");
Check(CuttingService.BuildDetailingReport(rotationState, rotationProject).Products[0].Rows[0].RotationLocked, "Detailing receives rotation lock");
rotationDetail.AllowRotation = true;
var rotatedCut = CuttingService.BuildCutReport(rotationState, rotationProject);
Check(rotatedCut.Unplaced.Count == 0 && rotatedCut.Groups.SelectMany(g => g.Sheets).SelectMany(s => s.Placements).All(p => p.Rotated && p.AllowRotation), "Enabling rotation allows a rotated fit");
Check(!CuttingService.BuildDetailingReport(rotationState, rotationProject).Products[0].Rows[0].RotationLocked, "Detailing updates when rotation is enabled");
rotationMaterial.TextureDirection = true;
Check(CuttingService.BuildCutReport(rotationState, rotationProject).Unplaced.Count == 1 && CuttingService.BuildDetailingReport(rotationState, rotationProject).Products[0].Rows[0].RotationLocked,
    "Texture direction blocks rotation in packing and detailing");
rotationMaterial.TextureDirection = false;
rotationMaterial.SheetLength = 800;
rotationMaterial.SheetWidth = 800;
rotationDetail.AllowRotation = false;
var lockedCut = CuttingService.BuildCutReport(rotationState, rotationProject);
var lockedPlacement = lockedCut.Groups[0].Sheets[0].Placements[0];
Check(!lockedPlacement.Rotated && !lockedPlacement.AllowRotation, "Cut map receives effective lock for a normally fitting part");
rotationProject.CutLayout = [new CutLayoutOverride { BaseLength = 600, BaseWidth = 300, InstanceId = lockedPlacement.InstanceId,
    X = lockedPlacement.X, Y = lockedPlacement.Y, Length = 300, Width = 600, Rotated = true }];
Check(!CuttingService.BuildCutReport(rotationState, rotationProject).Groups[0].Sheets[0].Placements[0].Rotated, "Saved layout cannot rotate a locked part");
Check(!DetailRotation.IsLocked(rotationDetail, new Material { Unit = "pc" }) && !DetailRotation.CanRotate(rotationDetail, new Material { Unit = "lm" }), "Hardware and linear materials have no rotation control");
var lockedReport = CuttingService.BuildDetailingReport(rotationState, rotationProject);
ReportDocumentService.SaveXlsx(lockedReport, Path.Combine(output, "locked-detailing.xlsx"));
using (var book = new ClosedXML.Excel.XLWorkbook(Path.Combine(output, "locked-detailing.xlsx")))
    Check(book.Worksheets.SelectMany(s => s.CellsUsed()).Any(c => c.GetString().Contains("без поворота")), "Detailing export preserves rotation warning");
rotationState.Database.Projects = [rotationProject];
rotationState.Save();
var displaySource = new Detail { Id = "source-id", Name = "Бок + фасад", LengthExpr = "600", WidthExpr = "300" };
var displayTarget = new Detail { Id = "target-id", Name = "Крыша" };
var displayProduct = new Product { Details = [displaySource, displayTarget] };
var presenter = new FormulaPresentation(displayProduct);
var storedFormula = "=[[detail:source-id:length]]+[[detail:source-id:width]]";
var friendly = presenter.Display(storedFormula);
Check(friendly == "= ⟦Бок + фасад · длина⟧ + ⟦Бок + фасад · ширина⟧", "References show names and dimensions instead of IDs");
Check(presenter.Storage(friendly).Replace(" ", "") == storedFormula, "Friendly formula preserves stored references");
displayTarget.LengthExpr = presenter.Storage(friendly);
Check(FormulaEvaluator.Evaluate(displayProduct, displayTarget).Value == 900, "Friendly formula calculates identically");
Check(FormulaEditing.QueryStart(friendly, friendly.IndexOf("фасад", StringComparison.Ordinal)) == -1, "Operators in readable reference names do not open suggestions");
Check(FormulaEditing.QueryStart(friendly + " * ", friendly.Length + 3) == friendly.Length + 2, "Suggestions work after readable references");
var pasted = new FormulaPresentation(displayProduct);
Check(pasted.Storage(friendly).Replace(" ", "") == storedFormula, "Readable references can be pasted into another field");
var spacedFriendly = friendly.Replace(" ", "\u00a0\t  ");
Check(pasted.Storage(spacedFriendly).Replace(" ", "").Replace("\t", "").Replace("\u00a0", "") == storedFormula,
    "Readable references tolerate repeated and nonbreaking whitespace");
Check(pasted.Storage("= ⟦ Бок + фасад·длина ⟧+⟦Бок + фасад·ширина⟧").Replace(" ", "") == storedFormula,
    "Spaces at readable reference delimiters are optional");
displayTarget.WidthExpr = pasted.Storage(spacedFriendly);
Check(FormulaEvaluator.Evaluate(displayProduct, displayTarget, width: true) is { IsValid: true, Value: 900 },
    "Spaced readable references calculate through the editor storage path");
displaySource.Name = "Боковина";
Check(presenter.Display(storedFormula).Contains("Боковина · длина") && presenter.Storage(friendly).Contains("source-id"), "Renaming updates labels without losing an active edit");
var duplicate = new Detail { Id = "duplicate-id", Name = "Боковина" };
displayProduct.Details.Add(duplicate);
var duplicateRaw = "[[detail:source-id:length]]+[[detail:duplicate-id:length]]";
var duplicateFriendly = presenter.Display(duplicateRaw);
Check(duplicateFriendly.Contains("деталь 1") && duplicateFriendly.Contains("деталь 3") && presenter.Storage(duplicateFriendly).Replace(" ", "") == duplicateRaw,
    "Duplicate detail names have distinct readable references");
displayProduct.Details.Remove(displaySource);
var missingFriendly = presenter.Display(storedFormula);
Check(missingFriendly.Contains("Удалённая деталь") && presenter.Storage(missingFriendly).Replace(" ", "") == storedFormula, "Deleted references are visible and retain identity");
Check(presenter.Storage("=⟦Несуществующая · длина⟧") == "=⟦Несуществующая · длина⟧", "Unknown labels are not silently rebound");
Check(presenter.Display("=Корона-верх + 1e-3") == "=Корона-верх + 1e-3", "Formatting preserves fixed-size names and scientific notation");
var path = Path.Combine(output, "database.json");
state.Load(path);
state.Database.Preferences.Theme = "dark";
state.Save();
state.Load(path);
Check(state.Database.Preferences.Theme == "dark", "Explicit theme survives restart");
Check(File.Exists(path + ".bak"), "Atomic save keeps a backup");
var original = File.ReadAllText(path);
Reject(() => state.ImportJson("{}"), "Unrelated JSON rejected");
Reject(() => state.ImportJson("{broken"), "Malformed import rejected");
Check(File.ReadAllText(path) == original && state.Database.Preferences.Theme == "dark", "Rejected import preserves disk and memory");
var broken = Path.Combine(output, "broken.json");
File.WriteAllText(broken, "{damaged");
var brokenState = new AppState();
Reject(() => brokenState.Load(broken), "Damaged database fails explicitly");
Reject(() => brokenState.Save(), "Save disabled after failed load");
Check(File.ReadAllText(broken) == "{damaged", "Damaged source is never overwritten");

var cloudState = new AppState();
cloudState.Load(Path.Combine(output, "cloud", "database.json"));
var cloud = new CloudSyncService(cloudState);
cloud.Load();
Check(!cloud.IsSignedIn && !cloud.HasUnsyncedLocalChanges,
    "Cloud sync starts without a stored session or unsynced snapshot");
Check(!(await cloud.SignInAsync("", "")).Succeeded,
    "Cloud sync requires account credentials before requesting Supabase");

var mergeBase = new Database
{
    Counterparties = ["Клиент"],
    MaterialGroups = ["ЛДСП"],
    Materials = new Dictionary<string, List<Material>>
    {
        ["ЛДСП"] = [new Material { Id = "material-1", Name = "Белый", Unit = "m2", Cost = 800, SheetLength = 2750, SheetWidth = 1830 }]
    },
    Projects = [new Project { Id = "project-1", Name = "Кухня", Counterparty = "Клиент" }],
    Catalog = [],
    Preferences = new AppPreferences { Theme = "dark" }
};
var mergeLocal = CloudEntityMerge.Restore(CloudEntityMerge.Snapshot(mergeBase));
var mergeRemote = CloudEntityMerge.Restore(CloudEntityMerge.Snapshot(mergeBase));
mergeLocal.Projects[0].Name = "Кухня Петрова";
mergeRemote.Materials["ЛДСП"][0].Cost = 900;
var mergedIndependent = CloudEntityMerge.Merge(mergeBase, mergeLocal, mergeRemote);
Check(mergedIndependent.Conflicts.Count == 0 && mergedIndependent.Database.Projects[0].Name == "Кухня Петрова" && mergedIndependent.Database.Materials["ЛДСП"][0].Cost == 900,
    "Addressable merge combines independent project and material edits");
mergeRemote.Projects[0].Name = "Кухня Иванова";
var mergedConflict = CloudEntityMerge.Merge(mergeBase, mergeLocal, mergeRemote);
Check(mergedConflict.Conflicts.Single().Key == new CloudEntityKey("project", "project-1") && mergedConflict.Database.Projects[0].Name == "Кухня Петрова",
    "Addressable merge isolates simultaneous edits to one project");
mergeLocal.Materials["ЛДСП"].Clear();
mergeRemote.Materials["ЛДСП"][0].Name = "Белый премиум";
var deletedConflict = CloudEntityMerge.Merge(mergeBase, mergeLocal, mergeRemote);
Check(deletedConflict.Conflicts.Any(conflict => conflict.Key == new CloudEntityKey("material", "material-1")),
    "Addressable merge reports delete-versus-edit conflicts");
var appearanceLocal = CloudEntityMerge.Restore(CloudEntityMerge.Snapshot(mergeBase));
var cutSettingsRemote = CloudEntityMerge.Restore(CloudEntityMerge.Snapshot(mergeBase));
appearanceLocal.Preferences.Theme = "light";
appearanceLocal.Preferences.AccentColor = "violet";
cutSettingsRemote.Preferences.CutTrim = 18;
cutSettingsRemote.Preferences.CutGap = 4;
var mergedSettings = CloudEntityMerge.Merge(mergeBase, appearanceLocal, cutSettingsRemote);
Check(mergedSettings.Conflicts.Count == 0 && mergedSettings.Database.Preferences.CutTrim == 18 && mergedSettings.Database.Preferences.CutGap == 4 &&
      mergedSettings.Database.Preferences.Theme == "light" && mergedSettings.Database.Preferences.AccentColor == "violet",
    "Addressable merge syncs cut settings but keeps local appearance preferences");

var material = new Material { Name = "ЛДСП Белый", Unit = "m2", Cost = 500, SheetLength = 2800, SheetWidth = 2070 };
state.Database.Materials["Пиломатериалы"] = [material];
var pc = new Material { Name = "Ручка", Unit = "pc", Cost = 100 };
state.Database.Materials["Фурнитура"] = [pc];
var a = new Detail { Name = "Полка", Type = "Пиломатериалы", MaterialId = material.Id, LengthExpr = "=длина изделия - 32", WidthExpr = "500" };
var b = new Detail { Name = "Крышка", Type = a.Type, MaterialId = a.MaterialId, LengthExpr = $"=[[detail:{a.Id}:length]]+10", WidthExpr = "500" };
var product = new Product { Name = "Тестовое изделие", Length = 600, Qty = 2, Details = [a, b], FixedSizes = [new() { Name = "Полка", Value = 10 }, new() { Name = "Полка 2", Value = 20 }] };
var project = new Project { Name = "Проверка", Products = [product] };
state.Database.Projects = [project];
Check(Calculator.Detail(state, product, b).Length == 578, "Detail reference arithmetic");
foreach (var expression in new[]
{
    "= длина изделия+30", "=длина изделия + 30", "= длина изделия +30",
    "  =   длина   изделия  +  30  ", "\t=\tДЛИНА\tИЗДЕЛИЯ\t+30",
    "=\u00a0длина\u00a0изделия+30", "=длина\u202fизделия + 30",
    "= ( длина изделия + 60 ) / 2 * 2 - 30"
})
{
    a.LengthExpr = expression;
    var result = FormulaEvaluator.Evaluate(product, a);
    Check(result.IsValid && result.Value == 630, "Formula whitespace: " + expression);
}
a.LengthExpr = "=Полка   2+Полка*2";
Check(FormulaEvaluator.Evaluate(product, a) is { IsValid: true, Value: 40 }, "Fixed-size references tolerate repeated spaces");
product.FixedSizes.Add(new() { Name = "  Зазор\u00a0  фасада  ", Value = 3 });
a.WidthExpr = "= 500 - Зазор фасада * 2";
Check(Calculator.Detail(state, product, a).Width == 494, "Stored fixed-size whitespace and width formulas use the same rules");
product.FixedSizes.Add(new() { Name = "Зазор фасада", Value = 4 });
Check(ProductValidation.Error(state, product)?.Contains("уникальными") == true, "Fixed sizes cannot differ only in whitespace");
product.FixedSizes.RemoveAt(product.FixedSizes.Count - 1);
a.WidthExpr = "500";
a.LengthExpr = "700";
Check(Calculator.Detail(state, product, b).Length == 710, "Reference recalculates after source edit");
foreach (var expression in new[] { "1..2", "(600-32", "600 garbage", "2/0", "600+", "Неизвестно", "600 30", "600\u00a030", "длина изделиялишнее+30", "==длина изделия" })
{
    a.LengthExpr = expression;
    Check(Calculator.Detail(state, product, a).Error.Length > 0, "Invalid formula diagnosed: " + expression);
}
a.LengthExpr = "=Полка 2 + Полка × 2";
Check(Calculator.Detail(state, product, a).Length == 40, "Longest fixed name and multiplication symbol");
a.LengthExpr = $"=[[detail:{b.Id}:length]]";
Check(Calculator.Detail(state, product, a).Error.Contains("Цикл"), "Cyclic references diagnosed");
a.LengthExpr = "=1000 ÷ 2";
Check(Calculator.Detail(state, product, a).Length == 500, "Division symbol");
product.Details.Remove(a);
Check(Calculator.Detail(state, product, b).Error.Contains("удалена"), "Deleted reference diagnosed");
product.Details.Insert(0, a);
var hardware = new Detail { Type = "Фурнитура", MaterialId = pc.Id, Qty = 4, LengthExpr = "=oops", WidthExpr = "600" };
product.Details.Add(hardware);
var hardwareCalc = Calculator.Detail(state, product, hardware);
Check(hardwareCalc.Length == 0 && hardwareCalc.Width == 0 && hardwareCalc.Cost == 400 && hardwareCalc.Error == "", "Piece material ignores dimension formulas");
var detailing = CuttingService.BuildDetailingReport(state, project);
Check(detailing.Products[0].Rows.Last().Qty == 8 && !detailing.Products[0].Rows.Last().HasIssue, "Detail report multiplies product quantity and accepts hardware");
var dspDetailing = CuttingService.FilterDetailingReportToDsp(detailing);
Check(dspDetailing.Products.SelectMany(product => product.Rows).All(row => row.MaterialName.Contains("ДСП", StringComparison.OrdinalIgnoreCase)) &&
      dspDetailing.Summary.DetailQuantity == 4 && dspDetailing.Products.Count == 1,
    "Detail report can be limited to DSP materials with recalculated totals");

var library = new MaterialService(state);
Check(library.ExistsName("  ручка  "), "Duplicate name ignores case and outer spaces");
Reject(() => library.Add("Фурнитура", new Material { Name = "ручка" }), "Duplicate material cannot be added");
Reject(() => library.Add("Фурнитура", new Material { Name = "Плохая цена", Cost = double.NaN }), "Nonfinite material price rejected");
Reject(() => library.Delete(new MaterialChoice("Фурнитура", pc)), "Used material cannot be deleted");
product.Qty = double.NaN;
Check(ProductValidation.Error(state, product) is not null, "Empty product quantity rejected");
product.Qty = 2;
foreach (var expression in new[] { "=", "600+", "600-", "600*", "600/", "600×", "600÷", "600−" })
    Check(FormulaEditing.QueryStart(expression, expression.Length) == expression.Length, "Reference trigger: " + expression);
var token = "=[[detail:abc-def:length]]";
Check(FormulaEditing.QueryStart(token, token.Length) == -1, "Operators inside reference IDs ignored");
Check(FormulaEditing.QueryStart("=глу+20", 4) == 1, "Formula query follows caret position");
var catalog = new CatalogService(state);
Check(catalog.GetProducts(project.Counterparty).Count == 0 && state.Database.Catalog.Count == 0, "Reading catalog does not copy project products");
var moved = library.Save(new MaterialChoice("Фурнитура", pc), "Комплектующие");
Check(hardware.Type == "Комплектующие" && Calculator.Detail(state, product, hardware).Cost == 400, "Moving material preserves detail references");
library.Save(moved, "Фурнитура");
var template = new Product { Name = "Шаблон" };
catalog.SaveProduct("Временный заказчик", null, template);
catalog.DeleteCounterparty("Временный заказчик");
Check(catalog.GetProducts("Без заказчика").Contains(template), "Deleting customer preserves catalog products");
catalog.DeleteProduct("Без заказчика", template);
Check(catalog.GetProducts("Без заказчика").Count == 0 && project.Products.Contains(product), "Catalog removal does not delete project products");

a.LengthExpr = "600";
a.Qty = 12;
var cut = CuttingService.BuildCutReport(state, project);
var placements = cut.Groups.SelectMany(group => group.Sheets).SelectMany(sheet => sheet.Placements).ToList();
Check(placements.Count == 26 && cut.Unplaced.Count == 0, "Cutting preserves all expanded quantities");
foreach (var sheet in cut.Groups.SelectMany(group => group.Sheets))
{
    Check(sheet.Placements.All(p => p.X >= cut.Trim && p.Y >= cut.Trim && p.X + p.Length <= sheet.SheetLength - cut.Trim && p.Y + p.Width <= sheet.SheetWidth - cut.Trim), "Parts fit sheet bounds");
    Check(!sheet.Placements.Any(p => sheet.Placements.Any(q => q != p && p.X < q.X + q.Length + cut.Gap && p.X + p.Length + cut.Gap > q.X && p.Y < q.Y + q.Width + cut.Gap && p.Y + p.Width + cut.Gap > q.Y)), "Parts do not overlap and respect kerf");
}
var first = placements.First();
project.CutLayout = [new() { InstanceId = first.InstanceId, BaseLength = first.BaseLength, BaseWidth = first.BaseWidth, X = 10, Y = 10, Length = 1, Width = 1 }];
Check(CuttingService.BuildCutReport(state, project).Groups.SelectMany(g => g.Sheets).SelectMany(s => s.Placements).All(p => p.Length > 1 && p.Width > 1), "Saved layout cannot shrink parts");
material.SheetLength = 0;
Check(CuttingService.BuildCutReport(state, project).Unplaced.Count > 0, "Missing sheet size is reported");
material.SheetLength = 2800;

ReportDocumentService.SavePdf(cut, Path.Combine(output, "cut.pdf"));
ReportDocumentService.SaveXlsx(cut, Path.Combine(output, "cut.xlsx"));
ReportDocumentService.SavePdf(detailing, Path.Combine(output, "detailing.pdf"));
ReportDocumentService.SaveXlsx(detailing, Path.Combine(output, "detailing.xlsx"));
var logo = Path.GetFullPath("native-winui/Assets/logo.png");
product.Image = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(logo));
var illustrated = CuttingService.BuildDetailingReport(state, project);
ReportDocumentService.SavePdf(illustrated, Path.Combine(output, "illustrated.pdf"));
ReportDocumentService.SaveXlsx(illustrated, Path.Combine(output, "illustrated.xlsx"));
using (var book = new ClosedXML.Excel.XLWorkbook(Path.Combine(output, "illustrated.xlsx")))
    Check(book.Worksheets.Sum(sheet => sheet.Pictures.Count) == 1, "Embedded product image exported to XLSX");
ReportDocumentService.SavePdf(cut with { Groups = [] }, Path.Combine(output, "empty.pdf"));
Check(File.ReadAllBytes(Path.Combine(output, "cut.pdf")).AsSpan(0, 4).SequenceEqual("%PDF"u8), "PDF document generated");
using (var book = new ClosedXML.Excel.XLWorkbook(Path.Combine(output, "cut.xlsx")))
{
    Check(book.Worksheets.Count == cut.Groups.Sum(group => group.Sheets.Count), "One Excel worksheet per cut sheet");
    Check(book.Worksheets.All(sheet => sheet.Pictures.Count == 1), "Every cut worksheet contains a drawing");
    Check(book.Worksheets.All(sheet => sheet.PageSetup.PagesWide == 1 && sheet.PageSetup.PagesTall == 1), "Each cut worksheet prints on one page");
}
state.Database.Preferences.Theme = "auto";
state.Save();
Check(state.Database.Preferences.Theme == "auto", "System theme is available for the UI fixture");
CutWorkflowChecks.Run(Check, output);
Console.WriteLine($"{checks} checks passed. Artifacts: {output}");
