using Microsoft.UI.Xaml;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Views;

public sealed partial class CatalogWindow : Window
{
    public CatalogWindow()
    {
        InitializeComponent();
        Title = "Vitan-Cut · Каталог";
        App.Preferences.Apply(this);
    }
}
