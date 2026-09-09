using System.Diagnostics;
using RectangleBinPacking;

namespace VitanCut.WinUI.Services;

public sealed record CutAlternative(string Name, string Algorithm, CutReport Report);

public static class CutOptimization
{
    // Bounded recursive order search around the established MaxRects/Guillotine engines.
    public static List<CutAlternative> Calculate(CutReport source, CancellationToken cancellation = default)
    {
        var watch = Stopwatch.StartNew();
        var results = new List<CutAlternative>();
        for (var mode = 0; mode < 4; mode++)
        {
            var groups = new List<CutGroup>();
            var usedBaseline = false;
            foreach (var group in source.Groups)
            {
                cancellation.ThrowIfCancellationRequested();
                var parts = group.Sheets.SelectMany(s => s.Placements).ToList();
                var order = FamilyOrder(parts, mode).ToList();
                var best = Pack(group, order, source.Trim, source.Gap, mode, false, cancellation) ?? group;
                if (mode < 2 && Better(group.Sheets, best.Sheets, source.Trim)) best = group;
                foreach (var columns in new[] { 1, 2, 3, 4, 8 })
                foreach (var rotate in new[] { false, true })
                {
                    cancellation.ThrowIfCancellationRequested();
                    var grouped = PackBlocks(group, order, source.Trim, source.Gap, mode, columns, rotate, cancellation);
                    if (grouped is not null && Better(grouped.Sheets, best.Sheets, source.Trim)) best = grouped;
                }
                var nodes = 0;
                void Search(List<CutPlacement> current, int depth)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (++nodes > 28 || watch.Elapsed.TotalSeconds > 8) return;
                    var candidate = Pack(group, current, source.Trim, source.Gap, mode, depth % 2 == 1, cancellation);
                    if (candidate is not null && Better(candidate.Sheets, best.Sheets, source.Trim)) best = candidate;
                    if (depth >= 3 || depth >= current.Count - 1) return;
                    // Branch on different leading parts, including parts from the tail of the order.
                    foreach (var index in new[] { depth + 1, current.Count / 2, current.Count - 1 }.Distinct())
                    {
                        if (index <= depth || index >= current.Count) continue;
                        var next = current.ToList();
                        (next[depth], next[index]) = (next[index], next[depth]);
                        Search(next, depth + 1);
                    }
                }
                Search(order, 0);
                usedBaseline |= ReferenceEquals(best, group);
                groups.Add(best);
            }
            var algorithm = mode < 2 ? "MaxRects" : "Гильотинный";
            var fallback = usedBaseline && mode < 2 ? " + базовый" : "";
            results.Add(new CutAlternative($"{algorithm}{fallback} · {(mode % 2 == 0 ? "по площади" : "по стороне")}", algorithm + fallback,
                CutEditing.Renumber(source with { Groups = groups })));
        }
        // The cutting plan is selected primarily by the number of saw passes.  Sheet
        // count remains the next tie-breaker, so a lower-cut layout does not get
        // discarded merely because it uses a different grouping of the same parts.
        return results.OrderBy(result => CuttingMetrics.ForSheets(result.Report.Groups.SelectMany(group => group.Sheets), result.Report.Trim).Count)
            .ThenBy(result => SheetCount(result.Report))
            .ThenBy(result => FamilySpread(result.Report.Groups.SelectMany(group => group.Sheets)))
            .ThenBy(result => FamilyDistance(result.Report.Groups.SelectMany(group => group.Sheets)))
            .ThenBy(result => CuttingMetrics.ForSheets(result.Report.Groups.SelectMany(group => group.Sheets), result.Report.Trim).Length)
            .ThenByDescending(result => Compactness(result.Report.Groups.SelectMany(group => group.Sheets)))
            .ToList();
    }

    public static int SheetCount(CutReport report) => report.Groups.Sum(g => g.Sheets.Count);
    private static double Compactness(IEnumerable<CutSheet> sheets) => sheets.Sum(s => s.Efficiency * s.Efficiency);
    private static bool Better(List<CutSheet> a, List<CutSheet> b, double trim)
    {
        var aCuts = CuttingMetrics.ForSheets(a, trim).Count;
        var bCuts = CuttingMetrics.ForSheets(b, trim).Count;
        if (aCuts != bCuts) return aCuts < bCuts;
        if (a.Count != b.Count) return a.Count < b.Count;
        var aSpread = FamilySpread(a); var bSpread = FamilySpread(b);
        if (aSpread != bSpread) return aSpread < bSpread;
        var distance = FamilyDistance(a).CompareTo(FamilyDistance(b));
        if (distance != 0) return distance < 0;
        var length = CuttingMetrics.ForSheets(a, trim).Length.CompareTo(CuttingMetrics.ForSheets(b, trim).Length);
        return length != 0 ? length < 0 : Compactness(a) > Compactness(b) + 1e-9;
    }

    private static double FamilyDistance(IEnumerable<CutSheet> sheets) => sheets.Sum(sheet =>
        sheet.Placements.GroupBy(part => (part.ProductId, part.DetailId)).Sum(group =>
            (group.Max(p => p.X + p.Length) - group.Min(p => p.X)) *
            (group.Max(p => p.Y + p.Width) - group.Min(p => p.Y)) - group.Sum(p => p.Length * p.Width)));

    private static int FamilySpread(IEnumerable<CutSheet> sheets) => sheets.SelectMany(sheet => sheet.Placements)
        .GroupBy(part => (part.ProductId, part.DetailId)).Sum(group => Math.Max(0, group.Select(part => sheets.First(sheet => sheet.Placements.Contains(part))).Distinct().Count() - 1));

    private static CutGroup? Pack(CutGroup group, List<CutPlacement> parts, double trim, double gap, int mode, bool preferRotation, CancellationToken cancellation)
    {
        // Inflate by kerf; the extra kerf on the bin's far edge permits edge-flush parts.
        // Conservative quantization never enlarges the usable sheet or shrinks a part.
        var scale = Math.Min(10, 40000 / Math.Max(group.SheetLength + gap, group.SheetWidth + gap));
        var width = (int)Math.Floor((group.SheetLength - 2 * trim + gap) * scale + 1e-7);
        var height = (int)Math.Floor((group.SheetWidth - 2 * trim + gap) * scale + 1e-7);
        if (width <= 0 || height <= 0) return null;
        var bins = new List<Bin>();
        foreach (var part in parts)
        {
            cancellation.ThrowIfCancellationRequested();
            var found = false;
            // Existing sheets are considered as one pool.  Family grouping is a
            // later tie-breaker; a partially used sheet gets first opportunity.
            foreach (var index in Enumerable.Range(0, bins.Count).Append(bins.Count))
            {
                var fresh = index == bins.Count;
                var bin = fresh ? new Bin(width, height, mode) : bins[index];
                foreach (var rotated in part.AllowRotation && !group.TextureDirection
                    ? new[] { preferRotation, !preferRotation } : new[] { false })
                {
                    var length = rotated ? part.BaseWidth : part.BaseLength;
                    var depth = rotated ? part.BaseLength : part.BaseWidth;
                    var rect = bin.Insert((int)Math.Ceiling((length + gap) * scale - 1e-7), (int)Math.Ceiling((depth + gap) * scale - 1e-7));
                    if (rect.Height == 0) continue;
                    bin.Parts.Add(part with { X = trim + rect.X / scale, Y = trim + rect.Y / scale, Length = length, Width = depth, Rotated = rotated });
                    if (fresh) bins.Add(bin);
                    found = true;
                    break;
                }
                if (found) break;
            }
            if (!found) return null; // Preserve the exact double-precision baseline if quantization prevents a fit.
        }
        var sheets = bins.Select((b, i) => new CutSheet(i + 1, group.SheetLength, group.SheetWidth,
            b.Parts.Sum(p => p.Length * p.Width) / (group.SheetLength * group.SheetWidth), b.Parts)).ToList();
        if (sheets.Any(s => !CutEditing.Valid(s.Placements, s.SheetLength, s.SheetWidth, trim, gap))) return null;
        return group with { Sheets = sheets };
    }

    private static IEnumerable<CutPlacement> FamilyOrder(IEnumerable<CutPlacement> parts, int mode) =>
        parts.GroupBy(part => (part.ProductId, part.DetailId))
            .OrderByDescending(group => mode % 2 == 0 ? group.Sum(part => part.BaseLength * part.BaseWidth)
                : group.Max(part => Math.Max(part.BaseLength, part.BaseWidth)))
            .ThenBy(group => group.Key.ProductId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.DetailId, StringComparer.Ordinal)
            .SelectMany(group => mode % 2 == 0
                ? group.OrderByDescending(part => part.BaseLength * part.BaseWidth).ThenBy(part => part.InstanceId, StringComparer.Ordinal)
                : group.OrderByDescending(part => Math.Max(part.BaseLength, part.BaseWidth)).ThenBy(part => part.InstanceId, StringComparer.Ordinal));

    private static CutGroup? PackBlocks(CutGroup group, List<CutPlacement> parts, double trim, double gap,
        int mode, int columns, bool rotate, CancellationToken cancellation)
    {
        var blocks = new List<CutPlacement>();
        var contents = new Dictionary<string, List<CutPlacement>>();
        foreach (var family in parts.GroupBy(p => (p.ProductId, p.DetailId, p.BaseLength, p.BaseWidth, p.AllowRotation)))
        {
            var first = family.First();
            var rotated = rotate && first.AllowRotation && !group.TextureDirection;
            var length = rotated ? first.BaseWidth : first.BaseLength;
            var width = rotated ? first.BaseLength : first.BaseWidth;
            var maxColumns = (int)Math.Floor((group.SheetLength - 2 * trim + gap) / (length + gap));
            var maxRows = (int)Math.Floor((group.SheetWidth - 2 * trim + gap) / (width + gap));
            if (maxColumns < 1 || maxRows < 1) return null;
            var remaining = new Queue<CutPlacement>(family);
            while (remaining.Count > 0)
            {
                var cols = Math.Min(Math.Min(columns, maxColumns), remaining.Count);
                var rows = Math.Min(maxRows, Math.Max(1, remaining.Count / cols));
                var local = new List<CutPlacement>();
                for (var row = 0; row < rows; row++)
                for (var col = 0; col < cols; col++)
                    local.Add(remaining.Dequeue() with { X = col * (length + gap), Y = row * (width + gap),
                        Length = length, Width = width, Rotated = rotated });
                var id = "block-" + blocks.Count;
                var blockLength = cols * (length + gap) - gap;
                var blockWidth = rows * (width + gap) - gap;
                blocks.Add(first with { InstanceId = id, BaseLength = blockLength, BaseWidth = blockWidth,
                    Length = blockLength, Width = blockWidth, AllowRotation = false, Rotated = false });
                contents[id] = local;
            }
        }
        var packed = Pack(group, FamilyOrder(blocks, mode).ToList(), trim, gap, mode, false, cancellation);
        if (packed is null) return null;
        var sheets = packed.Sheets.Select(sheet => sheet with { Placements = sheet.Placements.SelectMany(block =>
            contents[block.InstanceId].Select(part => part with { X = part.X + block.X, Y = part.Y + block.Y })).ToList() }).ToList();
        if (sheets.Any(sheet => !CutEditing.Valid(sheet.Placements, sheet.SheetLength, sheet.SheetWidth, trim, gap))) return null;
        return group with { Sheets = sheets };
    }

    private sealed class Bin
    {
        private readonly MaxRectsBinPack? _max;
        private readonly GuillotineBinPack? _guillotine;
        private readonly int _mode;
        public List<CutPlacement> Parts { get; } = [];
        public Bin(int width, int height, int mode)
        {
            _mode = mode;
            if (mode < 2) _max = new MaxRectsBinPack(width, height, false);
            else _guillotine = new GuillotineBinPack(width, height);
        }
        public Rect Insert(int width, int height) => _max is not null
            ? _max.Insert(width, height, _mode == 0 ? FreeRectChoiceHeuristic.RectBestShortSideFit : FreeRectChoiceHeuristic.RectBestAreaFit)
            : _guillotine!.Insert(width, height, false, GuillotineBinPack.FreeRectChoiceHeuristic.RectBestAreaFit,
                _mode == 2 ? GuillotineBinPack.GuillotineSplitHeuristic.SplitMinimizeArea : GuillotineBinPack.GuillotineSplitHeuristic.SplitMaximizeArea);
    }
}
