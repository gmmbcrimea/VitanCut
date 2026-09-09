using Microsoft.UI.Xaml;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI;

public partial class App : Application
{
    private Window? _mainWindow;
    public static AppState State { get; } = new();
    public static AppUpdateService Updates { get; } = new();
    public static CloudSyncService Cloud { get; } = new(State);
    public static PreferencesService Preferences { get; } = new(State);
    public static ProjectService Projects { get; } = new(State);
    public static CatalogService Catalog { get; } = new(State);
    public static MaterialService MaterialLibrary { get; } = new(State);

    public App()
    {
        UnhandledException += (_, args) =>
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VitanCut", "logs");
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, "errors.log"), $"{DateTimeOffset.Now:O}\n{args.Exception}\n");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        };
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            State.Load();
            Cloud.Load();
            _mainWindow = new MainWindow();
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            var text = new Microsoft.UI.Xaml.Controls.TextBlock
            {
                Text = error.Message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24)
            };
            _mainWindow = new Window { Title = "Не удалось открыть базу", Content = text };
            _mainWindow.AppWindow.Resize(new Windows.Graphics.SizeInt32(720, 300));
        }
        _mainWindow.Activate();
    }
}
