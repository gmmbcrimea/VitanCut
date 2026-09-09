namespace VitanCut.WinUI.Services;

public static class FormulaEditing
{
    public static int QueryStart(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var start = -1;
        for (var index = 0; index < caret; index++)
        {
            if (text[index] == '⟦')
            {
                var end = text.IndexOf('⟧', index + 1);
                if (end < 0 || end >= caret) return -1;
                index = end;
                start = -1;
            }
            else if (index + 1 < caret && text[index] == '[' && text[index + 1] == '[')
            {
                var end = text.IndexOf("]]", index + 2, StringComparison.Ordinal);
                if (end < 0 || end + 2 > caret) return -1;
                index = end + 1;
                start = -1;
            }
            else if ("=+-*/×÷−(".Contains(text[index])) start = index + 1;
        }
        return start;
    }
}
