using Microsoft.UI.Xaml;
using VitanCut.WinUI.Services;
using VitanCut.WinUI.Views;

namespace VitanCut.WinUI;

public partial class App : Application
{
    internal static bool RestartRequested { get; set; }
    private static bool _restartStarted;

    internal static void ExitOrRestart()
    {
        UiPerformance.Trace(UiLifecycle.AppExitRequested);
        if (RestartRequested && !_restartStarted)
        {
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = Environment.ProcessPath ?? throw new InvalidOperationException("Application path is unavailable."),
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            };
            start.ArgumentList.Add("--wait-for-process");
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            // Inherit the isolated data root for development previews as well.
            using var child = System.Diagnostics.Process.Start(start)
                ?? throw new InvalidOperationException("Application restart failed.");
            _restartStarted = true;
        }
        Current.Exit();
    }

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
        UiPerformance.Trace(UiLifecycle.AppStart);
        if (UiPerformance.Enabled)
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) => UiPerformance.Trace(UiLifecycle.ProcessExit);
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                UiPerformance.Trace(UiLifecycle.ManagedUnhandled, args.ExceptionObject as Exception);
        }
        UnhandledException += (_, args) =>
        {
            UiPerformance.Trace(UiLifecycle.UiUnhandled, args.Exception);
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
        var commandLine = Environment.GetCommandLineArgs();
        if (commandLine.Length == 2 && commandLine[1] is CloudHealthTask.CheckArgument or CloudHealthTask.RemoveArgument)
        {
            try
            {
                Environment.ExitCode = commandLine[1] == CloudHealthTask.CheckArgument
                    ? await CloudHealthProbe.RunAsync(Paths)
                    : await CloudHealthTask.ForCurrentUser(Paths).RemoveAsync();
            }
            catch (Exception error) { LogError(error); Environment.ExitCode = 1; }
            finally { Current.Exit(); }
            return;
        }
        StartupWindow? startup = null;
        var startupClosed = false;
        try
        {
            if (commandLine.Length == 3 && commandLine[1] == "--wait-for-process"
                && int.TryParse(commandLine[2], out var parentId) && parentId != Environment.ProcessId)
            {
                try
                {
                    using var parent = System.Diagnostics.Process.GetProcessById(parentId);
                    await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
                }
                catch (ArgumentException) { /* The previous instance has already exited. */ }
            }
            startup = new StartupWindow();
            startup.Closed += (_, _) => startupClosed = true;
            _mainWindow = startup;
            var startupDelay = startup.ShowAsync();
            // Let the splash render before reading local state on the UI thread.
            await Task.Delay(50);
            if (startupClosed || _mainWindow != startup) return;
            State.Load();
            UiText.Language = UiText.NormalizeLanguage(Preferences.Current.Language);
            startup.RefreshAppearance();
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
            UiText.Language = UiText.NormalizeLanguage(Preferences.Current.Language);
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
