using System.Globalization;
using System.Text.RegularExpressions;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public sealed record FormulaResult(double Value, string Error = "")
{
    public bool IsValid => Error.Length == 0;
}

public static class FormulaEvaluator
{
    private static readonly Regex Reference = new(@"\[\[detail:(?<id>[^:\]]+):(?<side>length|width)\]\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string RemapReferences(string formula, IReadOnlyDictionary<string, string> detailIds) =>
        Reference.Replace(formula, match => detailIds.TryGetValue(match.Groups["id"].Value, out var replacement)
            ? $"[[detail:{replacement}:{match.Groups["side"].Value}]]"
            : match.Value);

    public static string SwapDimensionReferences(string formula, string detailId) =>
        Reference.Replace(formula, match => match.Groups["id"].Value.Equals(detailId, StringComparison.OrdinalIgnoreCase)
            ? $"[[detail:{match.Groups["id"].Value}:{(match.Groups["side"].Value.Equals("length", StringComparison.OrdinalIgnoreCase) ? "width" : "length")}]]"
            : match.Value);

    public static FormulaResult Evaluate(Product product, Detail detail, bool width = false)
    {
        try
        {
            var value = Resolve(product, detail, width, new HashSet<string>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));
            if (!double.IsFinite(value) || value < 0) return new(0, "Размер должен быть конечным неотрицательным числом");
            return new(value);
        }
        catch (FormatException error) { return new(0, error.Message); }
        catch (OverflowException) { return new(0, "Слишком большое значение формулы"); }
    }

    private static double Resolve(Product product, Detail detail, bool width, HashSet<string> resolving, Dictionary<string, double> cache)
    {
        var key = detail.Id + (width ? ":width" : ":length");
        if (cache.TryGetValue(key, out var cached)) return cached;
        if (resolving.Count >= 100 || !resolving.Add(key)) throw new FormatException("Циклическая ссылка в формуле");
        try
        {
            var text = (width ? detail.WidthExpr : detail.LengthExpr)?.Trim() ?? "";
            if (text.Length > 8192) throw new FormatException("Слишком длинная формула");
            if (text.StartsWith('=')) text = text[1..];
            text = text.Replace(',', '.').Replace('×', '*').Replace('÷', '/').Replace('−', '-');
            text = Reference.Replace(text, match =>
            {
                var linked = product.Details.FirstOrDefault(item => string.Equals(item.Id, match.Groups["id"].Value, StringComparison.OrdinalIgnoreCase))
                    ?? throw new FormatException("Деталь, на которую ссылается формула, удалена");
                return "(" + Resolve(product, linked, match.Groups["side"].Value.Equals("width", StringComparison.OrdinalIgnoreCase), resolving, cache).ToString("R", CultureInfo.InvariantCulture) + ")";
            });
            text = FormulaText.NormalizeWhitespace(text);
            var variables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["длина изделия"] = product.Length,
                ["ширина изделия"] = product.Depth,
                ["глубина изделия"] = product.Depth,
                ["высота изделия"] = product.Height,
                ["высота ножки"] = product.HasLegs ? product.LegHeight : 0
            };
            foreach (var size in product.FixedSizes.Where(size => !string.IsNullOrWhiteSpace(size.Name)))
                variables.TryAdd(FormulaText.NormalizeWhitespace(size.Name), size.Value);
            // Replace whole variable names in one pass, longest first (e.g. "Полка 2" before "Полка").
            var pattern = @"(?<![\p{L}\p{N}_])(?:" + string.Join("|", variables.Keys.OrderByDescending(name => name.Length).Select(Regex.Escape)) + @")(?![\p{L}\p{N}_])";
            text = Regex.Replace(text, pattern, match => "(" + variables[match.Value].ToString("R", CultureInfo.InvariantCulture) + ")", RegexOptions.IgnoreCase);
            var value = new Expression(text).Parse();
            if (value < 0) throw new FormatException("Размер не может быть отрицательным");
            cache[key] = value;
            return value;
        }
        finally { resolving.Remove(key); }
    }

    private sealed class Expression(string text)
    {
        private int _position;
        private int _depth;

        public double Parse()
        {
            if (text.Length > 8192) throw new FormatException("Слишком длинная формула");
            var value = AddSub();
            Skip();
            if (_position != text.Length) throw new FormatException("Неизвестное имя или лишний символ в формуле");
            if (!double.IsFinite(value)) throw new FormatException("Слишком большое значение формулы");
            return value;
        }

        private double AddSub()
        {
            var value = MulDiv();
            while (true)
            {
                Skip();
                if (Take('+')) value += MulDiv();
                else if (Take('-')) value -= MulDiv();
                else return value;
            }
        }

        private double MulDiv()
        {
            var value = Factor();
            while (true)
            {
                Skip();
                if (Take('*')) value *= Factor();
                else if (Take('/'))
                {
                    var divisor = Factor();
                    if (divisor == 0) throw new FormatException("Деление на ноль");
                    value /= divisor;
                }
                else return value;
            }
        }

        private double Factor()
        {
            if (++_depth > 100) throw new FormatException("Слишком много вложенных скобок");
            try
            {
                Skip();
                if (Take('+')) return Factor();
                if (Take('-')) return -Factor();
                var value = Primary();
                Skip();
                // Recurse through Factor so powers associate right and allow negative exponents.
                if (Take('^'))
                {
                    value = Math.Pow(value, Factor());
                    if (!double.IsFinite(value)) throw new FormatException("Недопустимое значение степени");
                }
                return value;
            }
            finally { _depth--; }
        }

        private double Primary()
        {
            if (text.AsSpan(_position).StartsWith("sqrt", StringComparison.OrdinalIgnoreCase))
            {
                _position += 4;
                Skip();
                if (!Take('(')) throw new FormatException("После sqrt ожидается открывающая скобка");
                var argument = AddSub();
                Skip();
                if (!Take(')')) throw new FormatException("Не закрыта скобка");
                if (argument < 0) throw new FormatException("Квадратный корень из отрицательного числа не определён");
                return Math.Sqrt(argument);
            }
            if (Take('('))
            {
                var value = AddSub();
                Skip();
                if (!Take(')')) throw new FormatException("Не закрыта скобка");
                return value;
            }
            var start = _position;
            while (_position < text.Length && (char.IsAsciiDigit(text[_position]) || text[_position] == '.')) _position++;
            if (_position < text.Length && text[_position] is 'e' or 'E')
            {
                _position++;
                if (_position < text.Length && text[_position] is '+' or '-') _position++;
                while (_position < text.Length && char.IsAsciiDigit(text[_position])) _position++;
            }
            if (start == _position || !double.TryParse(text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                throw new FormatException("Ожидается число или ссылка на размер");
            return number;
        }

        private bool Take(char value)
        {
            if (_position >= text.Length || text[_position] != value) return false;
            _position++;
            return true;
        }

        private void Skip() { while (_position < text.Length && char.IsWhiteSpace(text[_position])) _position++; }
    }
}
