namespace VitanCut.WinUI.Services;

public sealed record CutMeasure(int Count, double Length);
public sealed record CutRemainder(double X, double Y, double Length, double Width);
public sealed record CutLine(bool IsVertical, double Position, double Start, double End, bool IsTrim = false)
{
    public double Length => Math.Max(0, End - Start);
}

public sealed record UsefulRemainder(string MaterialName, int SheetNumber, double Length, double Width)
{
    public double Area => Length * Width / 1_000_000d;
}

public sealed record CutMetrics(CutMeasure Cuts, List<UsefulRemainder> Remainders);

public static class CuttingMetrics
{
    private const double Epsilon = 1e-6;

    public static CutMetrics Analyze(CutReport report, double minimumRemainder)
    {
        var cuts = new CutMeasure(0, 0);
        var remainders = new List<UsefulRemainder>();
        var minimum = Math.Max(0, minimumRemainder);
        foreach (var group in report.Groups)
        foreach (var sheet in group.Sheets)
        {
            cuts = Add(cuts, ForSheet(sheet, report.Trim));
            remainders.AddRange(RemaindersForSheet(sheet, report.Trim, report.Gap, minimum)
                .Select(item => new UsefulRemainder(group.MaterialName, sheet.Number, item.Length, item.Width)));
        }
        return new CutMetrics(cuts, remainders);
    }

    public static CutMeasure ForSheets(IEnumerable<CutSheet> sheets, double trim) =>
        sheets.Aggregate(new CutMeasure(0, 0), (total, sheet) => Add(total, ForSheet(sheet, trim)));

    public static CutMeasure ForSheet(CutSheet sheet, double trim)
    {
        var lines = LinesForSheet(sheet, trim);
        return new CutMeasure(lines.Count, lines.Sum(line => line.Length));
    }

    // Builds a cutting tree, rather than a coordinate grid. A guillotine cut is
    // allowed only when no detail lies across it; child cuts stay inside their region.
    public static IReadOnlyList<CutLine> LinesForSheet(CutSheet sheet, double trim)
    {
        if (sheet.Placements.Count == 0) return [];
        var left = Math.Max(0, trim);
        var top = Math.Max(0, trim);
        var right = Math.Max(left, sheet.SheetLength - trim);
        var bottom = Math.Max(top, sheet.SheetWidth - trim);
        if (right - left <= Epsilon || bottom - top <= Epsilon) return [];

        var lines = new List<CutLine>();
        if (trim > Epsilon)
        {
            lines.AddRange([
                new CutLine(true, left, 0, sheet.SheetWidth, true),
                new CutLine(true, right, 0, sheet.SheetWidth, true),
                new CutLine(false, top, left, right, true),
                new CutLine(false, bottom, left, right, true)
            ]);
        }

        var parts = sheet.Placements
            .Where(part => part.Length > Epsilon && part.Width > Epsilon)
            .Select(part => new PartRect(part.X, part.Y, part.X + part.Length, part.Y + part.Width))
            .ToList();
        var unresolved = new List<(CutRegion Region, List<PartRect> Parts)>();
        Split(new CutRegion(left, top, right, bottom), parts, lines, unresolved, 0);

        // A MaxRects layout may not be a perfect guillotine tree. Fall back to local
        // boundaries only: they follow an edge and never pass through a detail.
        foreach (var (region, localParts) in unresolved)
            AddLocalEdges(region, localParts, lines);

        return Deduplicate(lines);
    }

    private static void Split(CutRegion region, List<PartRect> parts, List<CutLine> lines,
        List<(CutRegion Region, List<PartRect> Parts)> unresolved, int depth)
    {
        if (parts.Count == 0) return;
        if (depth > 256) { unresolved.Add((region, parts)); return; }
        var candidate = Candidates(region, parts)
            // First isolate a full empty side. This preserves a large useful remainder
            // instead of running crosscuts through it while processing a narrow strip.
            .OrderByDescending(item => item.EmptySide)
            // The sheet length is the horizontal axis. A vertical line is therefore
            // a crosscut; choose it before a longitudinal line when both split the
            // current area without touching a detail.
            .ThenBy(item => item.IsVertical ? 0 : 1)
            .ThenByDescending(item => item.SplitParts)
            .ThenBy(item => item.Length)
            .FirstOrDefault();
        if (candidate is null)
        {
            unresolved.Add((region, parts));
            return;
        }

        lines.Add(candidate.Line);
        if (candidate.IsVertical)
        {
            var left = parts.Where(part => part.Right <= candidate.Position + Epsilon).ToList();
            var right = parts.Where(part => part.Left >= candidate.Position - Epsilon).ToList();
            Split(new CutRegion(region.Left, region.Top, candidate.Position, region.Bottom), left, lines, unresolved, depth + 1);
            Split(new CutRegion(candidate.Position, region.Top, region.Right, region.Bottom), right, lines, unresolved, depth + 1);
        }
        else
        {
            var above = parts.Where(part => part.Bottom <= candidate.Position + Epsilon).ToList();
            var below = parts.Where(part => part.Top >= candidate.Position - Epsilon).ToList();
            Split(new CutRegion(region.Left, region.Top, region.Right, candidate.Position), above, lines, unresolved, depth + 1);
            Split(new CutRegion(region.Left, candidate.Position, region.Right, region.Bottom), below, lines, unresolved, depth + 1);
        }
    }

    private static IEnumerable<CutCandidate> Candidates(CutRegion region, List<PartRect> parts)
    {
        foreach (var position in parts.SelectMany(part => new[] { part.Left, part.Right }).Distinct().Where(region.ContainsX))
        {
            if (parts.Any(part => part.Left < position - Epsilon && part.Right > position + Epsilon)) continue;
            var left = parts.Count(part => part.Right <= position + Epsilon);
            var right = parts.Count(part => part.Left >= position - Epsilon);
            if (left + right != parts.Count || left == 0 && right == 0) continue;
            yield return new CutCandidate(true, position, new CutLine(true, position, region.Top, region.Bottom),
                left == 0 || right == 0 ? 1 : 0, Math.Min(left, right), region.Height);
        }
        foreach (var position in parts.SelectMany(part => new[] { part.Top, part.Bottom }).Distinct().Where(region.ContainsY))
        {
            if (parts.Any(part => part.Top < position - Epsilon && part.Bottom > position + Epsilon)) continue;
            var above = parts.Count(part => part.Bottom <= position + Epsilon);
            var below = parts.Count(part => part.Top >= position - Epsilon);
            if (above + below != parts.Count || above == 0 && below == 0) continue;
            yield return new CutCandidate(false, position, new CutLine(false, position, region.Left, region.Right),
                above == 0 || below == 0 ? 1 : 0, Math.Min(above, below), region.Width);
        }
    }

    private static void AddLocalEdges(CutRegion region, IEnumerable<PartRect> parts, ICollection<CutLine> lines)
    {
        foreach (var part in parts)
        {
            if (part.Left > region.Left + Epsilon) lines.Add(new CutLine(true, part.Left, part.Top, part.Bottom));
            if (part.Right < region.Right - Epsilon) lines.Add(new CutLine(true, part.Right, part.Top, part.Bottom));
            if (part.Top > region.Top + Epsilon) lines.Add(new CutLine(false, part.Top, part.Left, part.Right));
            if (part.Bottom < region.Bottom - Epsilon) lines.Add(new CutLine(false, part.Bottom, part.Left, part.Right));
        }
    }

    private static List<CutLine> Deduplicate(IEnumerable<CutLine> lines) => lines
        .Where(line => line.Length > Epsilon)
        .GroupBy(line => (line.IsVertical, Position: Math.Round(line.Position, 4), Start: Math.Round(line.Start, 4), End: Math.Round(line.End, 4), line.IsTrim))
        .Select(group => group.First())
        .ToList();

    private static CutMeasure Add(CutMeasure first, CutMeasure second) => new(first.Count + second.Count, first.Length + second.Length);

    public static IReadOnlyList<CutRemainder> RemaindersForSheet(CutSheet sheet, double trim, double gap, double minimum)
    {
        var free = FreeRectangles(sheet, trim, gap);
        // The stock rectangles must remain intact after every displayed saw pass.
        foreach (var line in LinesForSheet(sheet, trim).Where(line => !line.IsTrim))
        {
            var next = new List<CutRemainder>();
            foreach (var rect in free)
            {
                if (line.IsVertical && line.Position > rect.X + Epsilon && line.Position < rect.X + rect.Length - Epsilon &&
                    line.End > rect.Y + Epsilon && line.Start < rect.Y + rect.Width - Epsilon)
                {
                    next.Add(rect with { Length = line.Position - rect.X });
                    next.Add(rect with { X = line.Position, Length = rect.X + rect.Length - line.Position });
                }
                else if (!line.IsVertical && line.Position > rect.Y + Epsilon && line.Position < rect.Y + rect.Width - Epsilon &&
                    line.End > rect.X + Epsilon && line.Start < rect.X + rect.Length - Epsilon)
                {
                    next.Add(rect with { Width = line.Position - rect.Y });
                    next.Add(rect with { Y = line.Position, Width = rect.Y + rect.Width - line.Position });
                }
                else next.Add(rect);
            }
            free = next;
        }
        var threshold = double.IsFinite(minimum) ? Math.Max(0, minimum) : 0;
        return free.Where(r => r.Length > Epsilon && r.Width > Epsilon && r.Length >= threshold && r.Width >= threshold).ToList();
    }

    private static List<CutRemainder> FreeRectangles(CutSheet sheet, double trim, double gap)
    {
        var right = sheet.SheetLength - trim;
        var bottom = sheet.SheetWidth - trim;
        if (right <= trim || bottom <= trim) return [];
        var blocked = sheet.Placements.Select(part => new
        {
            Left = Math.Max(trim, part.X), Top = Math.Max(trim, part.Y),
            Right = Math.Min(right, part.X + part.Length + gap), Bottom = Math.Min(bottom, part.Y + part.Width + gap)
        }).ToList();
        var xs = blocked.SelectMany(item => new[] { item.Left, item.Right }).Append(trim).Append(right).Distinct().Order().ToArray();
        var ys = blocked.SelectMany(item => new[] { item.Top, item.Bottom }).Append(trim).Append(bottom).Distinct().Order().ToArray();
        // Build vertical free bands first. In a strip layout this keeps the broad area
        // beside the strip as one stock rectangle instead of splitting it at each part.
        var bands = new List<(double X, double Y, double Length, double Width)>();
        for (var column = 0; column < xs.Length - 1; column++)
        {
            var x = xs[column]; var width = xs[column + 1] - x;
            var start = -1;
            for (var row = 0; row < ys.Length - 1; row++)
            {
                var y = ys[row]; var height = ys[row + 1] - y;
                var isFree = width > Epsilon && height > Epsilon && !blocked.Any(item =>
                    x < item.Right - Epsilon && x + width > item.Left + Epsilon && y < item.Bottom - Epsilon && y + height > item.Top + Epsilon);
                if (isFree && start < 0) start = row;
                if (!isFree && start >= 0) { bands.Add((x, ys[start], width, y - ys[start])); start = -1; }
            }
            if (start >= 0) bands.Add((x, ys[start], width, bottom - ys[start]));
        }
        var rectangles = new List<(double X, double Y, double Length, double Width)>();
        foreach (var band in bands.OrderBy(item => item.X).ThenBy(item => item.Y))
        {
            var index = rectangles.FindIndex(item => Math.Abs(item.Y - band.Y) < Epsilon && Math.Abs(item.Width - band.Width) < Epsilon &&
                Math.Abs(item.X + item.Length - band.X) < Epsilon);
            if (index >= 0)
            {
                var current = rectangles[index];
                rectangles[index] = (current.X, current.Y, current.Length + band.Length, current.Width);
            }
            else rectangles.Add(band);
        }
        return rectangles.Select(item => new CutRemainder(item.X, item.Y, item.Length, item.Width)).Where(item => item.Length > Epsilon && item.Width > Epsilon).ToList();
    }

    private sealed record PartRect(double Left, double Top, double Right, double Bottom);
    private sealed record CutRegion(double Left, double Top, double Right, double Bottom)
    {
        public double Width => Right - Left;
        public double Height => Bottom - Top;
        public bool ContainsX(double value) => value > Left + Epsilon && value < Right - Epsilon;
        public bool ContainsY(double value) => value > Top + Epsilon && value < Bottom - Epsilon;
    }
    private sealed record CutCandidate(bool IsVertical, double Position, CutLine Line, int EmptySide, int SplitParts, double Length);
}
