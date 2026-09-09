using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public static class CutPlanService
{
    public static string Signature(CutReport report)
    {
        var input = new
        {
            report.Trim, report.Gap,
            Groups = report.Groups.OrderBy(g => g.MaterialId).Select(g => new
            {
                g.MaterialId, g.SheetLength, g.SheetWidth, g.TextureDirection,
                Parts = g.Sheets.SelectMany(s => s.Placements).OrderBy(p => p.InstanceId).Select(p => new { p.InstanceId, p.BaseLength, p.BaseWidth, p.AllowRotation })
            }),
            report.Unplaced
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));
    }

    public static SavedCutPlan Capture(CutReport report, string signature, string name, string algorithm, bool automatic = false) => new()
    {
        Name = name, Algorithm = algorithm, Automatic = automatic, InputSignature = signature,
        SheetCounts = report.Groups.ToDictionary(g => g.MaterialId, g => g.Sheets.Count),
        Layout = report.Groups.SelectMany(g => g.Sheets.SelectMany(s => s.Placements.Select(p => new CutLayoutOverride
        {
            MaterialId = g.MaterialId, SheetNumber = s.Number, InstanceId = p.InstanceId,
            BaseLength = p.BaseLength, BaseWidth = p.BaseWidth, X = p.X, Y = p.Y, Length = p.Length, Width = p.Width, Rotated = p.Rotated
        }))).ToList()
    };

    public static CutReport? Restore(CutReport baseline, SavedCutPlan plan)
    {
        if (plan.InputSignature != Signature(baseline) || plan.Layout is null || plan.SheetCounts is null) return null;
        var all = baseline.Groups.SelectMany(g => g.Sheets).SelectMany(s => s.Placements).ToList();
        if (plan.Layout.Count != all.Count || plan.Layout.Select(p => p.InstanceId).Distinct().Count() != all.Count) return null;
        if (plan.Layout.Any(p => !baseline.Groups.Any(g => g.MaterialId == p.MaterialId))) return null;
        var groups = new List<CutGroup>();
        foreach (var group in baseline.Groups)
        {
            if (!plan.SheetCounts.TryGetValue(group.MaterialId, out var count) || count < 0 || count > Math.Max(1, all.Count + baseline.Groups.Sum(g => g.Sheets.Count))) return null;
            var originals = group.Sheets.SelectMany(s => s.Placements).ToDictionary(p => p.InstanceId);
            var sheets = Enumerable.Range(1, count).Select(i => new CutSheet(i, group.SheetLength, group.SheetWidth, 0, [])).ToList();
            foreach (var p in plan.Layout.Where(p => p.MaterialId == group.MaterialId))
            {
                if (!originals.TryGetValue(p.InstanceId, out var original) || p.SheetNumber < 1 || p.SheetNumber > count ||
                    !Same(p.BaseLength, original.BaseLength) || !Same(p.BaseWidth, original.BaseWidth) ||
                    p.Rotated && (!original.AllowRotation || group.TextureDirection) ||
                    !Same(p.Length, p.Rotated ? original.BaseWidth : original.BaseLength) ||
                    !Same(p.Width, p.Rotated ? original.BaseLength : original.BaseWidth)) return null;
                sheets[p.SheetNumber - 1].Placements.Add(original with { X = p.X, Y = p.Y, Length = p.Length, Width = p.Width, Rotated = p.Rotated });
            }
            if (sheets.Sum(s => s.Placements.Count) != originals.Count || sheets.Any(s => !CutEditing.Valid(s.Placements, s.SheetLength, s.SheetWidth, baseline.Trim, baseline.Gap))) return null;
            groups.Add(group with { Sheets = sheets });
        }
        return CutEditing.Renumber(baseline with { Groups = groups });
    }

    public static SavedCutPlan Copy(SavedCutPlan plan) => JsonSerializer.Deserialize<SavedCutPlan>(JsonSerializer.Serialize(plan))!;
    private static bool Same(double a, double b) => double.IsFinite(a) && double.IsFinite(b) && Math.Abs(a - b) < 1e-6;
}
