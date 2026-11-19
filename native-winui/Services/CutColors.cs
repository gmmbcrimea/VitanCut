namespace VitanCut.WinUI.Services;

public static class CutColors
{
    public static string RemainderFill(string palette, string color) =>
        InterfaceOptions.Remainder(palette, color) == "red" ? "#F8DDDA" : "#DCEFD8";
    public static string RemainderStroke(string palette, string color) =>
        InterfaceOptions.Remainder(palette, color) == "red" ? "#A52D29" : "#4D7745";
    public static string CutLine(bool contrast) => contrast ? "#C62828" : "#596575";
    public static string Stroke(string palette) => palette switch
    {
        "green" => "#548438", "yellow" => "#A77800", "orange" => "#B9542D", "purple" => "#8552B0", _ => "#3679AB"
    };

    public static string Fill(string palette, int number)
    {
        var colors = palette switch
        {
            "green" => new[] { "#DFF2D8", "#C9E7BE" },
            "yellow" => new[] { "#FFF0B5", "#FFE29A" },
            "orange" => new[] { "#FFE0D0", "#FFC7A8" },
            "purple" => new[] { "#EAD9FF", "#D8BEFA" },
            _ => new[] { "#CDEFFF", "#DFF5FF" }
        };
        return colors[Math.Abs(number % colors.Length)];
    }
}
