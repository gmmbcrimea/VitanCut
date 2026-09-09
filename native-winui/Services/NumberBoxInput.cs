using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VitanCut.WinUI.Services;

/// <summary>Reads the live editor text before a command button commits focus.</summary>
public static class NumberBoxInput
{
    public static double Read(NumberBox box, double fallback = 0)
    {
        var text = FindTextBox(box)?.Text ?? box.Text;
        if (TryParse(text, out var value))
        {
            box.Value = value;
            return value;
        }
        return double.IsFinite(box.Value) ? box.Value : fallback;
    }

    private static bool TryParse(string? text, out double value)
    {
        var normalized = text?.Trim().Replace("\u00A0", " ") ?? "";
        return double.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value) && double.IsFinite(value);
    }

    private static TextBox? FindTextBox(DependencyObject node)
    {
        if (node is TextBox box) return box;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(node); index++)
        {
            var found = FindTextBox(VisualTreeHelper.GetChild(node, index));
            if (found is not null) return found;
        }
        return null;
    }
}
