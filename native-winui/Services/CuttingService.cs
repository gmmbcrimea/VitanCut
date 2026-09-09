using System.Globalization;
using System.Text;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public enum CutReportScope
{
    All,
    MainDsp,
    Additional
}

public static class CuttingService
{
    private const double Gap = 6;
    private const double Trim = 10;
    private static readonly CultureInfo CsvCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly char[] Cp1251Chars =
    [
        '\u0402', '\u0403', '\u201A', '\u0453', '\u201E', '\u2026', '\u2020', '\u2021',
        '\u20AC', '\u2030', '\u0409', '\u2039', '\u040A', '\u040C', '\u040B', '\u040F',
        '\u0452', '\u2018', '\u2019', '\u201C', '\u201D', '\u2022', '\u2013', '\u2014',
        '\0', '\u2122', '\u0459', '\u203A', '\u045A', '\u045C', '\u045B', '\u045F',
        '\u00A0', '\u040E', '\u045E', '\u0408', '\u00A4', '\u0490', '\u00A6', '\u00A7',
        '\u0401', '\u00A9', '\u0404', '\u00AB', '\u00AC', '\u00AD', '\u00AE', '\u0407',
        '\u00B0', '\u00B1', '\u0406', '\u0456', '\u0491', '\u00B5', '\u00B6', '\u00B7',
        '\u0451', '\u2116', '\u0454', '\u00BB', '\u0458', '\u0405', '\u0455', '\u0457',
        '\u0410', '\u0411', '\u0412', '\u0413', '\u0414', '\u0415', '\u0416', '\u0417',
        '\u0418', '\u0419', '\u041A', '\u041B', '\u041C', '\u041D', '\u041E', '\u041F',
        '\u0420', '\u0421', '\u0422', '\u0423', '\u0424', '\u0425', '\u0426', '\u0427',
        '\u0428', '\u0429', '\u042A', '\u042B', '\u042C', '\u042D', '\u042E', '\u042F',
        '\u0430', '\u0431', '\u0432', '\u0433', '\u0434', '\u0435', '\u0436', '\u0437',
        '\u0438', '\u0439', '\u043A', '\u043B', '\u043C', '\u043D', '\u043E', '\u043F',
        '\u0440', '\u0441', '\u0442', '\u0443', '\u0444', '\u0445', '\u0446', '\u0447',
        '\u0448', '\u0449', '\u044A', '\u044B', '\u044C', '\u044D', '\u044E', '\u044F'
    ];

    public static string CreateDetailingCsv(AppState state, Project project)
    {
        var rows = new List<string[]>
        {
            new[] { "Проект", "Изделие", "Кол-во изделий", "Деталь", "Тип материала", "Материал", "Кол-во деталей", "Длина", "Ширина", "Расход", "Стоимость" }
        };

        foreach (var item in DetailItems(state, project))
        {
            rows.Add(new[]
            {
                Text(project.Name),
                Text(item.Product.Name),
                N(item.ProductQty),
                Text(item.Detail.Name),
                Text(item.Detail.Type),
                Text(item.Calc.Material?.Name ?? ""),
                N(item.TotalQty),
                N(item.Calc.Length),
                N(item.Calc.Width),
                N(item.Calc.Usage * item.ProductQty),
                N(item.Calc.Cost * item.ProductQty)
            });
        }

        return ToCsv(rows);
    }

    public static string CreateCutMapCsv(AppState state, Project project)
    {
        var rows = new List<string[]>
        {
            new[] { "Проект", "Материал", "Лист", "Деталь", "Изделие", "Кол-во", "X", "Y", "Длина", "Ширина", "Повернуто", "Длина листа", "Ширина листа", "Заполнение" }
        };

        var groups = DetailItems(state, project)
            .Where(item => IsSheetMaterial(item.Calc.Material) && item.Calc.Length > 0 && item.Calc.Width > 0)
            .GroupBy(item => item.Calc.Material!.Id);

        foreach (var group in groups)
        {
            var material = group.First().Calc.Material!;
            var parts = group.SelectMany(item => ExpandParts(item)).OrderByDescending(part => part.Area).ToList();
            var sheets = Pack(parts, material, CutOptions.From(state.Database.Preferences)).Sheets;

            for (var sheetIndex = 0; sheetIndex < sheets.Count; sheetIndex++)
            {
                var sheet = sheets[sheetIndex];
                foreach (var placed in sheet.Parts)
                {
                    rows.Add(new[]
                    {
                        Text(project.Name),
                        Text(material.Name),
                        (sheetIndex + 1).ToString(CultureInfo.InvariantCulture),
                        Text(placed.Source.Detail.Name),
                        Text(placed.Source.Product.Name),
                        "1",
                        N(placed.X),
                        N(placed.Y),
                        N(placed.Length),
                        N(placed.Width),
                        placed.Rotated ? "Да" : "Нет",
                        N(material.SheetLength),
                        N(material.SheetWidth),
                        N(sheet.Efficiency)
                    });
                }
            }
        }

        return ToCsv(rows);
    }

    public static CutReport BuildCutReport(AppState state, Project project, bool applySavedLayout = true, CutReportScope scope = CutReportScope.All)
    {
        var options = CutOptions.From(state.Database.Preferences);
        var groups = new List<CutGroup>();
        var unplaced = new List<CutUnplacedPart>();
        var items = DetailItems(state, project).Where(item => MatchesScope(item.Calc.Material, scope)).ToList();
        foreach (var item in items)
        {
            var reason = item.Calc.Material is null ? "Не выбран материал" :
                item.Calc.Material.Unit != "m2" ? "" :
                !string.IsNullOrEmpty(item.Calc.Error) ? item.Calc.Error :
                !IsSheetMaterial(item.Calc.Material) ? "Не заданы размеры листа материала" :
                item.Calc.Length <= 0 || item.Calc.Width <= 0 ? "Нулевой размер детали" : "";
            if (reason.Length > 0)
                unplaced.Add(new CutUnplacedPart(Text(item.Calc.Material?.Name ?? ""), Text(item.Product.Name), Text(item.Detail.Name), item.Calc.Length, item.Calc.Width, reason));
        }

        foreach (var group in items
            .Where(item => IsSheetMaterial(item.Calc.Material) && item.Calc.Length > 0 && item.Calc.Width > 0)
            .GroupBy(item => item.Calc.Material!.Id))
        {
            var material = group.First().Calc.Material!;
            var parts = group.SelectMany(ExpandParts).OrderByDescending(part => part.Area).ToList();
            var packed = Pack(parts, material, options);
            unplaced.AddRange(packed.Unplaced.Select(part => new CutUnplacedPart(
                Text(material.Name), Text(part.Source.Product.Name), Text(part.Source.Detail.Name), part.Length, part.Width,
                "Деталь не помещается в доступную область листа с текущими подрезкой и зазором.")));

            // One numbering sequence per material keeps a detail's number stable across sheets.
            var displayNumbers = new Dictionary<string, int>();
            var sheets = packed.Sheets
                .Select((sheet, index) => ToCutSheet(sheet, material, index + 1, applySavedLayout ? project.CutLayout : null, options, displayNumbers))
                .ToList();
            groups.Add(new CutGroup(Text(material.Name), material.TextureDirection, material.SheetLength, material.SheetWidth, sheets, material.Id));
        }

        var report = new CutReport(Text(project.Name), Text(project.Counterparty), Text(project.Address), groups, unplaced, options.Trim, options.Gap, state.Database.Preferences.CutPalette);
        var plans = scope == CutReportScope.Additional ? project.AdditionalCutPlans : project.CutPlans;
        var activeId = scope == CutReportScope.Additional ? project.ActiveAdditionalCutPlanId : project.ActiveCutPlanId;
        if (applySavedLayout && plans.FirstOrDefault(p => p.Id == activeId) is { } active)
            return CutPlanService.Restore(report, active) ?? report;
        return report;
    }

    private static CutSheet ToCutSheet(SheetPlan sheet, Material material, int number, IReadOnlyList<CutLayoutOverride>? layout,
        CutOptions options, Dictionary<string, int> displayNumbers)
    {
        var placements = sheet.Parts.Select(placed =>
        {
            var groupKey = $"{placed.Source.Product.Id}|{placed.Source.Detail.Id}";
            if (!displayNumbers.TryGetValue(groupKey, out var displayNumber))
            {
                displayNumber = displayNumbers.Count + 1;
                displayNumbers[groupKey] = displayNumber;
            }

            return new CutPlacement(
                placed.Source.Product.Id,
                placed.Source.Detail.Id,
                Text(placed.Source.Product.Name),
                Text(placed.Source.Detail.Name),
                displayNumber,
                placed.Part.InstanceId,
                placed.Part.Length,
                placed.Part.Width,
                placed.X,
                placed.Y,
                placed.Length,
                placed.Width,
                placed.Rotated,
                DetailRotation.CanRotate(placed.Source.Detail, material));
        }).ToList();

        return new CutSheet(number, material.SheetLength, material.SheetWidth, sheet.Efficiency, ApplyLayoutOverrides(placements, layout, sheet.Length, sheet.Width, options));
    }

    private static List<CutPlacement> ApplyLayoutOverrides(
        List<CutPlacement> placements,
        IReadOnlyList<CutLayoutOverride>? layout,
        double sheetLength,
        double sheetWidth, CutOptions options)
    {
        if (layout is null || layout.Count == 0) return placements;
        var saved = layout
            .Where(item => !string.IsNullOrWhiteSpace(item.InstanceId))
            .GroupBy(item => item.InstanceId)
            .ToDictionary(group => group.Key, group => group.Last());

        var candidate = placements.Select(placement =>
        {
            if (!saved.TryGetValue(placement.InstanceId, out var item) ||
                !Same(item.BaseLength, placement.BaseLength) || !Same(item.BaseWidth, placement.BaseWidth) ||
                (item.Rotated && !placement.AllowRotation) ||
                !Same(item.Length, item.Rotated ? placement.BaseWidth : placement.BaseLength) ||
                !Same(item.Width, item.Rotated ? placement.BaseLength : placement.BaseWidth) ||
                !double.IsFinite(item.X) || !double.IsFinite(item.Y))
                return placement;
            return placement with { X = item.X, Y = item.Y, Length = item.Length, Width = item.Width, Rotated = item.Rotated };
        }).ToList();

        return IsValidLayout(candidate, sheetLength, sheetWidth, options) ? candidate : placements;
    }

    private static bool IsValidLayout(IReadOnlyList<CutPlacement> placements, double sheetLength, double sheetWidth, CutOptions options)
    {
        foreach (var placement in placements)
        {
            if (placement.X < options.Trim || placement.Y < options.Trim || placement.Length <= 0 || placement.Width <= 0 ||
                placement.X + placement.Length > sheetLength - options.Trim || placement.Y + placement.Width > sheetWidth - options.Trim)
                return false;
        }

        for (var left = 0; left < placements.Count; left++)
        for (var right = left + 1; right < placements.Count; right++)
        {
            var a = placements[left];
            var b = placements[right];
            if (a.X < b.X + b.Length + options.Gap && a.X + a.Length + options.Gap > b.X && a.Y < b.Y + b.Width + options.Gap && a.Y + a.Width + options.Gap > b.Y)
                return false;
        }

        return true;
    }

    private static bool Same(double first, double second) => Math.Abs(first - second) < 0.01;
    public static DetailingReport BuildDetailingReport(AppState state, Project project)
    {
        var products = project.Products.Select(product =>
        {
            var productQty = product.Qty <= 0 ? 1 : product.Qty;
            var rows = product.Details.Select(detail =>
            {
                var calc = Calculator.Detail(state, product, detail);
                var totalQty = (detail.Qty <= 0 ? 1 : detail.Qty) * productQty;
                return new DetailingRow(
                    Text(detail.Name), Text(detail.Type), Text(calc.Material?.Name ?? ""), totalQty,
                    calc.Length, calc.Width, calc.Usage * productQty, calc.Cost * productQty,
                    calc.RawArea * productQty, DetailIssue(calc), DetailRotation.IsLocked(detail, calc.Material));
            }).ToList();

            return new DetailingProduct(
                Text(product.Name), product.Length, product.Depth, product.Height, productQty,
                Text(product.Image), rows, product.FixedSizes.Select(size => new DetailingFixedSize(Text(size.Name), size.Value)).ToList(),
                product.HasLegs, product.LegHeight);
        }).ToList();

        var issues = products.SelectMany(product => product.Rows.Where(row => row.HasIssue)
            .Select(row => new DetailingIssue(product.Name, row.DetailName, row.Issue))).ToList();
        var summary = new DetailingSummary(
            products.Sum(product => product.Rows.Sum(row => row.Area)),
            products.Sum(product => product.Rows.Sum(row => row.Cost)),
            products.Sum(product => product.Rows.Sum(row => row.Qty)));
        return new DetailingReport(Text(project.Name), Text(project.Counterparty), Text(project.Address), products, issues, summary, state.Database.Preferences.DetailingIncludeImages);
    }

    private static string DetailIssue(DetailCalc calc)
    {
        if (calc.Material is null) return "Не выбран материал";
        if (calc.Error.Length > 0) return calc.Error;
        if (calc.Material.Unit is "m2" or "lm" && calc.Length <= 0) return "Некорректная длина или формула";
        if (calc.Material.Unit == "m2" && calc.Width <= 0) return "Некорректная ширина или формула";
        return "";
    }
    private static IEnumerable<DetailItem> DetailItems(AppState state, Project project)
    {
        foreach (var product in project.Products)
        {
            var productQty = product.Qty <= 0 ? 1 : product.Qty;
            foreach (var detail in product.Details)
            {
                var calc = Calculator.Detail(state, product, detail);
                yield return new DetailItem(product, detail, calc, productQty, (detail.Qty <= 0 ? 1 : detail.Qty) * productQty);
            }
        }
    }

    private static bool IsSheetMaterial(Material? material) =>
        material is not null && material.Unit == "m2" && material.SheetLength > 0 && material.SheetWidth > 0;

    private static bool MatchesScope(Material? material, CutReportScope scope)
    {
        if (scope == CutReportScope.All) return true;
        if (material is null) return false;
        var isDsp = material.Name.Contains("ДСП", StringComparison.OrdinalIgnoreCase);
        return scope == CutReportScope.MainDsp ? isDsp : !isDsp;
    }

    private static IEnumerable<Part> ExpandParts(DetailItem item)
    {
        var count = Math.Max(1, (int)Math.Ceiling(item.TotalQty));
        for (var index = 0; index < count; index++)
        {
            yield return new Part(item, item.Calc.Length, item.Calc.Width, $"{item.Product.Id}:{item.Detail.Id}:{index + 1}");
        }
    }

    private static PackingResult Pack(List<Part> parts, Material material, CutOptions options)
    {
        var sheets = new List<SheetPlan>();
        var unplaced = new List<Part>();
        // Process a family together, then prefer sheets that already contain it. Other
        // details may still use the remaining space, so sheet count stays minimal.
        var ordered = parts
            .GroupBy(part => (ProductId: part.Source.Product.Id, DetailId: part.Source.Detail.Id))
            .OrderByDescending(group => group.Sum(part => part.Area))
            .ThenBy(group => group.Key.ProductId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.DetailId, StringComparer.Ordinal)
            .SelectMany(group => group.OrderByDescending(part => part.Area).ThenBy(part => part.InstanceId, StringComparer.Ordinal));
        foreach (var part in ordered)
        {
            var candidate = sheets
                .SelectMany((sheet, index) => FindCandidates(sheet, part, material, options)
                    .Select(item => new
                    {
                        Candidate = item with { SheetIndex = index },
                        AddedCuts = AddedCuts(sheet, item, options),
                        SameFamily = sheet.Parts.Any(existing => existing.Part.Source.Product.Id == part.Source.Product.Id &&
                            existing.Part.Source.Detail.Id == part.Source.Detail.Id)
                    }))
                .OrderBy(item => item.AddedCuts)
                .ThenBy(item => item.Candidate.ShortSide)
                .ThenBy(item => item.Candidate.AreaWaste)
                .ThenByDescending(item => item.SameFamily)
                .ThenBy(item => item.Candidate.SheetIndex)
                .FirstOrDefault();
            if (candidate is not null)
            {
                Place(sheets[candidate.Candidate.SheetIndex], part, candidate.Candidate, options);
                continue;
            }

            var sheet = new SheetPlan(material.SheetLength, material.SheetWidth, options.Trim);
            var first = FindCandidates(sheet, part, material, options)
                .OrderBy(item => item.ShortSide)
                .ThenBy(item => item.AreaWaste)
                .FirstOrDefault();
            if (first is null)
            {
                unplaced.Add(part);
                continue;
            }

            Place(sheet, part, first, options);
            sheets.Add(sheet);
        }

        return new PackingResult(sheets, unplaced);
    }

    private static IEnumerable<PlacementCandidate> FindCandidates(SheetPlan sheet, Part part, Material material, CutOptions options)
    {
        foreach (var free in sheet.Free)
        {
            if (part.Length <= free.Width && part.Width <= free.Height)
                yield return Candidate(free, part.Length, part.Width, false);
            if (DetailRotation.CanRotate(part.Source.Detail, material) &&
                part.Width <= free.Width && part.Length <= free.Height)
                yield return Candidate(free, part.Width, part.Length, true);
        }
    }

    private static PlacementCandidate Candidate(FreeRect free, double length, double width, bool rotated) =>
        new(free, length, width, rotated, 0,
            Math.Min(free.Width - length, free.Height - width),
            free.Width * free.Height - length * width);

    private static int AddedCuts(SheetPlan sheet, PlacementCandidate candidate, CutOptions options)
    {
        var left = candidate.Free.X;
        var top = candidate.Free.Y;
        var xs = sheet.Parts.SelectMany(part => new[] { part.X, part.X + part.Length }).Append(options.Trim).Append(sheet.Length - options.Trim).ToList();
        var ys = sheet.Parts.SelectMany(part => new[] { part.Y, part.Y + part.Width }).Append(options.Trim).Append(sheet.Width - options.Trim).ToList();
        return NewCoordinateCount(left, left + candidate.Length, xs) + NewCoordinateCount(top, top + candidate.Width, ys);
    }

    private static int NewCoordinateCount(double first, double second, IReadOnlyCollection<double> existing) =>
        new[] { first, second }.Count(value => existing.All(other => Math.Abs(value - other) > 1e-6));

    private static void Place(SheetPlan sheet, Part part, PlacementCandidate candidate, CutOptions options)
    {
        var free = candidate.Free;
        sheet.Free.Remove(free);
        sheet.Parts.Add(new PlacedPart(part, free.X, free.Y, candidate.Length, candidate.Width, candidate.Rotated));

        // The gap belongs to the placed part, so adjacent free regions never produce overlapping parts.
        var rightWidth = free.Width - candidate.Length - options.Gap;
        if (rightWidth > 0)
            sheet.Free.Add(new FreeRect(free.X + candidate.Length + options.Gap, free.Y, rightWidth, free.Height));

        var bottomHeight = free.Height - candidate.Width - options.Gap;
        if (bottomHeight > 0)
            sheet.Free.Add(new FreeRect(free.X, free.Y + candidate.Width + options.Gap, candidate.Length, bottomHeight));

        PruneFreeRectangles(sheet.Free);
    }

    private static void PruneFreeRectangles(List<FreeRect> free)
    {
        for (var index = free.Count - 1; index >= 0; index--)
        {
            var item = free[index];
            if (item.Width <= 0 || item.Height <= 0 ||
                free.Where((_, otherIndex) => otherIndex != index).Any(other => Contains(other, item)))
                free.RemoveAt(index);
        }
    }

    private static bool Contains(FreeRect outer, FreeRect inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y &&
        inner.X + inner.Width <= outer.X + outer.Width &&
        inner.Y + inner.Height <= outer.Y + outer.Height;
    private static string ToCsv(IEnumerable<string[]> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine("sep=;");
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(";", row.Select(Escape)));
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        if (!value.Contains(';') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r')) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string N(double value) => value.ToString("0.##", CsvCulture);
    public static string Text(string value) => RepairMojibake(value);

    private static string RepairMojibake(string value)
    {
        if (string.IsNullOrEmpty(value) || (!value.Contains('Р') && !value.Contains('С'))) return value;

        var bytes = new List<byte>(value.Length);
        foreach (var ch in value)
        {
            if (ch <= 0x7f)
            {
                bytes.Add((byte)ch);
                continue;
            }

            var index = Array.IndexOf(Cp1251Chars, ch);
            if (index < 0) return value;
            bytes.Add((byte)(index + 0x80));
        }

        try
        {
            var repaired = Encoding.UTF8.GetString(bytes.ToArray());
            return repaired.Contains('\uFFFD') ? value : repaired;
        }
        catch
        {
            return value;
        }
    }

    private sealed record DetailItem(Product Product, Detail Detail, DetailCalc Calc, double ProductQty, double TotalQty);
    private sealed record Part(DetailItem Source, double Length, double Width, string InstanceId)
    {
        public double Area => Length * Width;
    }

    private sealed record PackingResult(List<SheetPlan> Sheets, List<Part> Unplaced);
    private sealed record FreeRect(double X, double Y, double Width, double Height);
    private sealed record PlacementCandidate(FreeRect Free, double Length, double Width, bool Rotated, int SheetIndex, double ShortSide, double AreaWaste);

    private sealed record CutOptions(double Trim, double Gap)
    {
        public static CutOptions From(AppPreferences preferences) =>
            new(Math.Max(0, preferences.CutTrim), Math.Max(0, preferences.CutGap));
    }

    private sealed record PlacedPart(Part Part, double X, double Y, double Length, double Width, bool Rotated)
    {
        public DetailItem Source => Part.Source;
        public double Area => Length * Width;
    }

    private sealed class SheetPlan
    {
        public SheetPlan(double length, double width, double trim)
        {
            Length = length;
            Width = width;
            var usableLength = Math.Max(0, length - trim * 2);
            var usableWidth = Math.Max(0, width - trim * 2);
            if (usableLength > 0 && usableWidth > 0)
                Free.Add(new FreeRect(trim, trim, usableLength, usableWidth));
        }

        public double Length { get; }
        public double Width { get; }
        public List<FreeRect> Free { get; } = [];
        public List<PlacedPart> Parts { get; } = [];
        public double Efficiency => Length <= 0 || Width <= 0 ? 0 : Parts.Sum(part => part.Area) / (Length * Width);
    }
}

public sealed record CutReport(string ProjectName, string Counterparty, string Address, List<CutGroup> Groups, List<CutUnplacedPart> Unplaced, double Trim, double Gap, string Palette = "blue");
public sealed record CutUnplacedPart(string MaterialName, string ProductName, string DetailName, double Length, double Width, string Reason);
public sealed record CutGroup(string MaterialName, bool TextureDirection, double SheetLength, double SheetWidth, List<CutSheet> Sheets, string MaterialId = "");
public sealed record CutSheet(int Number, double SheetLength, double SheetWidth, double Efficiency, List<CutPlacement> Placements);
public sealed record CutPlacement(string ProductId, string DetailId, string ProductName, string DetailName, int DisplayNumber, string InstanceId, double BaseLength, double BaseWidth, double X, double Y, double Length, double Width, bool Rotated, bool AllowRotation);

public sealed record DetailingReport(string ProjectName, string Counterparty, string Address, List<DetailingProduct> Products, List<DetailingIssue> Issues, DetailingSummary Summary, bool IncludeImages);
public sealed record DetailingProduct(string Name, double Length, double Depth, double Height, double Qty, string Image, List<DetailingRow> Rows, List<DetailingFixedSize> FixedSizes, bool HasLegs, double LegHeight);
public sealed record DetailingRow(string DetailName, string Type, string MaterialName, double Qty, double Length, double Width, double Usage, double Cost, double Area, string Issue, bool RotationLocked = false) { public bool HasIssue => !string.IsNullOrWhiteSpace(Issue); }
public sealed record DetailingFixedSize(string Name, double Value);
public sealed record DetailingIssue(string ProductName, string DetailName, string Message);
public sealed record DetailingSummary(double Area, double Cost, double DetailQuantity);
