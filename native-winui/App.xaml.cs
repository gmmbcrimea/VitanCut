using Microsoft.UI.Xaml;
using VitanCut.WinUI.Services;
using VitanCut.WinUI.Views;

namespace VitanCut.WinUI;

public partial class App : Application
{
    private Window? _mainWindow;
#if VITANCUT_UI_REVIEW
    public static AppPaths Paths { get; } = UiReviewPaths.Create(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("VITANCUT_DATA_ROOT"));
#else
    public static AppPaths Paths { get; } = AppPaths.ForCurrentProcess(AppContext.BaseDirectory);
#endif
    public static AppState State { get; } = new(Paths);
    internal static UiPerformanceLog UiPerformance { get; } = new(Paths.LogsDirectory,
        Paths.IsDevelopment && Environment.GetEnvironmentVariable("VITANCUT_UI_PROFILE") == "1");
    public static AppUpdateService Updates { get; } = new(Paths);
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

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupWindow? startup = null;
        var startupClosed = false;
        try
        {
            startup = new StartupWindow();
            startup.Closed += (_, _) => startupClosed = true;
            _mainWindow = startup;
            var startupDelay = startup.ShowAsync();
            // Let the splash render before reading local state on the UI thread.
            await Task.Delay(50);
            if (startupClosed || _mainWindow != startup) return;
            State.Load();
            Cloud.Load();
            await startupDelay;
            if (startupClosed || _mainWindow != startup) return;
            var main = new MainWindow();
            _mainWindow = main;
            main.Activate();
            startup.Close();
        }
        catch (Exception error)
        {
            LogError(error);
            ShowRecovery(error);
            if (!startupClosed) startup?.Close();
        }
    }

    private void ShowRecovery(Exception error)
    {
        var startup = _mainWindow as StartupWindow;
        var recovery = new RecoveryWindow(error, RetryLaunchAsync, RollbackAsync, Updates.CanRollback);
        _mainWindow = recovery;
        recovery.Activate();
        startup?.Close();
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
            var folder = State.Paths?.LogsDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VitanCut", "logs");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "errors.log"), $"{DateTimeOffset.Now:O}\n{error}\n");
            if (error is Microsoft.UI.Xaml.Markup.XamlParseException)
            {
                var details = string.Join("\n", error.Data.Keys.Cast<object>()
                    .Where(key => error.Data[key] is string)
                    .Select(key => $"{key}: {error.Data[key]}"));
                File.AppendAllText(Path.Combine(folder, "errors.log"), $"HRESULT: 0x{error.HResult:X8}\n{details}\n");
            }
        }
        catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
    }
}
