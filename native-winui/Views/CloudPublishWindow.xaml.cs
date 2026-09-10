using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace VitanCut.WinUI.Views;

public sealed partial class CloudPublishWindow : Window
{
    public CloudPublishWindow()
    {
        InitializeComponent();
        App.Preferences.Apply(this);
        AppWindow.Resize(new SizeInt32(460, 180));
    }

    public void Complete(bool succeeded, string message)
    {
        Progress.IsIndeterminate = false;
        Progress.Value = 100;
        StatusIcon.Glyph = succeeded ? "&#xE73E;" : "&#xE783;";
        StatusIcon.Foreground = new SolidColorBrush(succeeded
            ? Color.FromArgb(255, 62, 207, 142)
            : Color.FromArgb(255, 242, 192, 108));
        TitleText.Text = succeeded ? "Данные сохранены в облаке" : "Данные сохранены на ПК";
        StatusText.Text = message;
    }
}
