namespace VitanCut.WinUI.Services;

internal readonly record struct FormulaSuggestionEdit(string Text, int Caret);

internal static class FormulaSuggestionInsertion
{
    public static FormulaSuggestionEdit Apply(string text, int queryStart, int caret, string displayExpression)
    {
        if (queryStart < 0 || queryStart > caret || caret > text.Length)
            throw new ArgumentOutOfRangeException(nameof(queryStart));
        var prefix = text[..queryStart].TrimEnd() + " ";
        return new FormulaSuggestionEdit(prefix + displayExpression + text[caret..], prefix.Length + displayExpression.Length);
    }
}
