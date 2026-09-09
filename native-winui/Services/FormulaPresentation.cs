using System.Text.RegularExpressions;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

// Each editor keeps its own label map, so renaming a detail cannot retarget typed references.
public sealed class FormulaPresentation(Product product)
{
    private static readonly Regex Reference = new(@"\[\[detail:(?<id>[^:\]]+):(?<side>length|width)\]\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex Label = new("⟦[^⟧]*⟧", RegexOptions.Compiled);
    private readonly Dictionary<string, string> _references = new(StringComparer.OrdinalIgnoreCase);

    public string Display(string expression)
    {
        var text = Reference.Replace(expression, match =>
        {
            var id = match.Groups["id"].Value;
            var detail = product.Details.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            var name = detail is null ? "Удалённая деталь" : Name(detail);
            if (detail is not null && product.Details.Count(item => Name(item).Equals(name, StringComparison.OrdinalIgnoreCase)) > 1)
                name += $" (деталь {product.Details.IndexOf(detail) + 1})";
            var side = match.Groups["side"].Value.Equals("width", StringComparison.OrdinalIgnoreCase) ? "ширина" : "длина";
            var label = $"⟦{name} · {side}⟧";
            var suffix = 2;
            while (_references.TryGetValue(LabelKey(label), out var existing) && !existing.Equals(match.Value, StringComparison.OrdinalIgnoreCase))
                label = $"⟦{name} ({suffix++}) · {side}⟧";
            _references[LabelKey(label)] = match.Value;
            return label;
        });
        // Only separate reference boundaries; do not rewrite operators inside fixed-size names.
        text = Regex.Replace(text, @"([=+*/×÷−-])\s*(?=⟦)", "$1 ");
        return Regex.Replace(text, @"(?<=⟧)\s*([+*/×÷−-])\s*", " $1 ");
    }

    public string Storage(string expression)
    {
        if (Label.Matches(expression).Any(match => !_references.ContainsKey(LabelKey(match.Value))))
            foreach (var detail in product.Details)
            {
                Display($"[[detail:{detail.Id}:length]]");
                Display($"[[detail:{detail.Id}:width]]");
            }
        return Label.Replace(expression,
            match => _references.TryGetValue(LabelKey(match.Value), out var reference) ? reference : match.Value);
    }

    private static string LabelKey(string label) => FormulaText.NormalizeWhitespace(label)
        .Replace("⟦ ", "⟦").Replace(" ⟧", "⟧").Replace(" ·", "·").Replace("· ", "·");

    private static string Name(Detail detail) => (string.IsNullOrWhiteSpace(detail.Name) ? "Без названия" : FormulaText.NormalizeWhitespace(detail.Name))
        .Replace('⟦', '〔').Replace('⟧', '〕');
}
