using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace VitanCut.WinUI.Services;

internal static class CommandAppearance
{
    public static void Decorate(Button button)
    {
        if (button.Content is not string label) return;
        Symbol? symbol = label switch
        {
            "Сохранить" or "Сохранить раскрой" or "Сохранить деталировку" => Symbol.Save,
            "Печать" => Symbol.Print,
            "Создать деталировку" => Symbol.Document,
            "Создать карту кроя" => Symbol.Crop,
            "Добавить" or "Добавить изделие" or "Добавить материал" or "Создать проект" => Symbol.Add,
            "Добавить изображение" => Symbol.Pictures,
            "Импорт базы" => Symbol.Import,
            "Экспорт базы" => Symbol.Share,
            _ => null
        };
        if (symbol is null) return;
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(new SymbolIcon(symbol.Value) { Width = 20, Height = 20 });
        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        button.Content = content;
        AutomationProperties.SetName(button, label);
    }
}
