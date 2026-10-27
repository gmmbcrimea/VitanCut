using System.Text.Json;
using System.Net;
using System.Diagnostics;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

var testRoot = Environment.GetEnvironmentVariable("VITANCUT_TEST_ROOT");
if (string.IsNullOrWhiteSpace(testRoot))
    throw new InvalidOperationException("Set VITANCUT_TEST_ROOT to a disposable directory inside the repository.");
var output = Path.Combine(Path.GetFullPath(testRoot), "VitanCut-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(output);
var checks = 0;
EdgeBandingChecks.Run(Check, output);
MaterialPricingChecks.Run(Check, output);
LocalizationChecks.Run(Check, output);
ProjectDetailPricingChecks.Run(Check, output);
WasteAllowanceChecks.Run(Check, output);
ProjectCatalogPickerChecks.Run(Check, output);
CatalogBrowserChecks.Run(Check, output);
var exportDate = new DateTime(2026, 10, 26, 14, 30, 0);
foreach (var reportName in new[] { "Деталировка", "Раскрой", "Сводная таблица" })
    Check(ReportFileName.Create(reportName, "Гурзуф", exportDate) == $"{reportName}_Гурзуф_2026-10-26_14-30",
        "Export filename includes report type, project and save date: " + reportName);
Check(ReportFileName.Create("Раскрой", "  Кухня: А/Б\\В?*\"<>|\n. ", exportDate) ==
    "Раскрой_Кухня_ А_Б_В" + new string('_', 8) + "2026-10-26_14-30", "Export filename replaces Windows-invalid characters and trims trailing dots");
Check(ReportFileName.Create("Раскрой", " ... ", exportDate) == "Раскрой_Проект_2026-10-26_14-30",
    "Export filename uses a project fallback for an empty sanitized name");
Check(ReportFileName.Create("Раскрой", new string('A', 300), exportDate).Length < 180,
    "Very long project names leave room for the report prefix, date and extension");
var orderedDetails = new Product
{
    Details = [new() { Type = "Стекло", Name = "Glass" }, new() { Type = "Кромка", Name = "Edge", IsGeneratedEdgeBand = true },
        new() { Type = "Фурнитура", Name = "Hardware" }, new() { Type = "Пиломатериалы", Name = "First wood" },
        new() { Type = "Алюминий", Name = "Aluminum" }, new() { Type = "Пиломатериалы", Name = "Second wood" }]
};
var beforeOrdering = orderedDetails.Details.ToArray();
orderedDetails.Details[0].LengthExpr = $"[[detail:{orderedDetails.Details[3].Id}:length]]";
DetailOrdering.SortForSave(orderedDetails);
Check(orderedDetails.Details.Select(detail => detail.Name).SequenceEqual(["First wood", "Second wood", "Hardware", "Edge", "Aluminum", "Glass"]),
    "Saving orders wood, hardware, edge banding and remaining categories alphabetically with stable order within a category");
Check(beforeOrdering.All(detail => orderedDetails.Details.Contains(detail)) &&
    orderedDetails.Details.Last().LengthExpr == $"[[detail:{beforeOrdering[3].Id}:length]]" && orderedDetails.Details[3].IsGeneratedEdgeBand,
    "Detail sorting preserves identities, references and generated edge banding");
DetailOrdering.SortForSave(orderedDetails);
Check(orderedDetails.Details[0] == beforeOrdering[3] && orderedDetails.Details[1] == beforeOrdering[5], "Repeated detail sorting remains stable");
var legacyWoodOrder = new Product { Details = [new() { Type = "Кромка" }, new() { Type = "ЛДСП" }, new() { Type = "фурнитура" }] };
DetailOrdering.SortForSave(legacyWoodOrder);
Check(legacyWoodOrder.Details.Select(detail => detail.Type).SequenceEqual(["ЛДСП", "фурнитура", "Кромка"]),
    "Legacy sheet categories and case variants retain the requested priority");
Check(UiLayout.ReportPreparationDuration(1) == TimeSpan.FromSeconds(.5), "One product shows report preparation for half a second");
Check(UiLayout.ReportPreparationDuration(6) == TimeSpan.FromSeconds(3), "Six products show report preparation for three seconds");
Check(UiLayout.ReportPreparationDuration(0) == TimeSpan.FromSeconds(.5), "Empty projects retain a bounded preparation interval");
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
int RunRobocopy(params string[] arguments)
{
    var startInfo = new ProcessStartInfo("robocopy") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the synthetic robocopy fixture.");
    process.StandardOutput.ReadToEnd();
    process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode > 7) throw new IOException($"Synthetic robocopy failed with code {process.ExitCode}.");
    return process.ExitCode;
}

Check(UiLayout.WorkListHeight(620, 280) == 360, "Work lists retain five-row minimum in short windows");
Check(UiLayout.WorkListHeight(1000, 280) == 608, "Work lists use measured header and viewport");
Check(UiLayout.WorkListHeight(2000, 100) == 720, "Work lists have a bounded desktop height");
Check(UiLayout.MaterialColumnWidth(500, 980, 280, true) == 356, "Category resizing reserves editor space");
Check(UiLayout.MaterialColumnWidth(520, 980, 200, false) == 436, "Material resizing reserves editor space");
Check(UiLayout.MaterialColumnWidth(10, 640, 280, true) == 150, "Category resizing keeps its minimum");
Check(UiLayout.CompactGap(24) == 8 && Math.Abs(UiLayout.CompactGap(12) - 4.8) < .0001, "Compact gaps are dense without scaling controls");
Check(UiLayout.CompactInset(28) == 12 && UiLayout.CompactInset(16) == 8, "Compact panel padding is capped");
Check(UiLayout.CompactGap(0) == 0 && UiLayout.CompactInset(0) == 0, "Compact density preserves zero insets");
var revealViewport = new RevealBounds(0, 0, 200, 100);
Check(!RevealGeometry.HasVisibleEdge(0, 1, 0x00202020, 0xFF202020), "Transparent surfaces cannot reveal an invisible control rectangle");
Check(!RevealGeometry.HasVisibleEdge(0, 1, 0xFF202020, 0xFF202020), "Identical surface and backdrop have no reflective edge");
Check(RevealGeometry.HasVisibleEdge(255, 1, 0, 0xFF202020), "An actually painted border remains eligible for an edge highlight");
Check(!RevealGeometry.HasVisibleEdge(255, 0, 0, 0xFF202020), "A zero-width border cannot create a phantom edge");
Check(RevealGeometry.HasVisibleEdge(0, 0, 0xFF0078D4, 0xFF202020), "A contrasting filled control has a visible silhouette");
Check(!RevealGeometry.HasVisibleEdge(0, 0, 0x01FFFFFF, 0xFF202020), "Nearly transparent paint cannot create a strong artificial boundary");
Check(RevealGeometry.Clip(new RevealBounds(10, 20, 40, 30), revealViewport) == new RevealBounds(10, 20, 40, 30), "Reveal preserves a fully visible mask in window coordinates");
Check(RevealGeometry.Clip(new RevealBounds(-10, -20, 40, 50), revealViewport) == new RevealBounds(0, 0, 30, 30), "Reveal clips a partially scrolled mask to the viewport");
Check(RevealGeometry.Clip(new RevealBounds(190, 90, 40, 50), revealViewport) == new RevealBounds(190, 90, 10, 10), "Reveal stays within the bottom and right scroll edges");
Check(RevealGeometry.Clip(new RevealBounds(200, 100, 40, 50), revealViewport) == new RevealBounds(200, 100, 0, 0), "Reveal has no visible mask at an exactly adjacent viewport edge");
Check(RevealGeometry.Clip(new RevealBounds(250, 130, 40, 50), revealViewport).Width == 0, "Offscreen reveal masks do not acquire a negative size");
Check(UiLayout.ContainsPoint(50, 25, 100, 50), "Leaving a child inside the window must not extinguish reveal");
Check(UiTextSearch.Matches("  ", "ЛДСП"), "Empty material search preserves every choice");
var disabledProfileFolder = Path.Combine(output, "disabled-profile");
var disabledProfile = new UiPerformanceLog(disabledProfileFolder, false);
using (disabledProfile.Measure(UiWork.Materials)) { }
disabledProfile.Flush();
Check(!Directory.Exists(disabledProfileFolder), "Disabled UI profiler writes no files");
var profileFolder = Path.Combine(output, "ui-profile");
var profile = new UiPerformanceLog(profileFolder, true);
var stageQuota = Math.Max(1, 64 / Enum.GetValues<UiWork>().Length);
var measured = profile.Measure(UiWork.Materials);
measured.Dispose(); measured.Dispose();
profile.Flush();
using (var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(profileFolder, "ui-performance.json"))))
{
    Check(data.RootElement.GetArrayLength() == 1, "A UI measurement can complete only once");
    Check(data.RootElement[0].GetProperty("ManagedAllocatedBytes").GetInt64() >= 0 && data.RootElement[0].GetProperty("ElapsedMilliseconds").GetDouble() >= 0,
        "Synchronous UI profiler records nonnegative elapsed time and managed allocations");
}
for (var sample = 0; sample < 70; sample++)
    using (profile.Measure(UiWork.ImagePreview, asynchronous: true)) { }
profile.Flush();
using (var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(profileFolder, "ui-performance.json"))))
{
    Check(data.RootElement.GetArrayLength() == stageQuota + 1, "UI profiler retains a bounded stage quota without losing another stage");
    Check(data.RootElement.EnumerateArray().Where(item => item.GetProperty("Stage").GetString() == nameof(UiWork.ImagePreview)).All(item => item.GetProperty("ManagedAllocatedBytes").ValueKind == JsonValueKind.Null),
        "Async UI measurements do not claim interleaved thread allocations as their own");
    Check(data.RootElement.EnumerateArray().All(item => Enum.TryParse<UiWork>(item.GetProperty("Stage").GetString(), out _)),
        "UI profiler stores fixed operation names rather than user data");
}
foreach (var stage in Enum.GetValues<UiWork>())
    for (var sample = 0; sample < 80; sample++)
        using (profile.Measure(stage)) { }
profile.Flush();
using (var data = JsonDocument.Parse(File.ReadAllText(Path.Combine(profileFolder, "ui-performance.json"))))
{
    Check(data.RootElement.GetArrayLength() <= 64, "UI profiler remains bounded across all stages");
    Check(data.RootElement.EnumerateArray().GroupBy(item => item.GetProperty("Stage").GetString()).All(group => group.Count() == stageQuota),
        "Every UI stage survives a burst from all other stages");
}
var reviewWorkspace = Path.Combine(output, "Project-UI-Refactored");
var reviewApplication = Path.Combine(reviewWorkspace, "runtime");
Directory.CreateDirectory(reviewApplication);
File.WriteAllText(Path.Combine(reviewWorkspace, "UI_REFACTOR_PLAN.md"), "Synthetic isolation fixture");
File.WriteAllText(Path.Combine(reviewWorkspace, "UI_WORKSPACE.md"), "Synthetic isolation fixture");
var reviewPaths = UiReviewPaths.Create(reviewApplication);
Check(reviewPaths.IsDevelopment && reviewPaths.DataRoot.EndsWith(Path.Combine("ui-audit", "test-data"), StringComparison.OrdinalIgnoreCase), "UI review defaults to isolated Development data even without launcher environment");
var relativeReviewPaths = UiReviewPaths.Create(reviewApplication, Path.Combine("ui-audit", "review-path-check"));
Check(relativeReviewPaths.IsDevelopment && relativeReviewPaths.DataRoot.EndsWith(Path.Combine("ui-audit", "review-path-check"), StringComparison.OrdinalIgnoreCase), "Relative UI data paths resolve within the review workspace");
Reject(() => UiReviewPaths.Create(reviewApplication, Path.GetPathRoot(reviewApplication)), "UI review rejects data outside its workspace before loading it");
Reject(() => UiReviewPaths.Create(reviewApplication, reviewApplication), "UI review rejects data in application files");
Reject(() => UiReviewPaths.Create(Path.GetPathRoot(AppContext.BaseDirectory)!), "UI review rejects application launch outside its marked workspace");
var formulaSuggestion = FormulaSuggestionInsertion.Apply("= дли + 32", 1, 5, "длина изделия");
Check(formulaSuggestion.Text == "= длина изделия + 32", "Formula suggestion replaces only the current query and preserves its suffix");
Check(formulaSuggestion.Caret == formulaSuggestion.Text.IndexOf(" + 32", StringComparison.Ordinal), "Formula suggestion restores the caret immediately after the inserted reference");
var namedSuggestion = FormulaSuggestionInsertion.Apply("= бок + 16", 1, 5, "⟦Боковина · длина⟧");
Check(namedSuggestion.Text == "= ⟦Боковина · длина⟧ + 16", "Formula suggestion preserves display-reference tokens and trailing arithmetic");
Check(FormulaSuggestionInsertion.Apply("=   выс", 1, 7, "высота изделия").Text == "= высота изделия", "Formula suggestion retains existing prefix-whitespace behavior");
Check(UiTextSearch.Matches("детали ширина", "Полка · ширина", "[[detail:id:width]]", "Детали"), "Formula suggestions match words across label and section");
try
{
    FormulaSuggestionInsertion.Apply("= x", 4, 3, "длина изделия");
    Check(false, "Invalid suggestion range must be rejected");
}
catch (ArgumentOutOfRangeException) { Check(true, "Invalid suggestion range is rejected before editing text"); }
var requestGate = new UiRequestGate();
var olderRequest = requestGate.Begin();
Check(requestGate.IsCurrent(olderRequest), "Newest UI request can publish its result");
var newerRequest = requestGate.Begin();
Check(!requestGate.IsCurrent(olderRequest) && requestGate.IsCurrent(newerRequest), "New UI request invalidates older image and status completions");
var emptyRequest = requestGate.Begin();
Check(!requestGate.IsCurrent(newerRequest) && requestGate.IsCurrent(emptyRequest), "An empty latest image invalidates a pending previous image");
var otherImageGate = new UiRequestGate();
Check(otherImageGate.IsCurrent(otherImageGate.Begin()) && requestGate.IsCurrent(emptyRequest), "Independent image requests do not invalidate other controls");
var reverseCompletionGate = new UiRequestGate();
var firstImage = new TaskCompletionSource<string>();
var secondImage = new TaskCompletionSource<string>();
var committedImage = "";
async Task SimulateImageCompletion(Task<string> decode)
{
    var version = reverseCompletionGate.Begin();
    var decoded = await decode;
    if (reverseCompletionGate.IsCurrent(version)) committedImage = decoded;
}
var firstImageTask = SimulateImageCompletion(firstImage.Task);
var secondImageTask = SimulateImageCompletion(secondImage.Task);
secondImage.SetResult("new-image");
await secondImageTask;
firstImage.SetResult("old-image");
await firstImageTask;
Check(committedImage == "new-image", "Reverse async completion order keeps the latest thumbnail");
var cloudReadyView = CloudUiPresentation.Create(true, true, false, false);
var updatePreviewVersion = new Version(1, 2, 0);
foreach (var scenario in new[] { "current", "available", "error", "checking", "downloading", "restarting" })
    Check(UpdateUiPreview.Create(false, scenario, updatePreviewVersion) is null, $"Update preview cannot override production: {scenario}");
Check(UpdateUiPreview.Create(true, null, updatePreviewVersion) is null && UpdateUiPreview.Create(true, "unknown", updatePreviewVersion) is null,
    "Update preview is opt-in and rejects unknown scenarios");
Check(UpdateUiPreview.Create(true, "current", updatePreviewVersion)!.Info.Availability == UpdateAvailability.UpToDate,
    "Update preview renders the actual current version without a feed request");
var availablePreview = UpdateUiPreview.Create(true, "available", updatePreviewVersion)!;
Check(availablePreview.Info.LatestVersion == new Version(1, 3, 0) && !string.IsNullOrEmpty(availablePreview.Info.ReleaseNotes)
    && new Uri(availablePreview.Info.DownloadUrl!).Host.EndsWith(".test", StringComparison.Ordinal),
    "Available preview contains only target-release notes and a reserved test URL");
Check(UpdateUiPreview.Create(true, "checking", updatePreviewVersion)!.Checking,
    "Checking preview does not masquerade as a completed version check");
Check(UpdateUiPreview.Create(true, "downloading", updatePreviewVersion)!.Percent == 45
    && UpdateUiPreview.Create(true, "restarting", updatePreviewVersion)!.Restarting,
    "Download and restart previews require no files, processes or installation");
foreach (var scenario in new[] { "signed-out", "connecting", "synchronizing", "ready", "offline", "expired" })
    Check(CloudUiSnapshot.Preview(false, scenario) is null, $"Cloud preview cannot override production presentation: {scenario}");
Check(CloudUiSnapshot.Preview(true, null) is null && CloudUiSnapshot.Preview(true, "unknown") is null,
    "Cloud preview is opt-in and rejects unknown scenarios");
Check(CloudUiSnapshot.Preview(true, "ready")!.Presentation.IsHealthy,
    "Readonly cloud preview renders the ready state without a service session");
Check(CloudUiSnapshot.Preview(true, "connecting")!.Presentation.IsBusy && CloudUiSnapshot.Preview(true, "synchronizing")!.Presentation.IsBusy,
    "Readonly cloud preview exercises both login and signed-in progress states");
Check(CloudUiSnapshot.Preview(true, "offline")!.Presentation.ShowBadge && !CloudUiSnapshot.Preview(true, "offline")!.Presentation.IsHealthy,
    "Offline preview retains the account badge without claiming healthy sync");
Check(CloudUiSnapshot.Preview(true, "expired")!.Presentation.ShowLogin && CloudUiSnapshot.Preview(true, "expired")!.Presentation.ShowStatus,
    "Expired-session preview restores login and an explanatory error");
Check(cloudReadyView.IsHealthy && !cloudReadyView.ShowLogin && cloudReadyView.ShowBadge && cloudReadyView.ControlsEnabled,
    "Ready cloud UI hides login and permits manual synchronization");
var cloudConnectingView = CloudUiPresentation.Create(true, false, true, false);
Check(cloudConnectingView.IsBusy && cloudConnectingView.ShowLogin && cloudConnectingView.ShowStatus && !cloudConnectingView.ControlsEnabled,
    "Cloud connection shows progress without claiming a successful sync");
var cloudSyncingView = CloudUiPresentation.Create(true, true, true, false);
Check(cloudSyncingView.IsBusy && !cloudSyncingView.IsHealthy && cloudSyncingView.ShowBadge && !cloudSyncingView.ShowLogin,
    "An access token during sync does not display the ready state");
var cloudErrorView = CloudUiPresentation.Create(true, false, false, true);
Check(!cloudSyncingView.ShowStatus && cloudConnectingView.ShowStatus,
    "Cloud progress has one status label: badge when signed in, standalone while connecting");
Check(CloudUiPresentation.Create(true, true, false, true).ShowStatus,
    "Cloud attention preserves the detailed error below the account badge");
Check(cloudErrorView.ShowLogin && cloudErrorView.ShowStatus && cloudErrorView.ControlsEnabled && !cloudErrorView.IsHealthy,
    "Cloud error permits another login and exposes its status");
Check(!CloudUiPresentation.Create(true, true, false, true).IsHealthy,
    "An error with a retained token cannot show healthy cloud styling");
var cloudDisabledView = CloudUiPresentation.Create(false, true, true, true);
Check(!cloudDisabledView.ShowLogin && !cloudDisabledView.ShowBadge && !cloudDisabledView.IsBusy && !cloudDisabledView.ControlsEnabled,
    "Isolated cloud UI remains disabled even with synthetic transport flags");
foreach (var enabled in new[] { false, true })
foreach (var signedIn in new[] { false, true })
foreach (var busy in new[] { false, true })
foreach (var error in new[] { false, true })
{
    var presentation = CloudUiPresentation.Create(enabled, signedIn, busy, error);
    Check(presentation.IsHealthy == (enabled && signedIn && !busy && !error),
        $"Cloud ready invariant: enabled={enabled}, signedIn={signedIn}, busy={busy}, error={error}");
}
Check(UiLayout.ReportIsNarrow(799) && !UiLayout.ReportIsNarrow(800), "Report sidebar switches layout at its logical-pixel boundary");
Check(!UiLayout.ReportImageBesideTable(999) && UiLayout.ReportImageBesideTable(1000), "Report image stacks before it compresses the detail table");
Check(UiTextSearch.Matches("белый лдсп", "Белый 16 мм", "ЛДСП"), "Material search matches words across name and category");
Check(!UiTextSearch.Matches("белый дуб", "Белый 16 мм", "ЛДСП"), "Material search requires every search word");
Check(!UiLayout.ContainsPoint(-1, 25, 100, 50) && !UiLayout.ContainsPoint(100, 25, 100, 50), "Reveal extinguishes only beyond the window boundary");

var previousEnvironment = Environment.GetEnvironmentVariable("VITANCUT_ENVIRONMENT");
var previousDataRoot = Environment.GetEnvironmentVariable("VITANCUT_DATA_ROOT");
Environment.SetEnvironmentVariable("VITANCUT_ENVIRONMENT", "Development");
Environment.SetEnvironmentVariable("VITANCUT_DATA_ROOT", Path.Combine(output, "изолированные данные"));
var isolatedPaths = AppPaths.ForCurrentProcess(Path.Combine(output, "Приложение"));
Check(isolatedPaths.IsDevelopment && isolatedPaths.DatabasePath.StartsWith(output, StringComparison.OrdinalIgnoreCase),
    "Development storage is rooted in the explicit isolated directory");
var isolatedState = new AppState(isolatedPaths);
isolatedState.Load();
Check(isolatedState.DatabasePath == isolatedPaths.DatabasePath && File.Exists(isolatedPaths.DatabasePath),
    "Application database creation stays inside the explicit development root");
Environment.SetEnvironmentVariable("VITANCUT_DATA_ROOT", null);
try { _ = AppPaths.ForCurrentProcess(Path.Combine(output, "Приложение")); Check(false, "Development mode requires an isolated data root"); }
catch (InvalidOperationException) { Check(true, "Development mode requires an isolated data root"); }
Environment.SetEnvironmentVariable("VITANCUT_DATA_ROOT", previousDataRoot);
Environment.SetEnvironmentVariable("VITANCUT_ENVIRONMENT", previousEnvironment);

var deltaMigration = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "supabase", "migrations", "2026101201_secure_and_seed_delta_workspaces.sql"));
Check(deltaMigration.Contains("revoke all on function public.compose_vitan_snapshot(uuid, jsonb) from public, anon, authenticated", StringComparison.OrdinalIgnoreCase),
    "Delta snapshot composer is not directly executable by client roles");
Check(deltaMigration.Contains("perform public.seed_vitan_workspace_entities(v_workspace_id, p_payload, auth.uid(), now())", StringComparison.OrdinalIgnoreCase) &&
      deltaMigration.Contains("for v_snapshot in", StringComparison.OrdinalIgnoreCase),
    "New workspaces and legacy snapshots populate addressable cloud entities");

foreach (var viewport in new[] { 640d, 998d, 1200d, 1840d })
{
    var columns = DetailTableLayout.ColumnWidths(viewport);
    Check(Math.Abs(columns.Sum() + 70 - Math.Max(viewport, DetailTableLayout.MinimumWidth)) < .001,
        $"Detail columns fill viewport {viewport} without content-dependent widths");
    Check(columns.Length == 8 && columns[2] == 72 && columns[4] == 32 && columns[6] == 56 && columns[7] == 150 &&
        columns.Where((_, index) => index != 4).Min() >= 56,
        $"Detail fixed columns remain stable at {viewport}");
}

var swapMaterial = new Material { Unit = "m2" };
var swapDetail = new Detail { LengthExpr = "600", WidthExpr = "400", Qty = 3, AllowRotation = false };
Check(DetailDimensions.Swap(swapDetail, swapMaterial) && swapDetail.LengthExpr == "400" && swapDetail.WidthExpr == "600",
    "Detail dimension button exchanges numeric expressions");
Check(swapDetail.Qty == 3 && !swapDetail.AllowRotation,
    "Swapping detail dimensions leaves quantity and rotation permission unchanged");
swapDetail.LengthExpr = "длина изделия - 32";
swapDetail.WidthExpr = "=[[detail:stable-reference:length]] / 2";
Check(DetailDimensions.Swap(swapDetail, swapMaterial) && swapDetail.LengthExpr == "=[[detail:stable-reference:length]] / 2" &&
    swapDetail.WidthExpr == "длина изделия - 32", "Dimension swap preserves full formulas and stable references without evaluating them");
Check(DetailDimensions.Swap(swapDetail, swapMaterial) && swapDetail.LengthExpr == "длина изделия - 32" &&
    swapDetail.WidthExpr == "=[[detail:stable-reference:length]] / 2", "A second dimension swap restores both original expressions");
foreach (var unit in new[] { "lm", "pc" })
    Check(!DetailDimensions.Swap(swapDetail, new Material { Unit = unit }) && swapDetail.LengthExpr == "длина изделия - 32",
        "Dimension swap is unavailable for materials without editable width: " + unit);
swapDetail.IsGeneratedEdgeBand = true;
Check(!DetailDimensions.Swap(swapDetail, swapMaterial) && !DetailDimensions.Swap(swapDetail, null),
    "Generated edge bands and rows without a material cannot swap dimensions");

Check(AccentPalette.Options.Select(x => x.Id).Distinct().Count() == AccentPalette.Options.Count, "Accent palette IDs are unique");
Check(AppUpdateService.TryParseVersion("v1.2.3", out var parsedUpdateVersion) && parsedUpdateVersion == new Version(1, 2, 3), "Update tags accept a v-prefixed semantic version");
Check(AppUpdateService.TryParseVersion("1.2.3-beta.1", out parsedUpdateVersion) && parsedUpdateVersion == new Version(1, 2, 3), "Update tags ignore prerelease metadata when comparing versions");
Check(!AppUpdateService.TryParseVersion("latest", out _), "Invalid GitHub release tag is rejected");
Check(AppUpdateService.FormatVersion(new Version(2, 4, 0, 0)) == "2.4.0", "Update status formats local version consistently");
var orderingDate = DateTimeOffset.Parse("2026-10-22T00:00:00Z");
var catalogOrderingChoices = new[]
{
    new MaterialChoice("Wood", new Material { Id = "plain-a", Name = "A", CreatedAt = orderingDate }),
    new MaterialChoice("Wood", new Material { Id = "b-z", Name = "Z", Group = "Beta", CreatedAt = orderingDate }),
    new MaterialChoice("Wood", new Material { Id = "a-z", Name = "Z", Group = "Alpha", CreatedAt = orderingDate }),
    new MaterialChoice("Wood", new Material { Id = "plain-z", Name = "Z", Group = "  ", CreatedAt = orderingDate.AddDays(1) }),
    new MaterialChoice("Wood", new Material { Id = "b-a", Name = "A", Group = "Beta", CreatedAt = orderingDate.AddDays(1) }),
    new MaterialChoice("Wood", new Material { Id = "a-a", Name = "A", Group = " Alpha ", CreatedAt = orderingDate.AddDays(1) })
};
foreach (var mode in new[] { "name-asc", "name-desc", "date" })
{
    var orderedCatalog = MaterialCatalogOrdering.Sort(catalogOrderingChoices, mode);
    Check(orderedCatalog.Select(choice => choice.Material.Group.Trim()).SequenceEqual(["Alpha", "Alpha", "Beta", "Beta", "", ""]),
        "Named material groups always precede ungrouped rows with stable group order: " + mode);
    var expectedNames = mode == "name-desc" ? new[] { "Z", "A" } : new[] { "A", "Z" };
    Check(orderedCatalog.Take(2).Select(choice => choice.Material.Name).SequenceEqual(expectedNames) &&
        orderedCatalog.Skip(2).Take(2).Select(choice => choice.Material.Name).SequenceEqual(expectedNames),
        "Selected sorting applies independently inside each material group: " + mode);
    var expectedPlain = mode == "name-asc" ? new[] { "plain-a", "plain-z" } : new[] { "plain-z", "plain-a" };
    Check(orderedCatalog.TakeLast(2).Select(choice => choice.Material.Id).SequenceEqual(expectedPlain),
        "Ungrouped materials retain selected sorting below all groups: " + mode);
}
var pickerGroupMatches = MaterialCatalogOrdering.Sort(catalogOrderingChoices, "name-asc")
    .Where(choice => UiTextSearch.Matches("alpha Z", choice.Material.Name, choice.Type, choice.Material.Group)).ToList();
Check(pickerGroupMatches.Count == 1 && pickerGroupMatches[0].Material.Id == "a-z",
    "Detail material picker search combines group name and material name without matching unrelated groups");
Check(catalogOrderingChoices.Count(choice => UiTextSearch.Matches("Alpha", choice.Material.Name, choice.Type, choice.Material.Group)) == 2,
    "Searching a material group finds all its materials, including whitespace-normalized group names");
Check(catalogOrderingChoices.Count(choice => UiTextSearch.Matches("Wood Z", choice.Material.Name, choice.Type, choice.Material.Group)) == 3,
    "Detail material picker still searches across category and material labels");
var materialSelection = new MaterialSelectionModel();
materialSelection.SetVisibleOrder(["a", "b", "c", "d", "e"]);
materialSelection.Apply("b", control: false, shift: false);
materialSelection.Apply("d", control: false, shift: true);
Check(materialSelection.SelectedIds.SetEquals(["b", "c", "d"]), "Material Shift selects the complete visible range across group boundaries");
materialSelection.Apply("a", control: false, shift: true);
Check(materialSelection.SelectedIds.SetEquals(["a", "b"]), "Repeated Shift range uses the original anchor");
materialSelection.SetVisibleOrder(["e", "d", "c", "b", "a"]);
Check(materialSelection.SelectedIds.SetEquals(["a", "b"]), "Material selection survives a stable reorder until an explicit view reset");
materialSelection.Reset(["e", "d", "c", "b", "a"]);
Check(materialSelection.SelectedIds.Count == 0, "Category or sorting reset clears selection and its range anchor");
materialSelection.Apply("d", control: false, shift: false);
materialSelection.Apply("b", control: true, shift: false);
materialSelection.Apply("d", control: true, shift: false);
Check(materialSelection.SelectedIds.SetEquals(["b"]), "Material Ctrl toggles individual rows without clearing prior selections");
materialSelection.Reset(["a", "b", "c", "d"]);
materialSelection.Apply("b", false, false);
Check(materialSelection.Move("b", 1, false, true) == "c" && materialSelection.SelectedIds.SetEquals(["b", "c"]), "Keyboard Shift expands the ordered material range");
Check(materialSelection.Move("c", -1, false, true) == "b" && materialSelection.SelectedIds.SetEquals(["b"]), "Keyboard Shift shrinks using its original anchor");
Check(materialSelection.Move("b", 1, true, false) == "c" && materialSelection.SelectedIds.SetEquals(["b"]), "Ctrl arrow moves material focus without changing selection");
materialSelection.Apply("a", true, false);
Check(materialSelection.Move("b", 1, true, true) == "c" && materialSelection.SelectedIds.SetEquals(["a", "b", "c"]), "Ctrl Shift range retains previously selected materials");
Check(materialSelection.Move("c", int.MaxValue, false, false) == "d" && materialSelection.SelectedIds.SetEquals(["d"]), "Keyboard End reaches the last material without integer overflow");
Check(materialSelection.Move("d", int.MinValue, false, false) == "a" && materialSelection.SelectedIds.SetEquals(["a"]), "Keyboard Home reaches the first material without integer overflow");
Check(materialSelection.Move("a", -1, false, false) == "a", "Material focus does not wrap beyond the first row");
Check(materialSelection.Move("missing", 1, false, false) is null && materialSelection.SelectedIds.SetEquals(["a"]), "Missing material focus cannot alter the current selection");
materialSelection.ReplaceSelection(["b", "d"]);
var savedMaterialSelection = materialSelection.SelectedIds.Append("b").ToArray();
materialSelection.Reset(["d", "c", "b", "a"]);
materialSelection.ReplaceSelection(savedMaterialSelection);
Check(materialSelection.SelectedIds.SetEquals(["b", "d"]), "Saving a material restores selection by ID after rebuilding and reordering the list");
materialSelection.Reset(["b", "a"]);
materialSelection.ReplaceSelection(savedMaterialSelection);
Check(materialSelection.SelectedIds.SetEquals(["b"]), "Restoring material selection ignores IDs no longer in the current category");
materialSelection.Reset(["b", "a"]);
materialSelection.ReplaceSelection(materialSelection.SelectedIds.Append("b").ToArray());
Check(materialSelection.SelectedIds.SetEquals(["b"]), "Saving an editor material restores its row even when the list had no selection");
var materialRows = new System.Collections.ObjectModel.ObservableCollection<MaterialChoice>(
    new[] { "A", "B", "C" }.Select(name => new MaterialChoice("Wood", new Material { Id = name, Name = name })));
var originalMaterialRows = materialRows.ToArray();
var materialRowChanges = new List<System.Collections.Specialized.NotifyCollectionChangedAction>();
materialRows.CollectionChanged += (_, args) => materialRowChanges.Add(args.Action);
var savedRow = new MaterialChoice("Wood", materialRows[1].Material);
for (var save = 0; save < 3; save++)
{
    savedRow.Material.Cost += 10;
    Check(MaterialListUpdates.ApplySaved(materialRows, savedRow, materialRows.ToArray(), nameChanged: false),
        "Repeated material price saves update the existing list");
}
Check(materialRowChanges.Count == 0 && materialRows.Zip(originalMaterialRows).All(pair => ReferenceEquals(pair.First, pair.Second)),
    "Price saves do not reset, replace or move any material row, preserving the scroll containers");
savedRow.Material.Name = "Z";
Check(MaterialListUpdates.ApplySaved(materialRows, savedRow, [materialRows[0], materialRows[2], savedRow], nameChanged: true) &&
    materialRows.Select(item => item.Material.Id).SequenceEqual(["A", "C", "B"]),
    "Renaming updates the material label and moves only its row into sorted order");
Check(materialRowChanges.SequenceEqual(new[] { System.Collections.Specialized.NotifyCollectionChangedAction.Replace,
    System.Collections.Specialized.NotifyCollectionChangedAction.Move }) &&
    ReferenceEquals(materialRows[0], originalMaterialRows[0]) && ReferenceEquals(materialRows[1], originalMaterialRows[2]),
    "Renaming never resets the material list or replaces unrelated rows");
var groupRows = new System.Collections.ObjectModel.ObservableCollection<MaterialChoice>([savedRow]);
materialRowChanges.Clear();
groupRows.CollectionChanged += (_, args) => materialRowChanges.Add(args.Action);
Check(MaterialListUpdates.ApplySaved(groupRows, savedRow, [savedRow], nameChanged: false) && materialRowChanges.Count == 0,
    "Saving inside a material group leaves its nested list untouched");
var beforeInvalidMaterialUpdate = materialRows.ToArray();
materialRowChanges.Clear();
Check(!MaterialListUpdates.ApplySaved(materialRows, new MaterialChoice("Wood", new Material { Id = "missing" }),
    materialRows.ToArray(), nameChanged: false) && materialRowChanges.Count == 0 && materialRows.SequenceEqual(beforeInvalidMaterialUpdate),
    "A missing saved material leaves the current list unchanged for the caller to refresh safely");
var releaseSummary = AppUpdateService.SummarizeReleaseNotes("## Что нового в VitanCut 1.0.18\n\n- Исправлена прокрутка\n- Добавлена история уведомлений\n\n## Что нового в VitanCut 1.0.17\n- Старые изменения\n\n**Full Changelog**: link", "v1.0.18");
Check(releaseSummary is not null && releaseSummary.Contains("Исправлена прокрутка") && !releaseSummary.Contains("Старые изменения") && !releaseSummary.Contains("Full Changelog"), "Update notes include only the selected release version");
var updateScript = AppUpdateService.BuildUpdateScript("C:\\Users\\gmmbc\\OneDrive\\Заметки\\VitanCut", "C:\\Users\\gmmbc\\OneDrive\\Заметки\\VitanCut", "C:\\Users\\gmmbc\\OneDrive\\Заметки\\VitanCut\\VitanCut.WinUI.exe", "C:\\Temp\\update", 1234);
Check(updateScript.Contains("$source = 'C:\\Users\\gmmbc\\OneDrive\\Заметки\\VitanCut'") && updateScript.Contains("Get-Process -Id 1234") && updateScript.Contains("Start-Process -FilePath $executable") && updateScript.Contains("updater.log") && updateScript.Contains("Update failed:"),
    "Updater script preserves Unicode paths, logs failures and waits before relaunching");
Check(updateScript.Contains("/XD (Join-Path $target 'data')") && updateScript.Contains("/XD (Join-Path $source 'data')"),
    "Updater non-mirror copy excludes source and target data paths");
Check(updateScript.Contains("/MIR /XD 'data'") && updateScript.Contains("Restoring previous application files."),
    "Mirror update and failure recovery exclude the data directory by relative name");
if (OperatingSystem.IsWindows())
{
    var fixture = Path.Combine(output, "robocopy-update-fixture");
    var source = Path.Combine(fixture, "source");
    var target = Path.Combine(fixture, "target");
    var backup = Path.Combine(fixture, "backup");
    Directory.CreateDirectory(Path.Combine(source, "data"));
    Directory.CreateDirectory(Path.Combine(target, "data"));
    File.WriteAllText(Path.Combine(source, "VitanCut.WinUI.exe"), "new executable");
    File.WriteAllText(Path.Combine(source, "data", "archive-data.json"), "must not be copied");
    File.WriteAllText(Path.Combine(target, "VitanCut.WinUI.exe"), "old executable");
    File.WriteAllText(Path.Combine(target, "obsolete.dll"), "old file");
    File.WriteAllText(Path.Combine(target, "data", "database.json"), "user database");
    RunRobocopy(target, backup, "/E", "/XD", Path.Combine(target, "data"), "/NFL", "/NDL", "/NJH", "/NJS", "/NC", "/NS");
    Check(File.Exists(Path.Combine(backup, "VitanCut.WinUI.exe")) && !Directory.Exists(Path.Combine(backup, "data")),
        "Updater backup stores application files only");
    RunRobocopy(source, target, "/MIR", "/XD", "data", "/IS", "/IT", "/NFL", "/NDL", "/NJH", "/NJS", "/NC", "/NS");
    Check(File.ReadAllText(Path.Combine(target, "VitanCut.WinUI.exe")) == "new executable" &&
          !File.Exists(Path.Combine(target, "obsolete.dll")) && !File.Exists(Path.Combine(target, "data", "archive-data.json")) &&
          File.ReadAllText(Path.Combine(target, "data", "database.json")) == "user database",
        "Synthetic mirror update replaces app files while preserving existing user data");
    RunRobocopy(backup, target, "/MIR", "/XD", "data", "/IS", "/IT", "/NFL", "/NDL", "/NJH", "/NJS", "/NC", "/NS");
    Check(File.ReadAllText(Path.Combine(target, "VitanCut.WinUI.exe")) == "old executable" &&
          File.ReadAllText(Path.Combine(target, "data", "database.json")) == "user database",
        "Synthetic rollback restores prior app files without replacing user data");
    var shellFixture = Path.Combine(output, "Обновление с пробелами");
    var shellSource = Path.Combine(shellFixture, "new files");
    var shellTarget = Path.Combine(shellFixture, "installed app");
    var shellBackup = Path.Combine(shellFixture, "rollback");
    var shellWork = Path.Combine(shellFixture, "helper");
    foreach (var directory in new[] { shellSource, shellTarget, shellWork, Path.Combine(shellTarget, "data") }) Directory.CreateDirectory(directory);
    File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe"), Path.Combine(shellSource, "VitanCut.WinUI.exe"));
    File.WriteAllText(Path.Combine(shellSource, "version.txt"), "new version");
    File.WriteAllText(Path.Combine(shellTarget, "version.txt"), "old version");
    File.WriteAllText(Path.Combine(shellTarget, "data", "database.json"), "preserved database");
    var shellScript = Path.Combine(shellWork, "apply-update.ps1");
    File.WriteAllText(shellScript, AppUpdateService.BuildUpdateScript(shellSource + "\\", shellTarget + "\\",
        Path.Combine(shellTarget, "VitanCut.WinUI.exe"), shellWork, int.MaxValue, shellBackup), new System.Text.UTF8Encoding(true));
    var helperStart = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
    foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", shellScript }) helperStart.ArgumentList.Add(argument);
    helperStart.Environment["LOCALAPPDATA"] = Path.Combine(shellFixture, "isolated-profile");
    using var shellHelper = Process.Start(helperStart)!;
    if (!shellHelper.WaitForExit(30000)) { shellHelper.Kill(); throw new Exception("Update helper timed out."); }
    Check(shellHelper.ExitCode == 0 && File.ReadAllText(Path.Combine(shellTarget, "version.txt")) == "new version",
        "Actual Windows PowerShell updater replaces files with Unicode, spaces and trailing separators");
    Check(File.ReadAllText(Path.Combine(shellBackup, "version.txt")) == "old version" &&
        !Directory.Exists(Path.Combine(shellBackup, "data")) && File.ReadAllText(Path.Combine(shellTarget, "data", "database.json")) == "preserved database",
        "Actual updater creates rollback backup without copying or modifying user data");
    Check(File.ReadAllText(Path.Combine(shellFixture, "isolated-profile", "VitanCut", "logs", "updater.log")).Contains("Update completed."),
        "Actual updater reaches automatic relaunch and completion");
    Directory.CreateDirectory(shellWork);
    var blockedParent = Path.Combine(shellFixture, "blocked-backup-parent");
    File.WriteAllText(blockedParent, "not a directory");
    File.WriteAllText(Path.Combine(shellSource, "version.txt"), "must not be applied");
    File.WriteAllText(shellScript, AppUpdateService.BuildUpdateScript(shellSource + "\\", shellTarget + "\\",
        Path.Combine(shellTarget, "VitanCut.WinUI.exe"), shellWork, int.MaxValue, Path.Combine(blockedParent, "backup")), new System.Text.UTF8Encoding(true));
    using var failingHelper = Process.Start(helperStart)!;
    if (!failingHelper.WaitForExit(30000)) { failingHelper.Kill(); throw new Exception("Failure helper timed out."); }
    Check(File.ReadAllText(Path.Combine(shellTarget, "version.txt")) == "new version" &&
        File.ReadAllText(Path.Combine(shellTarget, "data", "database.json")) == "preserved database",
        "Backup failure leaves installed files and user database intact");
    var failureLog = File.ReadAllText(Path.Combine(shellFixture, "isolated-profile", "VitanCut", "logs", "updater.log"));
    Check(failureLog.Contains("Update failed:") && !failureLog.Contains("Restoring previous application files."),
        "Backup failure never mirrors an incomplete backup over the installed application");
}
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
Check(!accentState.Database.Preferences.AcrylicBackgroundEnabled, "Acrylic defaults off for existing databases");
accentState.Database.Preferences.AcrylicBackgroundEnabled = true;
accentState.Save();
accentState.Load(accentPath);
Check(accentState.Database.Preferences.AcrylicBackgroundEnabled, "Acrylic choice survives restart");
var remoteAcrylicSnapshot = System.Text.Json.Nodes.JsonNode.Parse(accentState.CreateCloudSnapshot())!;
remoteAcrylicSnapshot["preferences"]!["acrylicBackgroundEnabled"] = false;
accentState.ReplaceFromCloudSnapshot(remoteAcrylicSnapshot.ToJsonString());
Check(accentState.Database.Preferences.AcrylicBackgroundEnabled, "Cloud refresh preserves local Acrylic preference");
accentState.Database.Preferences.AcrylicBackgroundEnabled = false;
accentState.Save();
accentState.Load(accentPath);
Check(!accentState.Database.Preferences.AcrylicBackgroundEnabled, "Opaque background choice survives restart");
Check(BackdropPolicy.UseAcrylic(true, true, false), "Acrylic allowed with Windows effects");
Check(!BackdropPolicy.UseAcrylic(false, true, false), "Acrylic off overrides Windows effects");
Check(!BackdropPolicy.UseAcrylic(true, false, false), "Disabled Windows effects force opaque fallback");
Check(!BackdropPolicy.UseAcrylic(true, true, true), "High contrast forces opaque fallback");
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
var rotationAlternatives = CutOptimization.Calculate(rotatedCut);
Check(rotationAlternatives[0].Report.Groups.SelectMany(g => g.Sheets).SelectMany(s => s.Placements).All(p => p.Rotated && p.AllowRotation),
    "Automatic optimization preserves the required rotated orientation when rotation is allowed");
Check(!CuttingService.BuildDetailingReport(rotationState, rotationProject).Products[0].Rows[0].RotationLocked, "Detailing updates when rotation is enabled");
rotationMaterial.TextureDirection = true;
var texturedCut = CuttingService.BuildCutReport(rotationState, rotationProject);
Check(texturedCut.Unplaced.Count == 0 && texturedCut.Groups[0].TextureDirection &&
    texturedCut.Groups.SelectMany(g => g.Sheets).SelectMany(s => s.Placements).All(p => p.Rotated && p.AllowRotation) &&
    !CuttingService.BuildDetailingReport(rotationState, rotationProject).Products[0].Rows[0].RotationLocked,
    "Texture direction preserves material metadata without blocking explicitly allowed rotation");
Check(CutOptimization.Calculate(texturedCut).All(v => v.Report.Unplaced.Count == 0 &&
    v.Report.Groups.SelectMany(g => g.Sheets).SelectMany(s => s.Placements).All(p => p.Rotated && p.AllowRotation)),
    "Optimization allows required rotation for textured materials");
var texturedPlan = CutPlanService.Capture(texturedCut, CutPlanService.Signature(texturedCut), "Textured rotation", "test");
Check(CutPlanService.Restore(texturedCut, texturedPlan) is not null,
    "Saved textured layouts restore explicitly allowed rotations");
rotationDetail.AllowRotation = false;
Check(CuttingService.BuildCutReport(rotationState, rotationProject).Unplaced.Count == 1 && DetailRotation.IsLocked(rotationDetail, rotationMaterial),
    "Textured material still respects the individual detail rotation lock");
rotationDetail.AllowRotation = true;
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
var staleProjectState = new AppState();
staleProjectState.Load(Path.Combine(output, "stale-project.json"));
var staleProject = new Project { Id = "stable-project", Name = "Sync project", Products = [new Product { Id = "stable-product", Name = "Before sync" }] };
staleProjectState.Database.Projects.Add(staleProject);
staleProjectState.Save();
var staleProduct = staleProject.Products.Single();
staleProjectState.ReplaceFromCloudSnapshot(staleProjectState.CreateCloudSnapshot());
var currentProject = staleProjectState.Database.Projects.Single();
var editedProduct = JsonSerializer.Deserialize<Product>(JsonSerializer.Serialize(staleProduct))!;
editedProduct.Name = "Saved from open editor";
new ProjectService(staleProjectState).SaveProduct(staleProject, staleProduct, editedProduct);
Check(staleProjectState.Database.Projects.Single().Products.Single().Name == "Saved from open editor",
    "Open product editor resolves the current product instance by stable ID after cloud replacement");
var currentReport = CuttingService.BuildCutReport(staleProjectState, currentProject, applySavedLayout: false);
new ProjectService(staleProjectState).SaveCutPlans(staleProject, [], "", CutPlanService.Signature(currentReport));
Check(staleProjectState.Database.Projects.Single().Id == staleProject.Id,
    "Open report saves resolve the current project after cloud replaces its object instance");
var catalogService = new CatalogService(staleProjectState);
catalogService.AddCounterparty("Копирование источник");
catalogService.AddCounterparty("Копирование получатель");
var catalogSource = new Product
{
    Name = "Шкаф",
    Details = [
        new Detail { Id = "copy-source-detail", Name = "Бок", LengthExpr = "250", WidthExpr = "50" },
        new Detail { Id = "copy-dependent-detail", Name = "Полка", LengthExpr = "=[[detail:copy-source-detail:length]]+20", WidthExpr = "[[detail:copy-source-detail:width]]" }
    ],
    FixedSizes = [new FixedSize { Name = "Полка", Value = 300 }]
};
var originalCopyFormula = FormulaEvaluator.Evaluate(catalogSource, catalogSource.Details[1]).Value;
catalogService.SaveProduct("Копирование источник", null, catalogSource);
catalogSource = catalogService.GetProducts("Копирование источник").Single();
var staleCatalogSource = catalogSource;
staleProjectState.ReplaceFromCloudSnapshot(staleProjectState.CreateCloudSnapshot());
var editedCatalogSource = JsonSerializer.Deserialize<Product>(JsonSerializer.Serialize(staleCatalogSource))!;
editedCatalogSource.Name = "Каталог сохранён после замены";
catalogService.SaveProduct("Копирование источник", staleCatalogSource, editedCatalogSource);
catalogSource = catalogService.GetProducts("Копирование источник").Single();
Check(catalogSource.Name == "Каталог сохранён после замены",
    "Open catalog editor updates the current product instance by stable ID after cloud replacement");
catalogService.CopyProduct("Копирование источник", catalogSource, "Копирование получатель");
var catalogCopy = catalogService.GetProducts("Копирование получатель").Single();
Check(catalogCopy.Id != catalogSource.Id && catalogCopy.Details[0].Id != catalogSource.Details[0].Id &&
    catalogCopy.FixedSizes[0].Id != catalogSource.FixedSizes[0].Id,
    "Catalog copy creates independent product, detail, and fixed-size identities");
Check(FormulaEvaluator.Evaluate(catalogCopy, catalogCopy.Details[1]).Value == originalCopyFormula &&
      catalogCopy.Details[1].LengthExpr.Contains(catalogCopy.Details[0].Id, StringComparison.Ordinal),
    "Catalog copy remaps formula references while preserving calculated dimensions");
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
var transactionPath = Path.Combine(output, "transaction.json");
var transactionState = new AppState();
transactionState.Load(transactionPath);
var transactionOriginal = transactionState.ExportJson();
var transactionRevision = transactionState.Revision;
Directory.CreateDirectory(transactionPath + ".bak");
var transactionFailed = false;
try { transactionState.CommitMutation(() => transactionState.Database.Counterparties.Add("Не записано")); }
catch (Exception error) when (error is IOException or UnauthorizedAccessException) { transactionFailed = true; }
Check(transactionFailed && transactionState.Database.Counterparties.All(name => name != "Не записано") &&
      transactionState.ExportJson() == transactionOriginal && transactionState.Revision == transactionRevision,
    "Failed durable commit restores both in-memory database and revision");
var serializationState = new AppState();
serializationState.Load(Path.Combine(output, "pure-serialization", "database.json"));
var unnormalizedProject = new Project { Id = "", CreatedAt = default, UpdatedAt = default };
serializationState.Database.Projects.Add(unnormalizedProject);
var serializationRevision = serializationState.Revision;
_ = serializationState.CreateCloudSnapshot();
_ = serializationState.ExportJson();
Check(unnormalizedProject.Id == "" && unnormalizedProject.CreatedAt == default &&
      unnormalizedProject.UpdatedAt == default && serializationState.Revision == serializationRevision,
    "Cloud and file serialization do not mutate the in-memory database");
serializationState.Save();
Check(!string.IsNullOrWhiteSpace(unnormalizedProject.Id) && unnormalizedProject.CreatedAt != default &&
      unnormalizedProject.UpdatedAt != default,
    "Durable save still normalizes legacy and incomplete fields before writing");
var atomicJsonPath = Path.Combine(output, "atomic-cloud-state.json");
AtomicJsonFile.Write(atomicJsonPath, "{\"revision\":1}");
Reject(() => AtomicJsonFile.Write(atomicJsonPath, "{broken"), "Invalid JSON is rejected before replacing a durable cloud-state file");
Check(File.ReadAllText(atomicJsonPath) == "{\"revision\":1}" && !Directory.EnumerateFiles(output, "atomic-cloud-state.json.*.tmp").Any(),
    "Failed atomic JSON write preserves the previous file and removes its temporary file");
var pairDirectory = Path.Combine(output, "atomic-pair");
Directory.CreateDirectory(pairDirectory);
var pairFirst = Path.Combine(pairDirectory, "settings.json");
var pairSecond = Path.Combine(pairDirectory, "baseline.json");
AtomicJsonFile.Write(pairFirst, "{\"generation\":1}");
AtomicJsonFile.Write(pairSecond, "{\"generation\":1}");
AtomicJsonPairFile.Write(pairFirst, "{\"generation\":2}", pairSecond, "{\"generation\":2}");
Check(JsonDocument.Parse(File.ReadAllText(pairFirst)).RootElement.GetProperty("generation").GetInt32() == 2 &&
      JsonDocument.Parse(File.ReadAllText(pairSecond)).RootElement.GetProperty("generation").GetInt32() == 2,
    "Atomic JSON checkpoint commits both cloud metadata files");
foreach (var failedCheckpoint in new[] { "prepared", "first-replaced", "second-replaced" })
{
    var failureInjected = false;
    try
    {
        AtomicJsonPairFile.Write(pairFirst, "{\"generation\":3}", pairSecond, "{\"generation\":3}",
            checkpoint => { if (checkpoint == failedCheckpoint) throw new IOException("Synthetic crash boundary."); });
    }
    catch (IOException error) when (error.Message == "Synthetic crash boundary.") { failureInjected = true; }
    Check(failureInjected, $"Atomic JSON checkpoint failure injected at {failedCheckpoint}");
    Check(JsonDocument.Parse(File.ReadAllText(pairFirst)).RootElement.GetProperty("generation").GetInt32() == 2 &&
          JsonDocument.Parse(File.ReadAllText(pairSecond)).RootElement.GetProperty("generation").GetInt32() == 2,
        $"Atomic JSON checkpoint restores both prior files after {failedCheckpoint}");
}
var tripleDatabase = Path.Combine(pairDirectory, "database.json");
AtomicJsonFile.Write(tripleDatabase, "{\"generation\":2}");
foreach (var failedCheckpoint in new[] { "prepared", "first-replaced", "second-replaced", "third-replaced" })
{
    var failureInjected = false;
    try
    {
        AtomicJsonPairFile.Write(pairFirst, "{\"generation\":3}", pairSecond, "{\"generation\":3}",
            tripleDatabase, "{\"generation\":3}",
            checkpoint => { if (checkpoint == failedCheckpoint) throw new IOException("Synthetic triple crash boundary."); });
    }
    catch (IOException error) when (error.Message == "Synthetic triple crash boundary.") { failureInjected = true; }
    Check(failureInjected && new[] { pairFirst, pairSecond, tripleDatabase }.All(path =>
          JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("generation").GetInt32() == 2),
        $"Three-file cloud checkpoint restores all previous files after {failedCheckpoint}");
}
var tripleCommittedFailure = false;
try
{
    AtomicJsonPairFile.Write(pairFirst, "{\"generation\":4}", pairSecond, "{\"generation\":4}",
        tripleDatabase, "{\"generation\":4}",
        checkpoint => { if (checkpoint == "committed") throw new IOException("Synthetic committed triple failure."); });
}
catch (IOException error) when (error.Message == "Synthetic committed triple failure.") { tripleCommittedFailure = true; }
Check(tripleCommittedFailure && new[] { pairFirst, pairSecond, tripleDatabase }.All(path =>
      JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("generation").GetInt32() == 4),
    "Three-file cloud checkpoint preserves every file after its commit marker");
var temporaryWriteFailure = false;
try
{
    AtomicJsonPairFile.Write(pairFirst, "{\"generation\":5}", pairSecond, "{\"generation\":5}",
        tripleDatabase, "{\"generation\":5}",
        checkpoint => { if (checkpoint == "before-first-temporary-write") throw new IOException("Synthetic disk-full during temp write."); });
}
catch (IOException error) when (error.Message == "Synthetic disk-full during temp write.") { temporaryWriteFailure = true; }
Check(temporaryWriteFailure && new[] { pairFirst, pairSecond, tripleDatabase }.All(path =>
      JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("generation").GetInt32() == 4) &&
      !Directory.EnumerateFiles(pairDirectory, "*.tmp").Any(),
    "Temporary-write failure preserves all prior targets and removes incomplete files");
var deniedReplacement = false;
try
{
    AtomicJsonPairFile.Write(pairFirst, "{\"generation\":5}", pairSecond, "{\"generation\":5}",
        tripleDatabase, "{\"generation\":5}",
        checkpoint => { if (checkpoint == "before-third-replace") throw new UnauthorizedAccessException("Synthetic access denied."); });
}
catch (UnauthorizedAccessException error) when (error.Message == "Synthetic access denied.") { deniedReplacement = true; }
Check(deniedReplacement && new[] { pairFirst, pairSecond, tripleDatabase }.All(path =>
      JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("generation").GetInt32() == 4),
    "Access-denied replacement rolls all three checkpoint files back");
var postCommitFailureInjected = false;
try
{
    AtomicJsonPairFile.Write(pairFirst, "{\"generation\":4}", pairSecond, "{\"generation\":4}",
        checkpoint => { if (checkpoint == "committed") throw new IOException("Synthetic post-commit crash."); });
}
catch (IOException error) when (error.Message == "Synthetic post-commit crash.") { postCommitFailureInjected = true; }
Check(postCommitFailureInjected, "Atomic JSON checkpoint simulates a failure after the commit marker");
Check(JsonDocument.Parse(File.ReadAllText(pairFirst)).RootElement.GetProperty("generation").GetInt32() == 4 &&
      JsonDocument.Parse(File.ReadAllText(pairSecond)).RootElement.GetProperty("generation").GetInt32() == 4,
    "Atomic JSON checkpoint preserves both committed files after commit marker");

var recoveryFirst = Path.Combine(pairDirectory, "recover-settings.json");
var recoverySecond = Path.Combine(pairDirectory, "recover-baseline.json");
var recoveryFirstBackup = recoveryFirst + ".crash.bak";
var recoverySecondBackup = recoverySecond + ".crash.bak";
File.WriteAllText(recoveryFirstBackup, "{\"generation\":5}");
File.WriteAllText(recoverySecondBackup, "{\"generation\":5}");
File.WriteAllText(recoveryFirst, "{\"generation\":6}");
File.WriteAllText(recoverySecond, "{\"generation\":6}");
var recoveryJournal = recoveryFirst + ".pair-journal.json";
AtomicJsonFile.Write(recoveryJournal, JsonSerializer.Serialize(new
{
    FirstPath = recoveryFirst,
    FirstTemporaryPath = recoveryFirst + ".crash.tmp",
    FirstBackupPath = recoveryFirstBackup,
    FirstExisted = true,
    SecondPath = recoverySecond,
    SecondTemporaryPath = recoverySecond + ".crash.tmp",
    SecondBackupPath = recoverySecondBackup,
    SecondExisted = true,
    Phase = "prepared"
}));
AtomicJsonPairFile.Recover(recoveryJournal, recoveryFirst, recoverySecond);
Check(JsonDocument.Parse(File.ReadAllText(recoveryFirst)).RootElement.GetProperty("generation").GetInt32() == 5 &&
      JsonDocument.Parse(File.ReadAllText(recoverySecond)).RootElement.GetProperty("generation").GetInt32() == 5 &&
      !File.Exists(recoveryJournal),
    "Startup recovery rolls back a partially replaced cloud metadata pair");
var startupRecoveryDirectory = Path.Combine(output, "checkpoint-startup-recovery");
Directory.CreateDirectory(startupRecoveryDirectory);
var startupDatabase = Path.Combine(startupRecoveryDirectory, "database.json");
var startupSettings = Path.Combine(startupRecoveryDirectory, "vitan-cut-cloud.json");
var startupBaseline = Path.Combine(startupRecoveryDirectory, "vitan-cut-cloud-baseline.json");
var oldDatabase = "{\"projects\":[],\"materials\":{},\"catalog\":{},\"counterparties\":[\"До транзакции\"],\"materialGroups\":[\"ЛДСП\"],\"preferences\":{}}";
foreach (var (target, oldJson, newJson) in new[]
{
    (startupDatabase, oldDatabase, "{broken"),
    (startupSettings, "{\"generation\":1}", "{\"generation\":2}"),
    (startupBaseline, "{\"generation\":1}", "{\"generation\":2}")
})
{
    File.WriteAllText(target + ".startup.bak", oldJson);
    File.WriteAllText(target, newJson);
}
AtomicJsonFile.Write(startupSettings + ".pair-journal.json", JsonSerializer.Serialize(new
{
    FirstPath = startupSettings,
    FirstTemporaryPath = startupSettings + ".startup.tmp",
    FirstBackupPath = startupSettings + ".startup.bak",
    FirstExisted = true,
    SecondPath = startupBaseline,
    SecondTemporaryPath = startupBaseline + ".startup.tmp",
    SecondBackupPath = startupBaseline + ".startup.bak",
    SecondExisted = true,
    Phase = "prepared",
    ThirdPath = startupDatabase,
    ThirdTemporaryPath = startupDatabase + ".startup.tmp",
    ThirdBackupPath = startupDatabase + ".startup.bak",
    ThirdExisted = true
}));
var startupRecoveredState = new AppState();
startupRecoveredState.Load(startupDatabase);
Check(startupRecoveredState.Database.Counterparties.Contains("До транзакции") &&
      !File.Exists(startupSettings + ".pair-journal.json"),
    "AppState recovers the complete cloud checkpoint before reading database.json at startup");
var absentFirst = Path.Combine(pairDirectory, "new-settings.json");
var absentSecond = Path.Combine(pairDirectory, "new-baseline.json");
try
{
    AtomicJsonPairFile.Write(absentFirst, "{}", absentSecond, "{}",
        checkpoint => { if (checkpoint == "first-replaced") throw new IOException("Synthetic new-file failure."); });
}
catch (IOException error) when (error.Message == "Synthetic new-file failure.") { }
Check(!File.Exists(absentFirst) && !File.Exists(absentSecond),
    "Atomic JSON checkpoint rollback removes newly created files when neither target existed");

var unsafeJournal = Path.Combine(pairDirectory, "unsafe-settings.json.pair-journal.json");
var outsideFixture = Path.Combine(output, "must-not-be-touched.json");
File.WriteAllText(outsideFixture, "preserve");
AtomicJsonFile.Write(unsafeJournal, JsonSerializer.Serialize(new
{
    FirstPath = Path.Combine(pairDirectory, "unsafe-settings.json"),
    FirstTemporaryPath = Path.Combine(pairDirectory, "unsafe-settings.json.attack.tmp"),
    FirstBackupPath = outsideFixture,
    FirstExisted = true,
    SecondPath = Path.Combine(pairDirectory, "unsafe-baseline.json"),
    SecondTemporaryPath = Path.Combine(pairDirectory, "unsafe-baseline.json.attack.tmp"),
    SecondBackupPath = Path.Combine(pairDirectory, "unsafe-baseline.json.attack.bak"),
    SecondExisted = true,
    Phase = "prepared"
}));
Reject(() => AtomicJsonPairFile.Recover(unsafeJournal, Path.Combine(pairDirectory, "unsafe-settings.json"),
    Path.Combine(pairDirectory, "unsafe-baseline.json")), "Malformed transaction journal rejects paths outside its directory");
Check(File.ReadAllText(outsideFixture) == "preserve", "Rejected transaction journal leaves outside files untouched");
Reject(() =>
{
    var duplicateState = new AppState();
    duplicateState.Load(Path.Combine(output, "duplicate-ids.json"));
    duplicateState.ImportJson("""{"projects":[{"id":"same"},{"id":"same"}],"materials":{},"catalog":{}}""");
}, "Duplicate project identities are rejected instead of silently merged");
var preferenceState = new AppState();
preferenceState.Load(Path.Combine(output, "local-preferences.json"));
preferenceState.Database.Preferences.Theme = "dark";
preferenceState.Database.Preferences.AccentColor = "teal";
preferenceState.Database.Preferences.CompactMode = true;
preferenceState.Database.Preferences.AnimationsEnabled = false;
preferenceState.Database.Preferences.CloudPublishIntervalMinutes = 60;
preferenceState.Save();
preferenceState.ReplaceFromCloudSnapshot("""{"projects":[],"materials":{},"catalog":{},"preferences":{"theme":"light","accentColor":"coral","compactMode":false,"animationsEnabled":true,"cutPalette":"orange","cutTrim":14,"cutGap":8,"cloudPublishIntervalMinutes":5}}""");
Check(preferenceState.Database.Preferences.Theme == "dark" && preferenceState.Database.Preferences.AccentColor == "teal" &&
      preferenceState.Database.Preferences.CompactMode && !preferenceState.Database.Preferences.AnimationsEnabled &&
      preferenceState.Database.Preferences.CloudPublishIntervalMinutes == 60 && preferenceState.Database.Preferences.CutTrim == 14 &&
      preferenceState.Database.Preferences.CutPalette == "orange",
    "Cloud snapshot replaces cutting settings while preserving local appearance and sync interval");
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
Check(!new CloudSyncService(new AppState()).IsEnabled,
    "Cloud sync fails closed until an explicit production or fake transport is configured");
Check(!cloud.IsSignedIn && !cloud.HasUnsyncedLocalChanges,
    "Cloud sync starts without a stored session or unsynced snapshot");
Check(!(await cloud.SignInAsync("", "")).Succeeded,
    "Cloud sync requires account credentials before requesting Supabase");
Check(!cloud.IsEnabled && !(await cloud.SignInAsync("test@example.invalid", "synthetic-password")).Succeeded,
    "Explicit-path test databases cannot send requests to production Supabase");
var isolatedUpdater = new AppUpdateService(cloudState.Paths!);
Check((await isolatedUpdater.CheckAsync()).Availability == UpdateAvailability.Unavailable && !isolatedUpdater.CanRollback,
    "Development mode blocks GitHub update checks and rollback");
var remoteMergeState = new AppState();
remoteMergeState.Load(Path.Combine(output, "remote-merge-cloud", "database.json"));
var remoteMergeHandler = new RemoteRevisionHttpHandler();
var remoteMergeCloud = new CloudSyncService(remoteMergeState, new HttpClient(remoteMergeHandler), "https://remote-merge.test", "fixture-key");
remoteMergeCloud.Load();
Check((await remoteMergeCloud.SignInAsync("test@example.invalid", "synthetic-password")).Succeeded,
    "Fake cloud establishes an isolated revision baseline before remote merge test");
remoteMergeState.CommitMutation(() => remoteMergeState.Database.Projects.Add(new Project { Id = "local-project", Name = "Локальный проект" }));
remoteMergeHandler.AdvanceRemoteRevision();
var remoteMergeResult = await remoteMergeCloud.PublishAsync();
Check(remoteMergeResult.Succeeded && remoteMergeHandler.FullSnapshotReads == 1 &&
      remoteMergeHandler.PublishedExpectedRevision == 2 &&
      remoteMergeState.Database.Projects.Any(project => project.Id == "local-project") &&
      remoteMergeState.Database.Projects.Any(project => project.Id == "remote-project"),
    "Remote revision change fetches its payload, merges both projects and publishes against that revision");
var fakeState = new AppState();
fakeState.Load(Path.Combine(output, "fake-cloud", "database.json"));
var fakeHandler = new CountingHttpHandler();
var fakeCloud = new CloudSyncService(fakeState, new HttpClient(fakeHandler), "https://cloud-fixture.test", "fixture-key");
fakeCloud.Load();
await Task.WhenAll(
    fakeCloud.SignInAsync("first@example.invalid", "synthetic"),
    fakeCloud.SignInAsync("second@example.invalid", "synthetic"));
Check(fakeHandler.MaximumConcurrentRequests == 1,
    "Concurrent cloud operations are serialized through the service gate");

var slowCloudState = new AppState();
slowCloudState.Load(Path.Combine(output, "slow-cloud", "database.json"));
var slowHandler = new SlowPublishHttpHandler();
var slowCloud = new CloudSyncService(slowCloudState, new HttpClient(slowHandler), "https://slow-cloud.test", "fixture-key");
slowCloud.Load();
Check((await slowCloud.SignInAsync("test@example.invalid", "synthetic-password")).Succeeded,
    "Fake cloud login establishes an isolated baseline for the delayed-publish test");
slowCloudState.CommitMutation(() =>
    slowCloudState.Database.Materials["ЛДСП"].Add(new Material { Id = "publish-first", Name = "Первая локальная правка", Unit = "m2", SheetLength = 2800, SheetWidth = 2070 }));
var pendingPublish = slowCloud.PublishAsync();
await slowHandler.PublishStarted;
Check(slowCloud.IsOperationInProgress,
    "Cloud lifecycle exposes an in-flight publish so window shutdown can await the serialized operation");
slowCloudState.CommitMutation(() => slowCloudState.Database.Projects.Add(new Project { Id = "publish-during-flight", Name = "Правка во время отправки" }));
slowHandler.ReleasePublish();
var delayedPublishResult = await pendingPublish;
Check(delayedPublishResult.Succeeded && slowCloudState.Database.Projects.Any(project => project.Id == "publish-during-flight") &&
      slowCloudState.Database.Materials["ЛДСП"].Any(material => material.Id == "publish-first") && slowCloud.HasUnsyncedLocalChanges && !slowCloud.IsOperationInProgress,
    "Delayed publish acknowledgement preserves edits made in flight and leaves them pending for the next cycle");
var restartPreviousEnvironment = Environment.GetEnvironmentVariable("VITANCUT_ENVIRONMENT");
var restartPreviousDataRoot = Environment.GetEnvironmentVariable("VITANCUT_DATA_ROOT");
var restartPreviousDatabasePath = Environment.GetEnvironmentVariable("VITANCUT_DATABASE_PATH");
AppState restartedPendingState;
try
{
    Environment.SetEnvironmentVariable("VITANCUT_ENVIRONMENT", null);
    Environment.SetEnvironmentVariable("VITANCUT_DATA_ROOT", null);
    Environment.SetEnvironmentVariable("VITANCUT_DATABASE_PATH", slowCloudState.DatabasePath);
    restartedPendingState = new AppState();
    restartedPendingState.Load();
}
finally
{
    Environment.SetEnvironmentVariable("VITANCUT_ENVIRONMENT", restartPreviousEnvironment);
    Environment.SetEnvironmentVariable("VITANCUT_DATA_ROOT", restartPreviousDataRoot);
    Environment.SetEnvironmentVariable("VITANCUT_DATABASE_PATH", restartPreviousDatabasePath);
}
var restartedPendingCloud = new CloudSyncService(restartedPendingState,
    new HttpClient(new CountingHttpHandler()), "https://restart-pending.test", "fixture-key");
restartedPendingCloud.Load();
Check(restartedPendingCloud.HasUnsyncedLocalChanges,
    "Edits made during an acknowledged publish remain detectable as pending after application restart");
Check(slowHandler.RevisionPagesRequested == 2,
    "Cloud entity revisions are fetched in stable pages beyond the PostgREST row limit");
var importedCloudState = new AppState();
importedCloudState.Load(Path.Combine(output, "imported-cloud", "database.json"));
importedCloudState.CommitMutation(() => importedCloudState.Database.Projects.Add(new Project { Id = "imported-before-login", Name = "Импортированная локальная база" }));
var importedHandler = new SlowPublishHttpHandler();
var importedCloud = new CloudSyncService(importedCloudState, new HttpClient(importedHandler), "https://imported-cloud.test", "fixture-key");
importedCloud.Load();
var importedSignIn = importedCloud.SignInAsync("test@example.invalid", "synthetic-password");
await importedHandler.PublishStarted.WaitAsync(TimeSpan.FromSeconds(5));
importedHandler.ReleasePublish();
Check((await importedSignIn).Succeeded && importedCloudState.Database.Projects.Any(project => project.Id == "imported-before-login"),
    "Sign-in publishes an imported local database without re-entering the operation queue");
var refreshingState = new AppState();
refreshingState.Load(Path.Combine(output, "refreshing-cloud", "database.json"));
var refreshingHandler = new RefreshingHttpHandler();
var refreshingCloud = new CloudSyncService(refreshingState, new HttpClient(refreshingHandler), "https://refreshing-cloud.test", "fixture-key");
refreshingCloud.Load();
Check((await refreshingCloud.SignInAsync("test@example.invalid", "synthetic-password")).Succeeded &&
      refreshingHandler.RefreshRequests == 1 && refreshingHandler.RetriedWithFreshToken,
    "Expired cloud access token refreshes once and retries the authorized request");
var rejectedRefreshState = new AppState();
rejectedRefreshState.Load(Path.Combine(output, "rejected-refresh-cloud", "database.json"));
var rejectedRefreshHandler = new RefreshingHttpHandler(rejectRefresh: true);
var rejectedRefreshCloud = new CloudSyncService(rejectedRefreshState, new HttpClient(rejectedRefreshHandler), "https://rejected-refresh.test", "fixture-key");
rejectedRefreshCloud.Load();
var rejectedRefresh = await rejectedRefreshCloud.SignInAsync("test@example.invalid", "synthetic-password");
Check(!rejectedRefresh.Succeeded && rejectedRefreshHandler.RefreshRequests == 1 && !rejectedRefreshCloud.IsSignedIn &&
      rejectedRefresh.Message.Contains("Сеанс входа истёк", StringComparison.OrdinalIgnoreCase),
    "Rejected refresh token disconnects the session with a human error after one attempt");

var retryState = new AppState();
retryState.Load(Path.Combine(output, "retry-cloud", "database.json"));
var retryHandler = new RetryableReadHttpHandler(HttpStatusCode.TooManyRequests, failuresBeforeSuccess: 2, retryAfter: TimeSpan.FromMilliseconds(250));
var retryCloud = new CloudSyncService(retryState, new HttpClient(retryHandler), "https://retry-cloud.test", "fixture-key");
retryCloud.Load();
var retryWatch = Stopwatch.StartNew();
var retryResult = await retryCloud.SignInAsync("test@example.invalid", "synthetic-password");
retryWatch.Stop();
Check(retryResult.Succeeded && retryHandler.SnapshotReadAttempts == 3 && retryWatch.Elapsed >= TimeSpan.FromMilliseconds(400),
    "Cloud GET retries a bounded number of times after 429 and honors Retry-After");

var retryExhaustedState = new AppState();
retryExhaustedState.Load(Path.Combine(output, "retry-exhausted-cloud", "database.json"));
var retryExhaustedHandler = new RetryableReadHttpHandler(HttpStatusCode.ServiceUnavailable, failuresBeforeSuccess: int.MaxValue);
var retryExhaustedCloud = new CloudSyncService(retryExhaustedState, new HttpClient(retryExhaustedHandler), "https://retry-exhausted.test", "fixture-key");
retryExhaustedCloud.Load();
var retryExhausted = await retryExhaustedCloud.SignInAsync("test@example.invalid", "synthetic-password");
Check(!retryExhausted.Succeeded && retryExhaustedHandler.SnapshotReadAttempts == 3 &&
      retryExhausted.Message.Contains("временно недоступен", StringComparison.OrdinalIgnoreCase),
    "Cloud GET stops after three 5xx attempts and returns a human-readable failure");

foreach (var simulateTimeout in new[] { false, true })
{
    var networkState = new AppState();
    networkState.Load(Path.Combine(output, simulateTimeout ? "timeout-retry-cloud" : "network-retry-cloud", "database.json"));
    var networkHandler = new RetryableReadHttpHandler(HttpStatusCode.ServiceUnavailable, failuresBeforeSuccess: 2,
        throwNetworkFailure: !simulateTimeout, throwTimeout: simulateTimeout);
    var networkCloud = new CloudSyncService(networkState, new HttpClient(networkHandler), "https://network-retry.test", "fixture-key");
    networkCloud.Load();
    Check((await networkCloud.SignInAsync("test@example.invalid", "synthetic-password")).Succeeded &&
          networkHandler.SnapshotReadAttempts == 3,
        simulateTimeout
            ? "Cloud GET retries transient timeouts and then succeeds"
            : "Cloud GET retries transient network failures and then succeeds");
}

var postFailureState = new AppState();
postFailureState.Load(Path.Combine(output, "post-failure-cloud", "database.json"));
var postFailureHandler = new AuthPostFailureHttpHandler();
var postFailureCloud = new CloudSyncService(postFailureState, new HttpClient(postFailureHandler), "https://post-failure.test", "fixture-key");
postFailureCloud.Load();
var postFailure = await postFailureCloud.SignInAsync("test@example.invalid", "synthetic-password");
Check(!postFailure.Succeeded && postFailureHandler.AuthRequests == 1,
    "Cloud POST is not automatically retried to avoid duplicating writes");

var cancelState = new AppState();
cancelState.Load(Path.Combine(output, "cancel-cloud", "database.json"));
var cancelHandler = new SlowPublishHttpHandler();
var cancelCloud = new CloudSyncService(cancelState, new HttpClient(cancelHandler), "https://cancel-cloud.test", "fixture-key");
cancelCloud.Load();
Check((await cancelCloud.SignInAsync("test@example.invalid", "synthetic-password")).Succeeded,
    "Cancellation fixture establishes its isolated cloud baseline");
cancelState.CommitMutation(() => cancelState.Database.Projects.Add(new Project { Id = "cancel-preserved", Name = "Останется локально" }));
using (var cancellation = new CancellationTokenSource())
{
    var canceledPublish = cancelCloud.PublishAsync(cancellation.Token);
    await cancelHandler.PublishStarted.WaitAsync(TimeSpan.FromSeconds(5));
    cancellation.Cancel();
    var canceledResult = await canceledPublish;
    Check(!canceledResult.Succeeded && canceledResult.Message.Contains("отменена", StringComparison.OrdinalIgnoreCase) &&
          cancelState.Database.Projects.Any(project => project.Id == "cancel-preserved"),
        "Canceling an in-flight publish keeps the local database intact");
}

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
var textureState = new AppState();
var textureDatabasePath = Path.Combine(output, "material-textures.json");
textureState.Load(textureDatabasePath);
var textureLibrary = new MaterialService(textureState);
foreach (var unit in new[] { "m2", "lm", "pc" })
{
    var texturedMaterial = new Material { Name = "Texture " + unit, Unit = unit,
        Texture = "data:image/png;base64,iVBORw0KGgo=" };
    textureLibrary.Add("Test " + unit, texturedMaterial);
    var textureChoice = textureLibrary.Save(new MaterialChoice("Test " + unit, texturedMaterial), "Moved " + unit);
    var reloadedTextures = new AppState();
    reloadedTextures.Load(textureDatabasePath);
    Check(reloadedTextures.Database.Materials[textureChoice.Type].Single().Texture == texturedMaterial.Texture,
        "Material texture survives save, category move and restart for " + unit);
}
var freshMaterial = new Material { Name = textureLibrary.CreateUniqueName("Новый материал") };
textureLibrary.Add("Fresh material", freshMaterial);
var freshMaterialState = new AppState();
freshMaterialState.Load(textureDatabasePath);
Check(freshMaterialState.Database.Materials["Fresh material"].Single().Texture == string.Empty &&
    freshMaterialState.Database.Materials["Moved pc"].Single().Texture.Length > 0,
    "New material starts without a texture and leaves existing material textures intact");
Check(library.ExistsName("  ручка  "), "Duplicate name ignores case and outer spaces");
var scrollFixtureState = new AppState();
scrollFixtureState.Load(Path.Combine(output, "material-scroll-fixture.json"));
scrollFixtureState.Database.Materials.Clear();
foreach (var prefix in new[] { "Material", "Grouped material" })
    scrollFixtureState.Database.Materials[prefix] = Enumerable.Range(1, 80)
        .Select(index => new Material { Name = $"{prefix} {index:000}", Cost = 100, Unit = "pc",
            Group = prefix == "Material" ? "" : $"Group {(index - 1) / 20 + 1}" }).ToList();
scrollFixtureState.Save();
var scrollFixtureLibrary = new MaterialService(scrollFixtureState);
Check(scrollFixtureState.MaterialChoices().All(choice => !scrollFixtureLibrary.ExistsName(choice.Material.Name, choice.Material)),
    "Long scroll fixtures have globally unique names and never reject saving an unchanged material name");
foreach (var prefix in new[] { "Material", "Grouped material" })
{
    var scrollFixtureChoice = scrollFixtureLibrary.GetChoices(prefix)[26];
    for (var save = 0; save < 3; save++)
    {
        scrollFixtureChoice.Material.Cost += 10;
        scrollFixtureChoice = scrollFixtureLibrary.Save(scrollFixtureChoice, prefix);
    }
    Check(scrollFixtureChoice.Material.Cost == 130 &&
        !scrollFixtureLibrary.ExistsName(scrollFixtureChoice.Material.Name, scrollFixtureChoice.Material),
        "Repeated saves in a long flat or grouped list do not report a duplicate name: " + prefix);
}
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
foreach (var detailCount in new[] { 50, 500, 2000 })
{
    var profileState = new AppState();
    profileState.Load(Path.Combine(output, $"profile-{detailCount}", "database.json"));
    var sheet = profileState.Database.Materials["ЛДСП"].First();
    var profileProject = new Project
    {
        Name = $"Synthetic profile {detailCount}",
        Products =
        [
            new Product
            {
                Name = "Synthetic cabinet",
                Details = Enumerable.Range(0, detailCount)
                    .Select(index => new Detail
                    {
                        Name = $"Panel {index}", Type = "ЛДСП", MaterialId = sheet.Id,
                        LengthExpr = "100", WidthExpr = "80", Qty = 1
                    }).ToList()
            }
        ]
    };
    var saveWatch = Stopwatch.StartNew();
    profileState.CommitMutation(() => profileState.Database.Projects.Add(profileProject));
    saveWatch.Stop();
    profileState.Save();
    profileState.Save();
    var saveSamples = new double[5];
    for (var index = 0; index < saveSamples.Length; index++)
    {
        var sample = Stopwatch.StartNew();
        profileState.Save();
        saveSamples[index] = sample.Elapsed.TotalMilliseconds;
    }
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    _ = CuttingService.BuildCutReport(profileState, profileProject, applySavedLayout: false);
    GC.Collect();
    var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
    var cutSamples = new double[3];
    CutReport? profileCut = null;
    for (var index = 0; index < cutSamples.Length; index++)
    {
        var sample = Stopwatch.StartNew();
        profileCut = CuttingService.BuildCutReport(profileState, profileProject, applySavedLayout: false);
        cutSamples[index] = sample.Elapsed.TotalMilliseconds;
    }
    var allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
    var memoryAfter = GC.GetTotalMemory(forceFullCollection: true);
    Array.Sort(saveSamples);
    Array.Sort(cutSamples);
    if (profileCut is null)
        throw new InvalidOperationException("Synthetic cut profile returned no report.");
    var placementCount = profileCut.Groups.SelectMany(group => group.Sheets).Sum(cutSheet => cutSheet.Placements.Count);
    Check(placementCount + profileCut.Unplaced.Count == detailCount, $"Performance fixture preserves all {detailCount} synthetic details");
    Console.WriteLine($"PERF {detailCount}: commit={saveWatch.Elapsed.TotalMilliseconds:F1}ms; save-median={saveSamples[2]:F1}ms; cut-median={cutSamples[1]:F1}ms; json={new FileInfo(profileState.DatabasePath).Length}B; cut-allocated-avg={allocated / cutSamples.Length}B; retained-memory-delta={memoryAfter - memoryBefore}B");
}
Console.WriteLine($"{checks} checks passed. Artifacts: {output}");

sealed class RemoteRevisionHttpHandler : HttpMessageHandler
{
    private const string WorkspaceId = "5c627314-9e62-49bd-9735-2a96bf5b3850";
    private const string EmptySnapshot = "{\"projects\":[],\"catalog\":{},\"materials\":{},\"materialGroups\":[\"ЛДСП\",\"Фурнитура\",\"Кромка\",\"Прочее\"],\"preferences\":{}}";
    private const string RemoteSnapshot = "{\"projects\":[{\"id\":\"remote-project\",\"name\":\"Удалённый проект\"}],\"catalog\":{},\"materials\":{},\"materialGroups\":[\"ЛДСП\",\"Фурнитура\",\"Кромка\",\"Прочее\"],\"preferences\":{}}";
    private int _remoteRevision = 1;
    private int _fullSnapshotReads;
    private long _publishedExpectedRevision;

    public int FullSnapshotReads => Volatile.Read(ref _fullSnapshotReads);
    public long PublishedExpectedRevision => Interlocked.Read(ref _publishedExpectedRevision);
    public void AdvanceRemoteRevision() => Interlocked.Exchange(ref _remoteRevision, 2);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/auth/v1/token")
            return Json("{\"access_token\":\"remote-access\",\"refresh_token\":\"remote-refresh\",\"expires_in\":3600}");
        if (path == "/rest/v1/workspaces") return Json($"[{{\"id\":\"{WorkspaceId}\"}}]");
        if (path == "/rest/v1/workspace_snapshots")
        {
            var query = request.RequestUri.Query;
            if (query.Contains("select=revision,payload", StringComparison.Ordinal))
                return Json($"[{{\"revision\":1,\"payload\":{EmptySnapshot}}}]");
            if (query.Contains("select=revision", StringComparison.Ordinal))
                return Json($"[{{\"revision\":{Volatile.Read(ref _remoteRevision)}}}]");
            if (query.Contains("select=payload", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _fullSnapshotReads);
                return Json($"[{{\"payload\":{RemoteSnapshot}}}]");
            }
        }
        if (path == "/rest/v1/workspace_entities") return Json("[]");
        if (path == "/rest/v1/rpc/sync_vitan_entity_changes")
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Interlocked.Exchange(ref _publishedExpectedRevision, body.RootElement.GetProperty("p_expected_snapshot_revision").GetInt64());
            return Json("[{\"revision\":3}]");
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
    }

    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
    };
}

sealed class CountingHttpHandler : HttpMessageHandler
{
    private int _activeRequests;
    private int _maximumConcurrentRequests;

    public int MaximumConcurrentRequests => Volatile.Read(ref _maximumConcurrentRequests);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var active = Interlocked.Increment(ref _activeRequests);
        int observed;
        while (active > (observed = Volatile.Read(ref _maximumConcurrentRequests)) &&
               Interlocked.CompareExchange(ref _maximumConcurrentRequests, active, observed) != observed) { }
        await Task.Delay(40, cancellationToken);
        Interlocked.Decrement(ref _activeRequests);
        return new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error_code\":\"invalid_credentials\",\"msg\":\"Invalid login credentials\"}")
        };
    }
}

sealed class RefreshingHttpHandler(bool rejectRefresh = false) : HttpMessageHandler
{
    private static readonly string WorkspaceId = "831c0aec-b23a-4fa5-a005-64be61f0a0fb";
    private int _refreshRequests;
    private int _challengeIssued;

    public int RefreshRequests => Volatile.Read(ref _refreshRequests);
    public bool RetriedWithFreshToken { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/auth/v1/token")
        {
            if (request.RequestUri.Query.Contains("grant_type=password", StringComparison.Ordinal))
                return Task.FromResult(Json("{\"access_token\":\"expired-access\",\"refresh_token\":\"synthetic-refresh\",\"expires_in\":3600}"));
            Interlocked.Increment(ref _refreshRequests);
            if (rejectRefresh)
                return Task.FromResult(Json("{\"error_code\":\"refresh_token_not_found\",\"msg\":\"Invalid Refresh Token\"}", HttpStatusCode.BadRequest));
            return Task.FromResult(Json("{\"access_token\":\"fresh-access\",\"refresh_token\":\"fresh-refresh\",\"expires_in\":3600}"));
        }
        if (path == "/rest/v1/workspaces")
        {
            if (request.Headers.Authorization?.Parameter == "expired-access" && Interlocked.Exchange(ref _challengeIssued, 1) == 0)
                return Task.FromResult(Json("{\"message\":\"JWT expired\"}", HttpStatusCode.Unauthorized));
            RetriedWithFreshToken = request.Headers.Authorization?.Parameter == "fresh-access";
            return Task.FromResult(Json($"[{{\"id\":\"{WorkspaceId}\"}}]"));
        }
        if (path == "/rest/v1/workspace_snapshots")
            return Task.FromResult(Json("[{\"revision\":1,\"payload\":{\"projects\":[],\"catalog\":{},\"materials\":{},\"materialGroups\":[\"ЛДСП\",\"Фурнитура\",\"Кромка\",\"Прочее\"],\"preferences\":{}}}]"));
        return Task.FromResult(Json("[]"));
    }

    private static HttpResponseMessage Json(string payload, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
    };
}

sealed class RetryableReadHttpHandler(
    HttpStatusCode failureStatus,
    int failuresBeforeSuccess,
    TimeSpan? retryAfter = null,
    bool throwNetworkFailure = false,
    bool throwTimeout = false) : HttpMessageHandler
{
    private static readonly string WorkspaceId = "45ce26b5-4a10-4fb5-98ed-45a964951b7e";
    private int _snapshotReadAttempts;
    public int SnapshotReadAttempts => Volatile.Read(ref _snapshotReadAttempts);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/auth/v1/token")
            return Task.FromResult(Json("{\"access_token\":\"synthetic-access\",\"refresh_token\":\"synthetic-refresh\",\"expires_in\":3600}"));
        if (path == "/rest/v1/workspaces") return Task.FromResult(Json($"[{{\"id\":\"{WorkspaceId}\"}}]"));
        if (path == "/rest/v1/workspace_snapshots")
        {
            if (Interlocked.Increment(ref _snapshotReadAttempts) <= failuresBeforeSuccess)
            {
                if (throwNetworkFailure) throw new HttpRequestException("Synthetic network failure.");
                if (throwTimeout) throw new TaskCanceledException("Synthetic request timeout.");
                var failed = Json("{\"message\":\"temporary failure\"}", failureStatus);
                if (retryAfter is { } delay)
                    failed.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
                return Task.FromResult(failed);
            }
            return Task.FromResult(Json("[{\"revision\":1,\"payload\":{\"projects\":[],\"catalog\":{},\"materials\":{},\"materialGroups\":[\"ЛДСП\",\"Фурнитура\",\"Кромка\",\"Прочее\"],\"preferences\":{}}}]"));
        }
        return Task.FromResult(Json("[]"));
    }

    private static HttpResponseMessage Json(string payload, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
    };
}

sealed class AuthPostFailureHttpHandler : HttpMessageHandler
{
    private int _authRequests;
    public int AuthRequests => Volatile.Read(ref _authRequests);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _authRequests);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("{\"message\":\"unavailable\"}", System.Text.Encoding.UTF8, "application/json")
        });
    }
}

sealed class SlowPublishHttpHandler : HttpMessageHandler
{
    private static readonly string WorkspaceId = "cc001cbe-4198-4e88-9ae8-a8e177f08517";
    private readonly TaskCompletionSource _publishStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _releasePublish = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _revisionPagesRequested;

    public Task PublishStarted => _publishStarted.Task;
    public int RevisionPagesRequested => Volatile.Read(ref _revisionPagesRequested);
    public void ReleasePublish() => _releasePublish.TrySetResult();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/auth/v1/token", StringComparison.Ordinal))
            return Json("{\"access_token\":\"synthetic-access\",\"refresh_token\":\"synthetic-refresh\",\"expires_in\":3600}");
        if (path == "/rest/v1/workspaces") return Json($"[{{\"id\":\"{WorkspaceId}\"}}]");
        if (path == "/rest/v1/workspace_snapshots")
        {
            if (request.RequestUri.Query.Contains("select=revision,payload", StringComparison.Ordinal))
                return Json("[{\"revision\":1,\"payload\":{\"projects\":[],\"catalog\":{},\"materials\":{},\"materialGroups\":[\"ЛДСП\",\"Фурнитура\",\"Кромка\",\"Прочее\"],\"preferences\":{}}}]");
            if (request.RequestUri.Query.Contains("select=revision", StringComparison.Ordinal))
                return Json("[{\"revision\":1}]");
            return Json("[]");
        }
        if (path == "/rest/v1/workspace_entities")
        {
            Interlocked.Increment(ref _revisionPagesRequested);
            var start = request.Headers.TryGetValues("Range", out var rangeValues) && int.TryParse(rangeValues.Single().Split('-')[0], out var offset)
                ? offset : 0;
            var count = start == 0 ? 1000 : 1;
            var rows = Enumerable.Range(start, count)
                .Select(index => $"{{\"entity_type\":\"fixture\",\"entity_id\":\"{index:D4}\",\"revision\":1}}");
            return Json("[" + string.Join(',', rows) + "]");
        }
        if (path == "/rest/v1/rpc/sync_vitan_entity_changes")
        {
            _publishStarted.TrySetResult();
            await _releasePublish.Task.WaitAsync(cancellationToken);
            return Json("[{\"revision\":2}]");
        }
        return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
    }

    private static HttpResponseMessage Json(string payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
    };
}
