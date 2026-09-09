namespace VitanCut.WinUI.Services;

public sealed record CutEditProposal(CutReport Report, int NewSheets = 0, string Error = "", string SheetSummary = "",
    string ConfirmationTitle = "", string ConfirmationContent = "", string ConfirmationAction = "")
{
    public bool Success => Error.Length == 0;
}

public sealed record CutSheetReference(string MaterialId, int Number);

public static class CutEditing
{
    private const double Epsilon = 1e-6;
    public static bool Overlap(CutPlacement a, CutPlacement b, double gap) =>
        a.X < b.X + b.Length + gap - Epsilon && a.X + a.Length + gap > b.X + Epsilon &&
        a.Y < b.Y + b.Width + gap - Epsilon && a.Y + a.Width + gap > b.Y + Epsilon;

    public static bool Valid(IReadOnlyList<CutPlacement> parts, double width, double height, double trim, double gap)
    {
        if (parts.Any(p => !double.IsFinite(p.X + p.Y + p.Length + p.Width) || p.Length <= 0 || p.Width <= 0 ||
            p.X < trim - Epsilon || p.Y < trim - Epsilon || p.X + p.Length > width - trim + Epsilon || p.Y + p.Width > height - trim + Epsilon)) return false;
        for (var i = 0; i < parts.Count; i++)
        for (var j = i + 1; j < parts.Count; j++)
            if (Overlap(parts[i], parts[j], gap)) return false;
        return true;
    }

    // Project the intended translation onto the collision-free region. Candidate X values
    // lie on obstacle boundaries; at each X, project onto gaps between forbidden Y intervals.
    public static (double X, double Y)? Nearest(IReadOnlyList<CutPlacement> moving, IReadOnlyList<CutPlacement> obstacles,
        double width, double height, double trim, double gap, double desiredX = 0, double desiredY = 0)
    {
        if (moving.Count == 0) return null;
        for (var i = 0; i < moving.Count; i++)
        for (var j = i + 1; j < moving.Count; j++)
            if (Overlap(moving[i], moving[j], gap)) return null;
        var minX = trim - moving.Min(p => p.X);
        var maxX = width - trim - moving.Max(p => p.X + p.Length);
        var minY = trim - moving.Min(p => p.Y);
        var maxY = height - trim - moving.Max(p => p.Y + p.Width);
        if (maxX < minX - Epsilon || maxY < minY - Epsilon) return null;
        maxX = Math.Max(minX, maxX); maxY = Math.Max(minY, maxY);
        var xs = new HashSet<double> { Math.Clamp(desiredX, minX, maxX), minX, maxX };
        foreach (var p in moving)
        foreach (var other in obstacles)
        {
            xs.Add(Math.Clamp(other.X - gap - p.X - p.Length, minX, maxX));
            xs.Add(Math.Clamp(other.X + other.Length + gap - p.X, minX, maxX));
        }
        (double X, double Y)? best = null;
        var distance = double.PositiveInfinity;
        foreach (var x in xs.OrderBy(x => Math.Abs(x - desiredX)))
        {
            if ((x - desiredX) * (x - desiredX) > distance + Epsilon) break;
            var intervals = new List<(double Start, double End)>();
            foreach (var p in moving)
            foreach (var other in obstacles)
                if (p.X + x < other.X + other.Length + gap - Epsilon && p.X + x + p.Length + gap > other.X + Epsilon)
                    intervals.Add((other.Y - gap - p.Y - p.Width, other.Y + other.Width + gap - p.Y));
            var candidates = new List<double> { Math.Clamp(desiredY, minY, maxY), minY, maxY };
            // Boundary points can be valid even when two open forbidden intervals touch.
            foreach (var interval in intervals) { candidates.Add(interval.Start); candidates.Add(interval.End); }
            foreach (var y in candidates.Where(y => y >= minY - Epsilon && y <= maxY + Epsilon).OrderBy(y => Math.Abs(y - desiredY)))
            {
                var d = (x - desiredX) * (x - desiredX) + (y - desiredY) * (y - desiredY);
                if (d >= distance) break;
                if (intervals.Any(i => y > i.Start + Epsilon && y < i.End - Epsilon)) continue;
                best = (x, y); distance = d; break;
            }
        }
        return best;
    }

    public static (double X, double Y) Snap(CutPlacement part, IReadOnlyList<CutPlacement> others, double width, double height, double trim, double gap, double distance = 12)
    {
        var xs = new List<double> { trim, width - trim - part.Length };
        var ys = new List<double> { trim, height - trim - part.Width };
        foreach (var p in others)
        {
            xs.AddRange([p.X, p.X + p.Length - part.Length, p.X - part.Length - gap, p.X + p.Length + gap]);
            ys.AddRange([p.Y, p.Y + p.Width - part.Width, p.Y - part.Width - gap, p.Y + p.Width + gap]);
        }
        double Closest(double value, List<double> targets) => targets.OrderBy(t => Math.Abs(t - value)).FirstOrDefault(t => Math.Abs(t - value) <= distance, value);
        return (Closest(part.X, xs), Closest(part.Y, ys));
    }

    public static CutEditProposal Move(CutReport source, IReadOnlySet<string> ids, string materialId, int sheetNumber, string anchorId, double x, double y)
    {
        var group = source.Groups.FirstOrDefault(g => g.MaterialId == materialId);
        var target = group?.Sheets.FirstOrDefault(s => s.Number == sheetNumber);
        if (target is null) return new(source, Error: "Лист назначения не найден.");
        var moving = group!.Sheets.SelectMany(s => s.Placements).Where(p => ids.Contains(p.InstanceId)).ToList();
        if (moving.Count != ids.Count) return new(source, Error: "Между листами можно переносить только детали одного материала.");
        var anchor = moving.FirstOrDefault(p => p.InstanceId == anchorId);
        if (anchor is null) return new(source, Error: "Не выбрана деталь для переноса.");
        var wanted = moving.Select(p => p with { X = p.X + x - anchor.X, Y = p.Y + y - anchor.Y }).ToList();
        var copy = Clone(source);
        var destination = copy.Groups.First(g => g.MaterialId == materialId);
        foreach (var sheet in destination.Sheets) sheet.Placements.RemoveAll(p => ids.Contains(p.InstanceId));
        var index = destination.Sheets.FindIndex(s => s.Number == sheetNumber);
        return Arrange(copy, destination, index, wanted, source);
    }

    public static CutEditProposal MoveToNewSheet(CutReport source, IReadOnlySet<string> ids, string materialId, int sourceSheetNumber, string anchorId)
    {
        var group = source.Groups.FirstOrDefault(g => g.MaterialId == materialId);
        var sourceSheet = group?.Sheets.FirstOrDefault(s => s.Number == sourceSheetNumber);
        if (sourceSheet is null) return new(source, Error: "Исходный лист не найден.");

        var moving = group!.Sheets.SelectMany(s => s.Placements).Where(p => ids.Contains(p.InstanceId)).ToList();
        if (moving.Count != ids.Count) return new(source, Error: "На новый лист можно переносить только детали одного материала.");
        if (!moving.Any(p => p.InstanceId == anchorId)) return new(source, Error: "Не выбрана деталь для переноса.");

        var copy = Clone(source);
        var destination = copy.Groups.First(g => g.MaterialId == materialId);
        foreach (var sheet in destination.Sheets) sheet.Placements.RemoveAll(p => ids.Contains(p.InstanceId));
        var sourceIndex = destination.Sheets.FindIndex(s => s.Number == sourceSheetNumber);
        if (sourceIndex < 0) return new(source, Error: "Исходный лист не найден.");

        // A deliberate drop outside a sheet always starts with a fresh sheet immediately
        // after the source. Arrange() keeps a dragged group together where possible.
        destination.Sheets.Insert(sourceIndex + 1, new CutSheet(0, destination.SheetLength, destination.SheetWidth, 0, []));
        var arranged = Arrange(copy, destination, sourceIndex + 1, moving, source);
        if (!arranged.Success) return arranged;
        var created = arranged.NewSheets + 1;
        return arranged with
        {
            NewSheets = created,
            SheetSummary = $"{destination.MaterialName}, после листа {sourceSheetNumber}: +{created}",
            ConfirmationTitle = "Создать новый лист?",
            ConfirmationContent = "Детали отпущены за пределами листа. Создать новый лист и перенести их туда?",
            ConfirmationAction = "Создать и перенести"
        };
    }

    public static CutReport RemoveEmptySheets(CutReport source, IEnumerable<CutSheetReference> sheets)
    {
        var requested = sheets.ToHashSet();
        if (requested.Count == 0) return source;
        var copy = Clone(source);
        foreach (var group in copy.Groups)
            group.Sheets.RemoveAll(sheet => sheet.Placements.Count == 0 && requested.Contains(new CutSheetReference(group.MaterialId, sheet.Number)));
        return Renumber(copy);
    }

    public static HashSet<string> IdenticalIds(CutReport source, string anchorId, bool allSheets = false)
    {
        foreach (var group in source.Groups)
        foreach (var sheet in group.Sheets)
        {
            var anchor = sheet.Placements.FirstOrDefault(p => p.InstanceId == anchorId);
            if (anchor is null) continue;
            var parts = allSheets ? group.Sheets.SelectMany(s => s.Placements) : sheet.Placements;
            return parts.Where(p => p.ProductId == anchor.ProductId && p.DetailId == anchor.DetailId)
                .Select(p => p.InstanceId).ToHashSet();
        }
        return [];
    }

    public static CutEditProposal Rotate(CutReport source, IReadOnlySet<string> ids)
    {
        var selected = source.Groups.SelectMany(g => g.Sheets).SelectMany(s => s.Placements).Where(p => ids.Contains(p.InstanceId)).ToList();
        if (selected.Count == 0 || selected.Count != ids.Count) return new(source, Error: "Не выбраны детали.");
        if (selected.Any(p => !p.AllowRotation)) return new(source, Error: "В выделении есть детали с запрещённым поворотом.");
        var copy = Clone(source);
        var created = 0;
        var affected = new List<string>();
        foreach (var group in copy.Groups)
        foreach (var sheet in group.Sheets.ToList())
        {
            var moving = sheet.Placements.Where(p => ids.Contains(p.InstanceId)).ToList();
            if (moving.Count == 0) continue;
            if (group.TextureDirection) return new(source, Error: "Направление текстуры запрещает поворот.");
            sheet.Placements.RemoveAll(p => ids.Contains(p.InstanceId));
            var rotated = moving.Select(p => p with { Length = p.Width, Width = p.Length, Rotated = !p.Rotated }).ToList();
            var proposal = Arrange(copy, group, group.Sheets.IndexOf(sheet), rotated, source, align: true);
            if (!proposal.Success) return proposal;
            created += proposal.NewSheets;
            if (proposal.NewSheets > 0) affected.Add(proposal.SheetSummary);
        }
        return new(Renumber(copy), created, SheetSummary: string.Join("\n", affected));
    }

    public static CutEditProposal OrderSheet(CutReport source, string materialId, int sheetNumber)
    {
        var group = source.Groups.FirstOrDefault(item => item.MaterialId == materialId);
        var sheet = group?.Sheets.FirstOrDefault(item => item.Number == sheetNumber);
        if (sheet is null) return new(source, Error: "Лист не найден.");
        if (sheet.Placements.Count < 2) return new(source);

        // Keep orientation intact and try a few stable traversal orders. Nearest() anchors
        // each next part as close to the top-left corner as the occupied space permits.
        var sourceOrders = new[]
        {
            sheet.Placements.OrderBy(part => part.Y).ThenBy(part => part.X).ThenBy(part => part.InstanceId, StringComparer.Ordinal),
            sheet.Placements.OrderByDescending(part => part.Length * part.Width).ThenBy(part => part.InstanceId, StringComparer.Ordinal),
            sheet.Placements.OrderByDescending(part => Math.Max(part.Length, part.Width)).ThenBy(part => part.InstanceId, StringComparer.Ordinal)
        };
        CutReport? best = null;
        (int Cuts, double Bottom, double Right, double Extent)? bestScore = null;
        foreach (var sourceOrder in sourceOrders)
        {
            var ordered = new List<CutPlacement>();
            foreach (var part in sourceOrder)
            {
                var origin = part with { X = source.Trim, Y = source.Trim };
                var shift = Nearest([origin], ordered, sheet.SheetLength, sheet.SheetWidth, source.Trim, source.Gap);
                if (shift is null) { ordered.Clear(); break; }
                ordered.Add(origin with { X = origin.X + shift.Value.X, Y = origin.Y + shift.Value.Y });
            }
            if (ordered.Count != sheet.Placements.Count || !Valid(ordered, sheet.SheetLength, sheet.SheetWidth, source.Trim, source.Gap)) continue;
            var copy = Clone(source);
            var targetGroup = copy.Groups.First(item => item.MaterialId == materialId);
            var targetIndex = targetGroup.Sheets.FindIndex(item => item.Number == sheetNumber);
            targetGroup.Sheets[targetIndex] = targetGroup.Sheets[targetIndex] with { Placements = ordered };
            var measure = CuttingMetrics.ForSheet(targetGroup.Sheets[targetIndex], copy.Trim);
            var score = (measure.Count, ordered.Max(part => part.Y + part.Width), ordered.Max(part => part.X + part.Length),
                ordered.Max(part => part.Y + part.Width) + ordered.Max(part => part.X + part.Length));
            if (bestScore is not { } current || score.CompareTo(current) < 0)
            {
                best = copy;
                bestScore = score;
            }
        }
        if (best is not null) return new(Renumber(best));
        return new(source, Error: "Не удалось упорядочить детали без пересечений.");
    }

    private static CutEditProposal Arrange(CutReport copy, CutGroup group, int index, List<CutPlacement> moving, CutReport original, bool align = false)
    {
        if (moving.Any(p => p.Length > group.SheetLength - 2 * copy.Trim + Epsilon || p.Width > group.SheetWidth - 2 * copy.Trim + Epsilon))
            return new(original, Error: "В этой ориентации деталь не помещается даже на пустой лист материала.");
        var target = group.Sheets[index];
        if (align && moving.Count > 1)
        {
            var aligned = Aligned(moving, target.Placements, group.SheetLength, group.SheetWidth, copy.Trim, copy.Gap);
            if (aligned is not null)
            {
                target.Placements.AddRange(aligned);
                return new(Renumber(copy));
            }
            // Fragmented free space may not fit a complete row layout. Keep a common
            // placement origin instead of recreating the pre-rotation staircase.
            var x = moving.Min(p => p.X); var y = moving.Min(p => p.Y);
            moving = moving.Select(p => p with { X = x, Y = y }).ToList();
        }
        var rigid = Nearest(moving, target.Placements, group.SheetLength, group.SheetWidth, copy.Trim, copy.Gap);
        if (rigid is { } rigidShift)
        {
            target.Placements.AddRange(moving.Select(p => p with { X = p.X + rigidShift.X, Y = p.Y + rigidShift.Y }));
            return new(Renumber(copy));
        }
        var overflow = new List<CutPlacement>();
        foreach (var part in moving.OrderByDescending(p => p.Length * p.Width))
        {
            var free = Nearest([part], target.Placements, group.SheetLength, group.SheetWidth, copy.Trim, copy.Gap);
            if (free is { } offset) target.Placements.Add(part with { X = part.X + offset.X, Y = part.Y + offset.Y });
            else overflow.Add(part);
        }
        var added = new List<CutSheet>();
        foreach (var part in overflow)
        {
            CutSheet? destination = null;
            (double X, double Y)? shift = null;
            foreach (var sheet in added)
            {
                shift = Nearest([part], sheet.Placements, group.SheetLength, group.SheetWidth, copy.Trim, copy.Gap);
                if (shift is not null) { destination = sheet; break; }
            }
            if (destination is null)
            {
                destination = new CutSheet(0, group.SheetLength, group.SheetWidth, 0, []);
                added.Add(destination);
                shift = Nearest([part], [], group.SheetLength, group.SheetWidth, copy.Trim, copy.Gap);
            }
            destination.Placements.Add(part with { X = part.X + shift!.Value.X, Y = part.Y + shift.Value.Y });
        }
        group.Sheets.InsertRange(index + 1, added);
        return new(Renumber(copy), added.Count, SheetSummary: added.Count > 0 ? $"{group.MaterialName}, исходный лист {target.Number}: +{added.Count}" : "");
    }

    private static List<CutPlacement>? Aligned(List<CutPlacement> moving, IReadOnlyList<CutPlacement> obstacles,
        double width, double height, double trim, double gap)
    {
        var originX = moving.Min(p => p.X); var originY = moving.Min(p => p.Y);
        var ordered = moving.OrderByDescending(p => p.Width).ThenByDescending(p => p.Length)
            .ThenBy(p => p.ProductId, StringComparer.Ordinal).ThenBy(p => p.DetailId, StringComparer.Ordinal)
            .ThenBy(p => p.InstanceId, StringComparer.Ordinal).ToList();
        var columns = Enumerable.Range(1, Math.Min(moving.Count, 16))
            .Concat(Enumerable.Range(1, 16).Select(i => Math.Max(1, (int)Math.Ceiling(moving.Count * i / 16.0))))
            .Distinct().Order();
        List<CutPlacement>? best = null;
        var bestDistance = double.PositiveInfinity;
        var bestExtent = double.PositiveInfinity;
        foreach (var count in columns)
        {
            var layout = new List<CutPlacement>(moving.Count);
            var y = originY;
            for (var first = 0; first < ordered.Count; first += count)
            {
                var x = originX; var rowHeight = 0.0;
                for (var i = first; i < Math.Min(first + count, ordered.Count); i++)
                {
                    var part = ordered[i];
                    layout.Add(part with { X = x, Y = y });
                    x += part.Length + gap; rowHeight = Math.Max(rowHeight, part.Width);
                }
                y += rowHeight + gap;
            }
            var shift = Nearest(layout, obstacles, width, height, trim, gap);
            if (shift is not { } offset) continue;
            var distance = offset.X * offset.X + offset.Y * offset.Y;
            var extent = layout.Max(p => p.X + p.Length) - originX + layout.Max(p => p.Y + p.Width) - originY;
            if (distance > bestDistance + Epsilon || Math.Abs(distance - bestDistance) <= Epsilon && extent >= bestExtent) continue;
            var placed = layout.Select(p => p with { X = p.X + offset.X, Y = p.Y + offset.Y }).ToList();
            if (!Valid(obstacles.Concat(placed).ToList(), width, height, trim, gap)) continue;
            best = placed; bestDistance = distance; bestExtent = extent;
        }
        return best;
    }

    public static CutReport Clone(CutReport report) => report with { Groups = report.Groups.Select(g => g with { Sheets = g.Sheets.Select(s => s with { Placements = s.Placements.ToList() }).ToList() }).ToList() };
    public static CutReport Renumber(CutReport report) => report with
    {
        Groups = report.Groups.Select(g => g with
        {
            Sheets = g.Sheets.Select((s, i) =>
            {
                // DisplayNumber is assigned when the cut is generated. Manual moves and
                // rotations must not change it, otherwise the legend changes under the user.
                var area = s.Placements.Sum(p => p.Length * p.Width);
                return s with { Number = i + 1, Efficiency = area / (s.SheetLength * s.SheetWidth) };
            }).ToList()
        }).ToList()
    };
}
