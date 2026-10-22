using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using VitanCut.WinUI.Services;
using Windows.Graphics;

namespace VitanCut.WinUI.Views;

public sealed partial class StartupWindow : Window
{
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private readonly string[] _statuses = ["Подготовка приложения…", "Подготовка каталогов и проектов…", "Подготовка рабочего пространства…"];
    private int _statusIndex;
    private readonly TimeSpan _duration;
    public bool IsClosed { get; private set; }

    public StartupWindow() : this(TimeSpan.FromMilliseconds(2500), 120,
        ["Подготовка приложения…", "Подготовка каталогов и проектов…", "Подготовка рабочего пространства…"])
    {
    }

    public StartupWindow(TimeSpan duration, double logoSize, string[] statuses, int height = 320)
    {
        _duration = duration;
        _statuses = statuses.Length > 0 ? statuses : ["Подготовка…"];
        InitializeComponent();
        Logo.Width = Logo.Height = logoSize;
        if (height < 320) Logo.Margin = new Thickness(0, 0, 0, 44);
        StatusText.Text = UiText.Get(_statuses[0]);
        _statusTimer.Interval = TimeSpan.FromMilliseconds(duration.TotalMilliseconds / _statuses.Length);
        App.Preferences.Apply(this);
        AppWindow.Resize(new SizeInt32(440, height));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }
        RemoveNativeFrame();
        AppWindow.Changed += (_, args) =>
        {
            if (!IsClosed && args.DidSizeChange) UpdateRoundedRegion();
        };
        Activated += (_, _) => { if (!IsClosed) RemoveNativeFrame(); };
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(area.X + (area.Width - AppWindow.Size.Width) / 2,
            area.Y + (area.Height - AppWindow.Size.Height) / 2));
        _statusTimer.Tick += (_, _) =>
        {
            if (_statusIndex + 1 < _statuses.Length) StatusText.Text = UiText.Get(_statuses[++_statusIndex]);
            else _statusTimer.Stop();
        };
        Closed += (_, _) => { IsClosed = true; _statusTimer.Stop(); };
    }

    public Task ShowAsync()
    {
        Activate();
        _statusTimer.Start();
        return Task.Delay(_duration);
    }

    public void RefreshAppearance()
    {
        App.Preferences.Apply(this);
        StatusText.Text = UiText.Get(_statuses[_statusIndex]);
    }
}
