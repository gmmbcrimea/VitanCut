using Microsoft.UI.Xaml;
using VitanCut.WinUI.Services;
using VitanCut.WinUI.Views;

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
            args.Handled = true;
            LogError(args.Exception);
            if (_mainWindow is RecoveryWindow recovery)
            {
                recovery.ShowError(args.Exception, Updates.CanRollback);
                return;
            }
            ShowRecovery(args.Exception);
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
        catch (Exception error)
        {
            LogError(error);
            ShowRecovery(error);
        }
        _mainWindow!.Activate();
    }

    private void ShowRecovery(Exception error)
    {
        var recovery = new RecoveryWindow(error, RetryLaunchAsync, RollbackAsync, Updates.CanRollback);
        _mainWindow = recovery;
        recovery.Activate();
    }

    private async Task RetryLaunchAsync(RecoveryWindow recovery)
    {
        try
        {
            State.Load();
            Cloud.Load();
            var main = new MainWindow();
            _mainWindow = main;
            main.Activate();
            recovery.Close();
        }
        catch (Exception error)
        {
            LogError(error);
            recovery.ShowError(error, Updates.CanRollback);
        }
        await Task.CompletedTask;
    }

    private Task<bool> RollbackAsync() => Updates.RollbackAsync();

    private static void LogError(Exception error)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VitanCut", "logs");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "errors.log"), $"{DateTimeOffset.Now:O}\n{error}\n");
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
    }
}
