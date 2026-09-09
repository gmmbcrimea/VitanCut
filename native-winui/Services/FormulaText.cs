using System.Text.RegularExpressions;

namespace VitanCut.WinUI.Services;

public static class FormulaText
{
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    // Keep token boundaries: "600 30" must not silently become "60030".
    public static string NormalizeWhitespace(string text) => Whitespace.Replace(text, " ").Trim();
}
