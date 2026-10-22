using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Controllers;
using VitanCut.WinUI.Services;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;
using WinRT.Interop;

namespace VitanCut.WinUI;

public sealed partial class MainWindow : Window
{
    private const int MinimumWindowWidth = 980;
    private const int MinimumWindowHeight = 620;
    private const int WmGetMinMaxInfo = 0x0024;


    private readonly WindowSubclassProc _minSizeSubclassProc;
    private readonly ProjectPageController _projectPage;
    private readonly ProjectLayoutController _projectLayout;
    private readonly MaterialPageController _materialPage;
    private readonly CatalogPageController _catalogPage;
    private readonly NavigationController _navigation;
    private readonly SettingsPageController _settingsPage;
    private readonly DialogService _dialogs;
    private readonly ProductWindowCoordinator _productWindows;
    private readonly DatabaseTransferController _databaseTransfer;
    private readonly ListContextMenuController _contextMenus;
    private readonly ProjectCommandsController _projectCommands;
    private readonly CatalogCommandsController _catalogCommands;
    private readonly MaterialCommandsController _materialCommands;
    private readonly NestedScrollController _nestedScrolling;
    private readonly OwnedWindowActivator _childWindows;
    private readonly ReportWindowCoordinator _reports;
    private bool _loadingProjects;
    private bool _loadingMaterials = true;
    private bool _materialGroupActionChanging;
    private readonly List<MaterialChoice> _materialSelection = [];
    private readonly MaterialSelectionModel _materialSelectionModel = new();
    private readonly UiRequestGate _materialPreviewRequests = new();
    private readonly UiRequestGate _materialFocusRequests = new();
    // XAML assigns initial control values while the window is being constructed.
    // Ignore those events until persisted preferences are loaded explicitly.
    private bool _loadingSettings = true;
    private bool _firstLoad = true;
    private bool _showingProjectListOnNarrow = true;
    private Button? _activeMaterialSplitter;
    private double _materialSplitterStartX;
    private double _materialSplitterStartWidth;
    private Border? _updateCard;
    private SymbolIcon? _updateIcon;
    private TextBlock? _updateTitle;
    private TextBlock? _updateDescription;
    private TextBlock? _updateNotes;
    private Button? _installUpdateButton;
    private Button? _checkUpdatesButton;
    private ProgressBar? _updateProgress;
    private UpdateUiPreview? _updatePreview = null;
    private ComboBox? _cloudIntervalBox;
    private TextBlock? _cloudBadgeTitle;
    private Button? _cloudSyncNowButton;
    private int _cloudUiOperations;
    private Timer? _updateTimer;
    private Timer? _cloudPublishTimer;
    private Views.CloudPublishWindow? _activeCloudPublishWindow;
    private int _activeCloudPublishInterval;
    private bool _cloudPublishInProgress;
    private bool _shutdownInProgress;
    private bool _shutdownRequested;
    private Project? SelectedProject => ProjectsList.SelectedItem as Project;
    private string? SelectedCustomer => _catalogPage.SelectedCounterparty;
    private MaterialChoice? SelectedMaterial => _materialPage.SelectedMaterial;

    public MainWindow()
    {
        _minSizeSubclassProc = MinSizeSubclassProc;
        InitializeComponent();
        foreach (var splitter in new[] { MaterialCategoriesSplitter, MaterialListSplitter })
        {
            splitter.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(MaterialSplitterPressed), true);
            splitter.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(MaterialSplitterMoved), true);
            splitter.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(MaterialSplitterReleased), true);
            splitter.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(MaterialSplitterCaptureLost), true);
        }
        var notifications = new NotificationOverlay();
        Grid.SetRowSpan(notifications, 2);
        Grid.SetColumnSpan(notifications, 2);
        Root.Children.Add(notifications);
        _projectPage = new ProjectPageController(
            App.State, ProjectsList, NoProjectsPanel, EmptyStatePanel, ProjectPanel,
            ProjectTitleText, ProjectCustomerText, ProjectAddressText,
            MetricAreaText, MetricMaterialsText, MetricPayrollText, MetricTotalText,
            ProductsList, PayrollsList, NoProductsPanel, NoPayrollsPanel);
        _projectLayout = new ProjectLayoutController(
            ContentHost, ProjectsPage, ProjectListPane, ProjectDetailPane, BackToProjectsButton, MetricsGrid,
            MetricAreaCard, MetricMaterialsCard, MetricPayrollCard, MetricTotalCard);
        _materialPage = new MaterialPageController(
            App.MaterialLibrary, MaterialCategoriesList, MaterialsList, GroupedMaterialsList, MaterialListEmptyPanel,
            MaterialParametersEmptyPanel, MaterialParametersPanel, MaterialTypeBox, MaterialNameBox,
            MaterialUnitBox, MaterialCostBox, MaterialWasteAllowanceBox, MaterialPriceModeBox, MaterialRollLengthBox, MaterialSheetLengthBox, MaterialSheetWidthBox, MaterialTextureBox, MaterialEdgeBandingBox,
            MaterialSortBox);
        _catalogPage = new CatalogPageController(
            App.Catalog, CustomersList, CustomerProjectsList, CustomerProjectsTitle, CustomerProjectsSubtitle);
        _navigation = new NavigationController(
            ("projects", ProjectsPage, ProjectsTabButton, false),
            ("customers", CustomersPage, CustomersTabButton, false),
            ("materials", MaterialsPage, MaterialsTabButton, false),
            ("catalog", CatalogPage, CatalogTabButton, false),
            ("settings", SettingsPage, SettingsTabButton, true));
        _settingsPage = new SettingsPageController(
            ContentHost, ThemeBox, AccentBox, CompactModeSwitch, AnimationsSwitch, CutExportFormatBox, DetailingExportFormatBox, CutTrimBox, CutGapBox,
            AppearancePanel, CuttingPanel, DetailingPanel, DatabasePanel,
            ProjectsPage, CustomersPage, MaterialsPage, CatalogPage, SettingsPage);
        _dialogs = new DialogService(() => Root.XamlRoot, App.Catalog);
        _childWindows = new OwnedWindowActivator(this);
        _productWindows = new ProductWindowCoordinator(_childWindows, App.Projects, App.Catalog);
        _reports = new ReportWindowCoordinator(_childWindows, App.State, App.Projects);
        _databaseTransfer = new DatabaseTransferController(this, App.State);
        _contextMenus = new ListContextMenuController();
        _projectCommands = new ProjectCommandsController(App.State, App.Projects, _dialogs, _productWindows);
        _catalogCommands = new CatalogCommandsController(App.Catalog, _dialogs);
        _materialCommands = new MaterialCommandsController(App.MaterialLibrary, _dialogs);
        _nestedScrolling = new NestedScrollController(ProjectPanel);
        Title = "Vitan-Cut";
        CatalogTabButton.Visibility = Visibility.Collapsed;
        ConfigureTopNavigation();
        ConfigureCloudSurface();
        ConfigureUpdateSurface();
        ConfigureTitleBar();
        AppWindow.Closing += MainWindowClosing;
        Closed += (_, _) => { App.UiPerformance.Flush(); App.ExitOrRestart(); };
        App.Preferences.Apply(this);
        ApplyTitleBarForeground();
        Root.SizeChanged += (_, _) => UpdateResponsiveLayout();
        Root.Loaded += async (_, _) =>
        {
            UpdateResponsiveLayout();
            await InitializeOnlineServicesAsync();
            StartBackgroundTimers();
        };
        MaterialUnitBox.SelectionChanged += MaterialUnitChanged;
        ConfigureNestedScrolling();
        ConfigureContextMenuSelection();
        LoadSettings();
        RefreshCustomers();
        RefreshProjects(select: null, allowAutoSelect: false);
        RefreshMaterials();
        ShowTab("projects");
    }

    private void ConfigureTopNavigation()
    {
        ConfigureTopTab(ProjectsTabButton, Symbol.Folder, "Проекты");
        ConfigureTopTab(CustomersTabButton, Symbol.AllApps, "Каталог");
        ConfigureTopTab(MaterialsTabButton, Symbol.Library, "Материалы");
        ConfigureTopTab(SettingsTabButton, Symbol.Setting, "Настройки");
    }

    private static void ConfigureTopTab(Button button, Symbol symbol, string label)
    {
        var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new SymbolIcon { Symbol = symbol });
        content.Children.Add(new TextBlock { Text = UiText.Get(label), Style = (Style)Application.Current.Resources["CommandLabelStyle"], FontSize = 12,
            MaxWidth = 76, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        button.Content = content;
        ToolTipService.SetToolTip(button, UiText.Get(label));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, UiText.Get(label));
    }

    private void ConfigureCloudSurface()
    {
        if (CloudIconSurface is { } iconSurface)
        {
            iconSurface.Width = 180;
            iconSurface.Height = 36;
            iconSurface.CornerRadius = new CornerRadius(0);
            iconSurface.Background = new SolidColorBrush(Colors.Transparent);
            iconSurface.Child = new Image
            {
                Source = new BitmapImage(new Uri("ms-appx:///Assets/supabase-wordmark.png")),
                Width = 180,
                Height = 36
            };
            CloudHeaderText.Visibility = Visibility.Collapsed;
        }
    }

    private void ConfigureUpdateSurface()
    {
        _updateIcon = new SymbolIcon { Symbol = Symbol.Sync, Foreground = new SolidColorBrush(Color.FromArgb(255, 138, 153, 168)) };
        _updateTitle = new TextBlock { Text = UiText.Get("Обновления"), Style = (Style)Application.Current.Resources["CardTitleTextBlockStyle"] };
        _updateDescription = new TextBlock { Text = UiText.Get("Проверка обновлений выполняется каждые 30 минут."), Style = (Style)Application.Current.Resources["MutedTextBlockStyle"], TextWrapping = TextWrapping.Wrap };
        _updateNotes = new TextBlock { Style = (Style)Application.Current.Resources["MutedTextBlockStyle"], TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        _installUpdateButton = new Button
        {
            Content = UiText.Get("Обновить"),
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromArgb(255, 196, 61, 61)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        _installUpdateButton.Click += InstallUpdateClick;
        _checkUpdatesButton = new Button { Content = UiText.Get("Проверить сейчас") };
        _checkUpdatesButton.Click += CheckUpdatesClick;
        _updateProgress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 6,
            MinHeight = 6,
            Foreground = (Brush)Application.Current.Resources["AppAccentBrush"],
            Visibility = Visibility.Collapsed
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_updateProgress,UiText.Get("Скачивание обновления"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(_updateDescription, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);

        var header = new Grid { ColumnSpacing = 14 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(_updateIcon);
        var text = new StackPanel { Spacing = 4 };
        text.Children.Add(_updateTitle);
        text.Children.Add(_updateDescription);
        text.Children.Add(_updateNotes);
        Grid.SetColumn(text, 1);
        header.Children.Add(text);

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(header);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(_checkUpdatesButton);
        actions.Children.Add(_installUpdateButton);
        panel.Children.Add(actions);
        panel.Children.Add(_updateProgress);
        _updateCard = new Border
        {
            Style = (Style)Application.Current.Resources["SettingsSectionStyle"],
            Padding = new Thickness(22),
            Child = panel
        };
        // Keep maintenance tools together in the right settings column, before the cloud connection card.
        DatabasePanel.Children.Insert(1, _updateCard);
#if VITANCUT_UI_REVIEW
        _updatePreview = UpdateUiPreview.Create(App.Paths.IsDevelopment,
            Environment.GetEnvironmentVariable("VITANCUT_UI_UPDATE_PREVIEW"),
            typeof(App).Assembly.GetName().Version ?? new Version(1, 0, 0));
        if (_updatePreview is not null) ApplyUpdatePreview(_updatePreview);
#endif
    }

    private void RefreshUpdateSurface(AppUpdateInfo update)
    {
        if (_updateCard is null || _updateTitle is null || _updateDescription is null || _updateNotes is null || _updateIcon is null || _installUpdateButton is null || _checkUpdatesButton is null || _updateProgress is null) return;
        _checkUpdatesButton.IsEnabled = true;
        _installUpdateButton.IsEnabled = update.CanInstall && update.LatestVersion is not null;
        _checkUpdatesButton.Content = UiText.Get("Проверить сейчас");
        _updateProgress.Visibility = Visibility.Collapsed;
        _updateDescription.Text = update.Message;
        _updateNotes.Text = string.IsNullOrWhiteSpace(update.ReleaseNotes) ? UiText.Get("Что нового: описание пока не добавлено.") : UiText.Get("Что нового: ") + update.ReleaseNotes;
        _updateNotes.Visibility = update.Availability == UpdateAvailability.Available ? Visibility.Visible : Visibility.Collapsed;
        switch (update.Availability)
        {
            case UpdateAvailability.UpToDate:
                _updateIcon.Symbol = Symbol.Accept;
                _updateIcon.Foreground = (Brush)Application.Current.Resources["UiSuccessBrush"];
                _updateTitle.Text = UiText.Get("Приложение обновлено");
                _updateCard.BorderBrush = (Brush)Application.Current.Resources["UiSuccessBrush"];
                _installUpdateButton.Visibility = Visibility.Collapsed;
                break;
            case UpdateAvailability.Available:
                _updateIcon.Symbol = Symbol.Important;
                _updateIcon.Foreground = (Brush)Application.Current.Resources["UiDangerBrush"];
                _updateTitle.Text = UiText.Get("Доступно обновление");
                _updateCard.BorderBrush = (Brush)Application.Current.Resources["UiDangerBrush"];
                _installUpdateButton.Content = update.LatestVersion is { } latest ? UiText.Format($"Обновить до {AppUpdateService.FormatVersion(latest)}") : UiText.Get("Обновить");
                _installUpdateButton.Visibility = Visibility.Visible;
                break;
            default:
                _updateIcon.Symbol = Symbol.Important;
                _updateIcon.Foreground = (Brush)Application.Current.Resources["UiWarningTextBrush"];
                _updateTitle.Text = UiText.Get("Не удалось проверить обновления");
                _updateCard.BorderBrush = (Brush)Application.Current.Resources["UiWarningTextBrush"];
                _installUpdateButton.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void ApplyUpdatePreview(UpdateUiPreview preview)
    {
        RefreshUpdateSurface(preview.Info);
        _checkUpdatesButton!.IsEnabled = false;
        _installUpdateButton!.IsEnabled = false;
        if (preview.Checking)
        {
            _updateTitle!.Text = UiText.Get("Проверка обновлений");
            _updateIcon!.Symbol = Symbol.Sync;
            _updateIcon.Foreground = (Brush)Application.Current.Resources["AppTextBrush"];
            _updateCard!.BorderBrush = (Brush)Application.Current.Resources["PanelStrokeBrush"];
            _checkUpdatesButton.Content = UiText.Get("Проверяем...");
            _updateProgress!.Visibility = Visibility.Visible;
            _updateProgress.IsIndeterminate = true;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_updateProgress,UiText.Get("Проверка обновлений"));
        }
        if (preview.Downloading)
        {
            if (_updateProgress!.IsLoaded) ShowUpdateDownloadProgress(preview.Percent);
            else
            {
                _updateProgress.Visibility = Visibility.Visible;
                _updateProgress.Loaded += UpdatePreviewProgressLoaded;
            }
            _installUpdateButton.Content = UiText.Get("Скачивание обновления...");
            if (preview.Restarting) ShowUpdateRestartPreview();
        }
    }

    private void UpdatePreviewProgressLoaded(object sender, RoutedEventArgs e)
    {
        _updateProgress!.Loaded -= UpdatePreviewProgressLoaded;
        if (_updatePreview is not { Downloading: true } preview) return;
        ShowUpdateDownloadProgress(preview.Percent);
        if (preview.Restarting) ShowUpdateRestartPreview();
    }

    private void ShowUpdateRestartPreview()
    {
        _updateDescription!.Text = UiText.Get("Файлы подготовлены. Перезапуск приложения...");
        _installUpdateButton!.Content = UiText.Get("Перезапуск...");
    }

    private void ShowUpdateDownloadProgress(double? percent)
    {
        if (_updateProgress is null || _updateDescription is null) return;
        _updateProgress.Visibility = Visibility.Visible;
        _updateProgress.IsIndeterminate = percent is null;
        if (percent is { } value)
        {
            _updateProgress.Value = Math.Clamp(value, 0, 100);
            _updateDescription.Text = UiText.Format($"Скачивание обновления: {_updateProgress.Value:0}%");
        }
        else _updateDescription.Text = UiText.Get("Скачивание обновления...");
    }

    private async Task InitializeOnlineServicesAsync()
    {
        if (!App.Cloud.IsEnabled)
        {
            RefreshCloudSettings();
            return;
        }
        await RestoreCloudSessionAfterLaunchAsync();
    }

    private void StartBackgroundTimers()
    {
        _updateTimer ??= new Timer(_ => QueueBackgroundOperation(CheckForUpdatesAsync, "Не удалось проверить обновления"));
        _updateTimer.Change(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(30));
        ConfigureCloudPublishTimer();
        Closed += (_, _) =>
        {
            _updateTimer?.Dispose();
            _cloudPublishTimer?.Dispose();
        };
    }

    private void MainWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shutdownRequested) return;
        if (_shutdownInProgress)
        {
            args.Cancel = true;
            return;
        }

        var cloudOperationInProgress = App.Cloud.IsOperationInProgress;
        _shutdownInProgress = true;
        StopBackgroundTimers();
        if (!App.Cloud.HasUnsyncedLocalChanges && !cloudOperationInProgress)
        {
            _shutdownRequested = true;
            return;
        }

        args.Cancel = true;
        _ = PublishBeforeExitAsync();
    }

    private void StopBackgroundTimers()
    {
        _updateTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _cloudPublishTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    private void QueueBackgroundOperation(Func<Task> operation, string failureTitle)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (_shutdownRequested || _shutdownInProgress) return;
            try { await operation(); }
            catch (Exception error)
            {
                if (_shutdownRequested || _shutdownInProgress) return;
                NotificationCenter.Publish(failureTitle,error.Message);
            }
        });
    }

    private async Task PublishBeforeExitAsync()
    {
        var progressWindow = _activeCloudPublishWindow ?? new Views.CloudPublishWindow();
        _activeCloudPublishWindow = progressWindow;
        progressWindow.Activate();
        AppWindow.Hide();

        CloudSyncResult result;
        try { result = await App.Cloud.PublishAsync(); }
        catch (Exception) { result = new CloudSyncResult(false, "Нет соединения с Supabase. Изменения останутся на этом компьютере и будут отправлены при следующем запуске."); }

        var message = result.Succeeded
            ? "Изменённые данные опубликованы. Приложение можно безопасно закрыть."
            : "Изменения сохранены на этом компьютере и будут отправлены при следующем запуске. " + result.Message;
        progressWindow.Complete(result.Succeeded, message);
        await Task.Delay(result.Succeeded ? 900 : 3200);
        _shutdownRequested = true;
        progressWindow.Close();
        _activeCloudPublishWindow = null;
        App.ExitOrRestart();
    }

    private async Task CheckForUpdatesAsync()
    {
        if (_shutdownRequested || _shutdownInProgress || _updatePreview is not null) return;
        var update = await App.Updates.CheckAsync();
        if (_shutdownRequested || _shutdownInProgress) return;
        RefreshUpdateSurface(update);
        if (update.Availability != UpdateAvailability.Available) return;

        NotificationCenter.Publish(UiText.Get("Доступно обновление"),update.Message);
    }

    private async void CheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (_checkUpdatesButton is null || _updatePreview is not null) return;
        _checkUpdatesButton.IsEnabled = false;
        _checkUpdatesButton.Content = UiText.Get("Проверяем...");
        try { await CheckForUpdatesAsync(); }
        finally
        {
            if (!_shutdownRequested && !_shutdownInProgress)
            {
                _checkUpdatesButton.IsEnabled = true;
                _checkUpdatesButton.Content = UiText.Get("Проверить сейчас");
            }
        }
    }

    private void ConfigureCloudPublishTimer()
    {
        if (!App.Cloud.IsSignedIn)
        {
            _cloudPublishTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _activeCloudPublishInterval = 0;
            return;
        }

        var minutes = App.Preferences.Current.CloudPublishIntervalMinutes;
        if (_cloudPublishTimer is not null && _activeCloudPublishInterval == minutes) return;
        _cloudPublishTimer ??= new Timer(_ => QueueBackgroundOperation(PublishCloudChangesAutomaticallyAsync, "Не удалось выполнить облачную синхронизацию"));
        _cloudPublishTimer.Change(TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes));
        _activeCloudPublishInterval = minutes;
    }

    private async Task PublishCloudChangesAutomaticallyAsync()
    {
        if (_shutdownRequested || _shutdownInProgress || _cloudPublishInProgress || !App.Cloud.IsSignedIn) return;
        _cloudPublishInProgress = true;
        try
        {
            var result = await RunCloudOperationAsync(() => App.Cloud.PublishAsync());
            if (result.Succeeded)
            {
                NotificationCenter.Publish(UiText.Get("Облачная синхронизация"),UiText.Get("Изменения успешно опубликованы в Supabase."));
                RefreshCloudSettings();
                if (result.DatabaseChanged) RefreshAfterCloudDatabaseLoad();
                return;
            }
            result = App.Cloud.RequireSignInAfterSyncFailure(result);
            RefreshCloudSettings();
            NotificationCenter.Publish(UiText.Get("Не удалось опубликовать изменения"),result.Message);
        }
        finally
        {
            _cloudPublishInProgress = false;
        }
    }

    private async void InstallUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_updatePreview is not null) return;
        var update = App.Updates.LastCheck;
        if (update is null || !update.CanInstall) return;
        if (_installUpdateButton is not null)
        {
            _installUpdateButton.IsEnabled = false;
            _installUpdateButton.Content = UiText.Get("Скачивание обновления...");
        }

        if (_updateProgress is not null)
        {
            _updateProgress.Value = 0;
            _updateProgress.IsIndeterminate = true;
            _updateProgress.Visibility = Visibility.Visible;
        }

        var progress = new Progress<UpdateDownloadProgress>(status => ShowUpdateDownloadProgress(status.Percent));

        if (await App.Updates.InstallAsync(update, progress))
        {
            if (_updateProgress is not null)
            {
                _updateProgress.IsIndeterminate = false;
                _updateProgress.Value = 100;
            }
            if (_updateDescription is not null) _updateDescription.Text = UiText.Get("Файлы подготовлены. Перезапуск приложения...");
            NotificationCenter.Publish(UiText.Get("Обновление готово"),UiText.Get("Приложение перезапустится после замены файлов."));
            await Task.Delay(700);
            Application.Current.Exit();
            return;
        }

        if (_installUpdateButton is not null)
        {
            _installUpdateButton.IsEnabled = true;
            _installUpdateButton.Content = UiText.Get("Повторить обновление");
        }
        if (_updateProgress is not null) _updateProgress.Visibility = Visibility.Collapsed;
        NotificationCenter.Publish(UiText.Get("Не удалось установить обновление"),UiText.Get("Проверьте подключение к сети и права на изменение папки приложения."));
    }

    private void ConfigureTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleDragRegion);
        AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(24, 0, 0, 0);
        AppWindow.TitleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(40, 0, 0, 0);
        ConfigureMinimumWindowSize();
    }

    private void ApplyTitleBarForeground()
    {
        var dark = App.Preferences.Current.Theme == "dark" ||
            (App.Preferences.Current.Theme == "auto" && new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background).R < 80);
        var color = dark ? Colors.White : Colors.Black;
        AppWindow.TitleBar.ButtonForegroundColor = color;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = color;
    }
    private void ConfigureMinimumWindowSize()
    {
        var size = AppWindow.Size;
        if (size.Width < MinimumWindowWidth || size.Height < MinimumWindowHeight)
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(
                Math.Max(size.Width, MinimumWindowWidth),
                Math.Max(size.Height, MinimumWindowHeight)));
        }

        SetWindowSubclass(WindowNative.GetWindowHandle(this), _minSizeSubclassProc, new UIntPtr(1), UIntPtr.Zero);
    }

    private void TopTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tab }) ShowTab(tab);
    }

    private void ShowTab(string tab)
    {
        var returningToProject = tab == "projects" && ProjectsPage.Visibility != Visibility.Visible;
        _navigation.Show(tab, App.Preferences.Current.AnimationsEnabled);
        if (tab != "projects") _priceCheckedProjectId = null;
        else if (returningToProject && SelectedProject is { } project)
        {
            _priceCheckedProjectId = project.Id;
            QueueProjectPriceCheck(project.Id);
        }
        if (tab == "customers") RefreshCustomers();
        if (tab == "materials") RefreshMaterials(SelectedMaterial);
        if (tab == "settings") LoadSettings();
        UpdateResponsiveLayout();
        DispatcherQueue.TryEnqueue(() => ThemeService.ApplyLayout(Root, App.Preferences.Current));
    }

    private void ConfigureNestedScrolling() =>
        _nestedScrolling.Attach(PayrollsList);

    private void ConfigureContextMenuSelection() =>
        _contextMenus.Configure(BuildContextMenu, ProjectsList, ProductsList, PayrollsList, CustomersList, CustomerProjectsList, MaterialsList);

    private void ListViewRightTappedSelectItem(object sender, RightTappedRoutedEventArgs e) =>
        _contextMenus.Show(sender, e, BuildContextMenu);

    private MenuFlyout BuildContextMenu(ListView list, object? data)
    {
        var menu = new MenuFlyout();
        var menuData = data ?? list.SelectedItem;
        void Add(string text, Action action)
        {
            var item = new MenuFlyoutItem { Text = UiText.Get(text) };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        if (list == CustomerProjectsList)
        {
            Add("Добавить", () => AddCatalogProductClick(list, new RoutedEventArgs()));
            if (menuData is CustomerProductItem item)
            {
                Add("Изменить", () => OpenCatalogProductWindow(item.Counterparty, item.Product));
                Add("Копировать другому заказчику", async () => await CopyCatalogProductToCounterpartyAsync(item));
                Add("Удалить", async () =>
                {
                    if (!await _dialogs.ConfirmDeleteAsync(UiText.Get("Удалить из каталога?"),item.Product.Name + UiText.Get(". Изделия в проектах сохранятся."))) return;
                    try { App.Catalog.DeleteProduct(item.Counterparty, item.Product); _catalogPage.RenderProducts(); }
                    catch (Exception error) { await _dialogs.ShowMessageAsync(UiText.Get("Не удалось удалить"),error.Message); }
                });
            }
            return menu;
        }

        if (list == ProjectsList)
        {
            Add("Добавить", () => AddProjectClick(list, new RoutedEventArgs()));
            if (menuData is Project project)
            {
                Add("Изменить", async () => await EditProjectAsync(project));
                Add("Удалить", async () => await DeleteProjectAsync(project));
            }
            return menu;
        }

        if (list == ProductsList)
        {
            Add("Добавить", () => AddProductClick(list, new RoutedEventArgs()));
            if (menuData is Product product && SelectedProject is { } project)
            {
                Add("Изменить", () => OpenProductWindow(project, product));
                Add("Удалить", async () => await DeleteProductAsync(project, product));
            }
            return menu;
        }

        if (list == PayrollsList)
        {
            Add("Добавить", () => AddPayrollClick(list, new RoutedEventArgs()));
            var index = PayrollsList.SelectedIndex;
            if (SelectedProject is not null && index >= 0)
            {
                Add("Изменить", async () => await EditPayrollAsync(index));
                Add("Удалить", async () => await DeletePayrollAsync(index));
            }
            return menu;
        }

        if (list == CustomersList)
        {
            Add("Добавить", () => AddCustomerClick(list, new RoutedEventArgs()));
            if (menuData is string customer)
            {
                Add("Изменить", async () => await EditCustomerAsync(customer));
                Add("Удалить", async () => await DeleteCustomerAsync(customer));
            }
            return menu;
        }

        if (menuData is MaterialChoice choice)
        {
            if (_materialSelection.Count > 1)
                Add("Объединить в группу...", () => MaterialGroupActionBox.SelectedIndex = 1);
            if (_materialSelection.Any(selected => !string.IsNullOrWhiteSpace(selected.Material.Group)))
                Add("Убрать из группы", () => MaterialGroupActionBox.SelectedIndex = 2);
            Add("Изменить", () =>
            {
                _materialPage.SelectMaterial(choice);
                _ = UpdateMaterialTexturePreviewAsync();
                MaterialNameBox.Focus(FocusState.Programmatic);
            });
            Add("Удалить", async () => await DeleteMaterialAsync(choice));
        }

        return menu;
    }

    private void RefreshCustomers() => _catalogPage.Refresh();

    private void RefreshCustomerProjects() => _catalogPage.RenderProducts();
    private async void AddCustomerClick(object sender, RoutedEventArgs e)
    {
        var name = await _catalogCommands.AddCounterpartyAsync();
        if (name is null) return;
        RefreshCustomers();
        _catalogPage.SelectCounterparty(name);
    }

    private async void EditCustomerClick(object sender, RoutedEventArgs e)
    {
        if (SelectedCustomer is { } oldName) await EditCustomerAsync(oldName);
    }

    private async Task EditCustomerAsync(string oldName)
    {
        var newName = await _catalogCommands.RenameCounterpartyAsync(oldName);
        if (newName is null) return;
        RefreshProjects(SelectedProject, allowAutoSelect: false);
        RefreshCustomers();
        _catalogPage.SelectCounterparty(newName);
    }

    private async void DeleteCustomerClick(object sender, RoutedEventArgs e)
    {
        if (SelectedCustomer is { } customer) await DeleteCustomerAsync(customer);
    }

    private async Task DeleteCustomerAsync(string customer)
    {
        if (!await _catalogCommands.DeleteCounterpartyAsync(customer)) return;
        RefreshProjects(SelectedProject, allowAutoSelect: false);
        RefreshCustomers();
        NotificationCenter.Publish(UiText.Get("Заказчик удалён"),UiText.Format($"Заказчик «{customer}» удалён из каталога."));
    }

    private void CustomersSelectionChanged(object sender, SelectionChangedEventArgs e) => _catalogPage.RenderProducts();

    private void CustomerProjectsDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        OpenSelectedCustomerProject();
    }

    private void OpenCustomerProjectClick(object sender, RoutedEventArgs e)
    {
        OpenSelectedCustomerProject();
    }

    private void OpenSelectedCustomerProject()
    {
        if (CustomerProjectsList.SelectedItem is not CustomerProductItem item) return;
        OpenCatalogProductWindow(item.Counterparty, item.Product);
    }

    private void SelectProjectAndShow(Project project)
    {
        _showingProjectListOnNarrow = false;
        RefreshProjects(project, allowAutoSelect: false);
        ShowTab("projects");
    }

    private void RefreshProjects(Project? select = null, bool allowAutoSelect = true)
    {
        var selected = select ?? SelectedProject;
        var projects = App.Projects.GetOrdered(SortKey()).ToList();
        _projectPage.BindProjectList(projects, selected, allowAutoSelect && !_firstLoad);
        _firstLoad = false;
        FillProjectCard();
    }
    private string SortKey()
    {
        return SortBox.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() ?? "updated" : "updated";
    }

    private void FillProjectCard()
    {
        _loadingProjects = true;
        _projectPage.Render(SelectedProject, PayrollLabel);
        _loadingProjects = false;
        UpdateResponsiveLayout();
    }
    private void AddProjectClick(object sender, RoutedEventArgs e)
    {
        _projectCommands.OpenCreateProject(project =>
        {
            RefreshCustomers();
            RefreshProjects(project);
            ShowTab("projects");
            NotificationCenter.Publish(UiText.Get("Проект создан"),UiText.Format($"Проект «{project.Name}» создан."));
        });
    }

    private async void EditProjectClick(object sender, RoutedEventArgs e)
    {
        var project = ProjectFromCommand(sender);
        if (project is not null) await EditProjectAsync(project);
    }

    private async Task EditProjectAsync(Project project)
    {
        if (!await _projectCommands.EditProjectAsync(project)) return;
        RefreshCustomers();
        RefreshProjects(project, allowAutoSelect: false);
        RefreshCustomerProjects();
    }

    private async void DeleteProjectClick(object sender, RoutedEventArgs e)
    {
        var project = ProjectFromCommand(sender);
        if (project is not null) await DeleteProjectAsync(project);
    }

    private async Task DeleteProjectAsync(Project project)
    {
        if (!await _projectCommands.DeleteProjectAsync(project)) return;
        RefreshProjects(select: null, allowAutoSelect: false);
        RefreshCustomerProjects();
        NotificationCenter.Publish(UiText.Get("Проект удалён"),UiText.Format($"Проект «{project.Name}» удалён."));
    }

    private Project? ProjectFromCommand(object sender) =>
        _contextMenus.CurrentList == CustomerProjectsList
            ? CustomerProjectsList.SelectedItem as Project
            : ProjectsList.SelectedItem as Project;

    private void AddProductClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is { } project) OpenProductWindow(project, null);
    }

    private void ProductRowMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not Product product || SelectedProject is not { } project) return;

        var menu = new MenuFlyout();
        var edit = new MenuFlyoutItem { Text = UiText.Get("Изменить") };
        edit.Click += (_, _) => OpenProductWindow(project, product);
        var remove = new MenuFlyoutItem { Text = UiText.Get("Удалить") };
        remove.Click += async (_, _) => await DeleteProductAsync(project, product);
        menu.Items.Add(edit);
        menu.Items.Add(remove);
        menu.ShowAt(element);
    }
    private void EditProductClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is { } project && ProductsList.SelectedItem is Product product)
            OpenProductWindow(project, product);
    }

    private async void DeleteProductClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is { } project && ProductsList.SelectedItem is Product product)
            await DeleteProductAsync(project, product);
    }

    private async Task DeleteProductAsync(Project project, Product product)
    {
        if (await _projectCommands.DeleteProductAsync(project, product))
        {
            FillProjectCard();
            NotificationCenter.Publish(UiText.Get("Изделие удалено"),UiText.Format($"Изделие «{product.Name}» удалено из проекта."));
        }
    }

    private void AddPayrollClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is not { } project) return;
        _projectCommands.AddPayroll(project);
        FillProjectCard();
        NotificationCenter.Publish(UiText.Get("Зарплата добавлена"),UiText.Get("В проект добавлена новая строка ЗП."));
    }

    private async void EditPayrollClick(object sender, RoutedEventArgs e) =>
        await EditPayrollAsync(PayrollsList.SelectedIndex);

    private async Task EditPayrollAsync(int index)
    {
        if (SelectedProject is { } project && await _projectCommands.EditPayrollAsync(project, index)) FillProjectCard();
    }

    private async void DeletePayrollClick(object sender, RoutedEventArgs e) =>
        await DeletePayrollAsync(PayrollsList.SelectedIndex);

    private async Task DeletePayrollAsync(int index)
    {
        if (SelectedProject is { } project && index >= 0 && index < project.Payrolls.Count && await _projectCommands.DeletePayrollAsync(project, index))
        {
            FillProjectCard();
            NotificationCenter.Publish(UiText.Get("Строка ЗП удалена"),UiText.Get("Строка зарплаты удалена из проекта."));
        }
    }

    private void ProjectsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingProjects) return;
        if (SelectedProject is not null && _projectLayout.IsNarrow)
        {
            _showingProjectListOnNarrow = false;
        }
        FillProjectCard();
        if (SelectedProject is { } project && _priceCheckedProjectId != project.Id)
        {
            _priceCheckedProjectId = project.Id;
            QueueProjectPriceCheck(project.Id);
        }
    }

    private string? _priceCheckedProjectId;
    private string? _pendingPriceCheck;
    private bool _priceCheckDialogOpen;

    private void QueueProjectPriceCheck(string projectId)
    {
        _pendingPriceCheck = projectId;
        DispatcherQueue.TryEnqueue(async () =>
        {
            if (_priceCheckDialogOpen || Root.XamlRoot is null || ProjectsPage.Visibility != Visibility.Visible || SelectedProject?.Id != _pendingPriceCheck) return;
            var id = _pendingPriceCheck;
            _pendingPriceCheck = null;
            var project = id is null ? null : App.Projects.Find(id);
            if (project is null) return;
            var changes = ProjectDetailPricing.CatalogChanges(App.State, project);
            if (changes.Count == 0) return;
            _priceCheckDialogOpen = true;
            try
            {
                var details = new StackPanel { Spacing = 8, MaxWidth = 460 };
                details.Children.Add(new TextBlock
                {
                    Text = UiText.Get("Цены в каталоге изменились. Обновить цены проекта? Ручные цены останутся без изменений."),
                    TextWrapping = TextWrapping.Wrap
                });
                string PriceLabel(string mode, double price, double quantity, string unit, double allowance)
                {
                    var label = UiText.Get(mode == "sheet" ? "Цена за лист" : mode == "roll" ? "Цена за бухту" : "Цена за единицу");
                    return $"{label}: {Calculator.Number(price * quantity)} {UiText.Get("руб.")}" +
                        (mode == "unit" ? $" / {MaterialSummaryService.UnitLabel(unit)}" :
                            $" ({Calculator.Number(quantity, 3)} {MaterialSummaryService.UnitLabel(unit)})") +
                        (unit is "m2" or "lm" ? $" · {UiText.Get("Допуск на обрезки, %")}: {Calculator.Number(allowance)}" : "");
                }
                foreach (var change in changes.DistinctBy(change => (change.MaterialId, change.PreviousPrice, change.CurrentPrice,
                    change.PreviousMode, change.CurrentMode, change.PreviousQuantity, change.CurrentQuantity, change.PreviousAllowance, change.CurrentAllowance)))
                    details.Children.Add(new TextBlock
                    {
                        Text = $"{change.Name}\n{PriceLabel(change.PreviousMode, change.PreviousPrice, change.PreviousQuantity, change.Unit, change.PreviousAllowance)} → {PriceLabel(change.CurrentMode, change.CurrentPrice, change.CurrentQuantity, change.Unit, change.CurrentAllowance)}",
                        TextWrapping = TextWrapping.Wrap
                    });
                var dialog = new ContentDialog
                {
                    XamlRoot = Root.XamlRoot, RequestedTheme = Root.ActualTheme, Title = UiText.Get("Обновить цены проекта?"),
                    Content = new ScrollViewer { Content = details, MaxHeight = Math.Max(160, Root.ActualHeight - 260),
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
                    PrimaryButtonText = UiText.Get("Обновить цены"), CloseButtonText = UiText.Get("Оставить цены проекта"),
                    DefaultButton = ContentDialogButton.Close, MinWidth = Math.Min(540, Root.ActualWidth - 48)
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary && SelectedProject?.Id == id)
                {
                    App.Projects.UpdateCatalogPrices(project, changes);
                    FillProjectCard();
                    NotificationCenter.Publish(UiText.Get("Готово"), UiText.Get("Цены проекта обновлены"));
                }
            }
            catch (Exception error) { NotificationCenter.Publish(UiText.Get("Обновить цены проекта?"), error.Message); }
            finally
            {
                _priceCheckDialogOpen = false;
                if (_pendingPriceCheck is { } pending) QueueProjectPriceCheck(pending);
            }
        });
    }

    private void BackToProjectsClick(object sender, RoutedEventArgs e)
    {
        _priceCheckedProjectId = null;
        _showingProjectListOnNarrow = true;
        ProjectsList.SelectedIndex = -1;
        FillProjectCard();
        UpdateResponsiveLayout();
    }

    private void ProductsListDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (SelectedProject is not { } project || ProductsList.SelectedItem is not Product product) return;
        OpenProductWindow(project, product);
    }

    private void AddCatalogProductClick(object sender, RoutedEventArgs e)
    {
        if (SelectedCustomer is not { } counterparty) return;
        OpenCatalogProductWindow(counterparty, null);
    }

    private void OpenCatalogProductWindow(string counterparty, Product? product) =>
        _productWindows.OpenCatalogProduct(counterparty, product, _catalogPage.RenderProducts);

    private void OpenProductWindow(Project project, Product? product) =>
        _productWindows.OpenProjectProduct(project, product, FillProjectCard);

    private void SortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectsList is null) return;
        RefreshProjects(SelectedProject, allowAutoSelect: false);
    }

    private static string PayrollLabel(Payroll payroll)
    {
        var amount = payroll.Mode == "fixed" ? payroll.Amount : payroll.Rate;
        var mode = payroll.Mode == "fixed" ? "фикс." : "за м²";
        return $"{payroll.Note} · {amount:0} руб. · {mode}";
    }

    private void RefreshMaterials(MaterialChoice? select = null)
    {
        _loadingMaterials = true;
        _materialSelection.Clear();
        MaterialGroupActionBox.Visibility = Visibility.Collapsed;
        MaterialGroupActionBox.SelectedIndex = 0;
        _materialPage.Refresh(select);
        _materialSelectionModel.Reset(_materialPage.ChoicesInViewOrder.Select(choice => choice.Material.Id));
        _loadingMaterials = false;
        _ = UpdateMaterialTexturePreviewAsync();
    }

    private void FillMaterial() => _materialPage.RenderSelected();

    private async void AddMaterialClick(object sender, RoutedEventArgs e)
    {
        try
        {
            RefreshMaterials(_materialCommands.AddFromEditor(_materialPage));
            MaterialNameBox.Focus(FocusState.Programmatic);
            MaterialNameBox.SelectAll();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await _dialogs.ShowMessageAsync(UiText.Get("Не удалось создать материал"),error.Message);
        }
    }

    private async void SaveMaterialClick(object sender, RoutedEventArgs e)
    {
        if (SelectedMaterial is { } choice)
        {
            var saved = await _materialCommands.SaveFromEditorAsync(_materialPage, choice);
            if (saved is not null)
            {
                RefreshMaterials(saved);
                ShowSuccess(UiText.Get("Материал сохранён"));
            }
        }
    }

    private void SettingsSaveClick(object sender, RoutedEventArgs e)
    {
        _settingsPage.CommitCutLayout(App.Preferences);
        ShowSuccess(UiText.Get("Настройки сохранены"));
    }

    private void ShowSuccess(string message)
    {
        NotificationCenter.Publish(UiText.Get("Готово"),message);
    }

    private async void DeleteMaterialClick(object sender, RoutedEventArgs e)
    {
        if (SelectedMaterial is { } choice) await DeleteMaterialAsync(choice);
    }

    private async Task DeleteMaterialAsync(MaterialChoice choice)
    {
        if (await _materialCommands.DeleteAsync(choice))
        {
            RefreshCurrentMaterialCategory();
            NotificationCenter.Publish(UiText.Get("Материал удалён"),UiText.Format($"Материал «{choice.Material.Name}» удалён."));
        }
    }

    private void RefreshCurrentMaterialCategory()
    {
        _loadingMaterials = true;
        _materialPage.RefreshCurrentCategory();
        ResetMaterialSelection();
        _loadingMaterials = false;
        _ = UpdateMaterialTexturePreviewAsync();
    }
    private void MaterialCategoriesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingMaterials) return;
        _loadingMaterials = true;
        _materialPage.OnCategoryChanged();
        ResetMaterialSelection();
        _loadingMaterials = false;
        _ = UpdateMaterialTexturePreviewAsync();
    }

    private void MaterialsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingMaterials) return;
        if (ReferenceEquals(sender, MaterialsList))
        {
            SetMaterialSelection(MaterialsList.SelectedItems.OfType<MaterialChoice>());
            if (!IsKeyDown(VirtualKey.Control) && !IsKeyDown(VirtualKey.Shift))
                _materialSelectionModel.ReplaceSelection(_materialSelection.Select(choice => choice.Material.Id));
        }
    }

    private void MaterialGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingMaterials) return;
        if (e.AddedItems.Count > 0 && !IsKeyDown(VirtualKey.Control) && !IsKeyDown(VirtualKey.Shift) && sender is ListView currentList)
        {
            _materialSelection.Clear();
            _loadingMaterials = true;
            try
            {
                MaterialsList.SelectedItems.Clear();
                foreach (var list in DescendantListViews(GroupedMaterialsList))
                    if (!ReferenceEquals(list, currentList)) list.SelectedItems.Clear();
            }
            finally { _loadingMaterials = false; }
        }
        foreach (var item in e.AddedItems.OfType<MaterialChoice>())
            if (_materialSelection.All(selected => selected.Material.Id != item.Material.Id)) _materialSelection.Add(item);
        foreach (var item in e.RemovedItems.OfType<MaterialChoice>())
            _materialSelection.RemoveAll(selected => selected.Material.Id == item.Material.Id);
        if (!IsKeyDown(VirtualKey.Control) && !IsKeyDown(VirtualKey.Shift))
            _materialSelectionModel.ReplaceSelection(_materialSelection.Select(choice => choice.Material.Id));
        UpdateMaterialGroupAction();
        var editorChoice = e.AddedItems.OfType<MaterialChoice>().FirstOrDefault()
            ?? (sender as ListView)?.SelectedItems.OfType<MaterialChoice>().FirstOrDefault();
        if (editorChoice is { } choice)
        {
            _materialPage.SelectMaterial(choice);
            _ = UpdateMaterialTexturePreviewAsync();
        }
    }

    private static bool IsKeyDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) != 0;

    private void MaterialItemClicked(object sender, ItemClickEventArgs e)
    {
        if (_loadingMaterials || e.ClickedItem is not MaterialChoice clicked) return;
        _materialFocusRequests.Begin();
        var choices = VisibleMaterialChoices();
        _materialSelectionModel.SetVisibleOrder(choices.Select(choice => choice.Material.Id));
        _materialSelectionModel.Apply(clicked.Material.Id, IsKeyDown(VirtualKey.Control), IsKeyDown(VirtualKey.Shift));
        ApplyMaterialSelection(choices, clicked);
    }

    private IReadOnlyList<MaterialChoice> VisibleMaterialChoices() => GroupedMaterialsList.Visibility == Visibility.Visible
        ? GroupedMaterialsList.Items.OfType<MaterialGroupView>()
            .Where(group => group.Name.Length == 0 || group.IsExpanded).SelectMany(group => group.Items).ToList()
        : MaterialsList.Items.OfType<MaterialChoice>().ToList();

    private void ApplyMaterialSelection(IReadOnlyList<MaterialChoice> choices, MaterialChoice? editorChoice)
    {
        var selected = choices.Where(choice => _materialSelectionModel.SelectedIds.Contains(choice.Material.Id)).ToList();

        _loadingMaterials = true;
        try
        {
            MaterialsList.SelectedItems.Clear();
            foreach (var list in DescendantListViews(GroupedMaterialsList)) list.SelectedItems.Clear();
            var selectedIds = _materialSelectionModel.SelectedIds;
            if (MaterialsList.Visibility == Visibility.Visible)
                foreach (var choice in MaterialsList.Items.OfType<MaterialChoice>().Where(choice => selectedIds.Contains(choice.Material.Id)))
                    MaterialsList.SelectedItems.Add(choice);
            foreach (var list in DescendantListViews(GroupedMaterialsList))
                foreach (var choice in list.Items.OfType<MaterialChoice>().Where(choice => selectedIds.Contains(choice.Material.Id)))
                    list.SelectedItems.Add(choice);
        }
        finally { _loadingMaterials = false; }

        SetMaterialSelection(selected, editorChoice);
    }

    private static IEnumerable<ListView> DescendantListViews(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is ListView list) yield return list;
            foreach (var nested in DescendantListViews(child)) yield return nested;
        }
    }

    private void SetMaterialSelection(IEnumerable<MaterialChoice> choices, MaterialChoice? focusChoice = null)
    {
        _materialSelection.Clear();
        _materialSelection.AddRange(choices);
        UpdateMaterialGroupAction();
        var focused = focusChoice ?? _materialSelection.FirstOrDefault();
        if (focused is { } choice)
        {
            _materialPage.SelectMaterial(choice);
            _ = UpdateMaterialTexturePreviewAsync();
        }
    }

    private void ResetMaterialSelection()
    {
        _materialFocusRequests.Begin();
        _materialSelection.Clear();
        _materialSelectionModel.Reset(_materialPage.ChoicesInViewOrder.Select(choice => choice.Material.Id));
        MaterialsList.SelectedItems.Clear();
        foreach (var list in DescendantListViews(GroupedMaterialsList)) list.SelectedItems.Clear();
        UpdateMaterialGroupAction();
    }

    private void UpdateMaterialGroupAction()
    {
        MaterialGroupActionBox.Visibility = Visibility.Collapsed;
        _materialGroupActionChanging = true;
        MaterialGroupActionBox.SelectedIndex = 0;
        _materialGroupActionChanging = false;
    }

    private void MaterialSortChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingMaterials) return;
        _loadingMaterials = true;
        _materialPage.RefreshCurrentCategory();
        ResetMaterialSelection();
        _loadingMaterials = false;
    }

    private void MaterialListKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Handled || sender is not ListView list) return;
        MaterialChoice? focused = null;
        for (var element = e.OriginalSource as DependencyObject; element is not null && !ReferenceEquals(element, list); element = VisualTreeHelper.GetParent(element))
            if (element is ListViewItem { Content: MaterialChoice choice }) { focused = choice; break; }
        focused ??= list.SelectedItem as MaterialChoice;
        if (e.Key == VirtualKey.Space && IsKeyDown(VirtualKey.Control) && !IsKeyDown(VirtualKey.Shift) && focused is not null)
        {
            var choices = VisibleMaterialChoices();
            _materialSelectionModel.SetVisibleOrder(choices.Select(choice => choice.Material.Id));
            _materialSelectionModel.Apply(focused.Material.Id, control: true, shift: false);
            e.Handled = true;
            ApplyMaterialSelection(choices, _materialPage.SelectedMaterial);
            FocusMaterialRow(focused);
            return;
        }
        if (e.Key is VirtualKey.Up or VirtualKey.Down or VirtualKey.Home or VirtualKey.End)
        {
            if (focused is null) return;
            var choices = VisibleMaterialChoices();
            _materialSelectionModel.SetVisibleOrder(choices.Select(choice => choice.Material.Id));
            var delta = e.Key switch { VirtualKey.Up => -1, VirtualKey.Down => 1, VirtualKey.Home => int.MinValue, _ => int.MaxValue };
            var control = IsKeyDown(VirtualKey.Control);
            var shift = IsKeyDown(VirtualKey.Shift);
            var targetId = _materialSelectionModel.Move(focused.Material.Id, delta, control, shift);
            var target = choices.FirstOrDefault(choice => choice.Material.Id == targetId);
            if (target is null) return;
            e.Handled = true;
            ApplyMaterialSelection(choices, control && !shift ? _materialPage.SelectedMaterial : target);
            FocusMaterialRow(target);
            return;
        }
        if (e.Key != VirtualKey.Application && !(e.Key == VirtualKey.F10 && IsKeyDown(VirtualKey.Shift))) return;
        BuildContextMenu(list, focused).ShowAt(list);
        e.Handled = true;
    }

    private void FocusMaterialRow(MaterialChoice target)
    {
        var focusRequest = _materialFocusRequests.Begin();
        var lists = MaterialsList.Visibility == Visibility.Visible ? new[] { MaterialsList } : DescendantListViews(GroupedMaterialsList);
        foreach (var destination in lists)
        {
            if (!destination.Items.Contains(target)) continue;
            destination.ScrollIntoView(target);
            if (destination.ContainerFromItem(target) is ListViewItem realized)
                realized.Focus(FocusState.Keyboard);
            else destination.DispatcherQueue.TryEnqueue(() =>
            {
                if (!_materialFocusRequests.IsCurrent(focusRequest) || MaterialsPage.Visibility != Visibility.Visible) return;
                if (destination.ContainerFromItem(target) is ListViewItem container)
                    container.Focus(FocusState.Keyboard);
            });
            break;
        }
    }

    private async void MaterialGroupActionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingMaterials || _materialGroupActionChanging || MaterialGroupActionBox.SelectedItem is not ComboBoxItem item) return;
        var action = item.Tag?.ToString();
        if (action is not ("merge" or "ungroup")) return;
        try
        {
            if (action == "merge")
            {
                if (_materialSelection.Count < 2)
                {
                    NotificationCenter.Publish(UiText.Get("Нужно выбрать несколько материалов"),UiText.Get("Для объединения выберите минимум два материала."));
                    return;
                }
                var groupName = await AskMaterialGroupNameAsync();
                if (string.IsNullOrWhiteSpace(groupName)) return;
                var selectedIds = _materialSelection.Select(choice => choice.Material.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                App.State.CommitMutation(() =>
                {
                    foreach (var material in App.State.Database.Materials.Values.SelectMany(materials => materials)
                                 .Where(material => selectedIds.Contains(material.Id)))
                        material.Group = groupName.Trim();
                });
                RefreshMaterials(_materialSelection[0]);
                NotificationCenter.Publish(UiText.Get("Материалы объединены"),UiText.Format($"Выбранные материалы объединены в группу «{groupName.Trim()}»."));
            }
            else
            {
                var selectedIds = _materialSelection.Select(choice => choice.Material.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                App.State.CommitMutation(() =>
                {
                    foreach (var material in App.State.Database.Materials.Values.SelectMany(materials => materials)
                                 .Where(material => selectedIds.Contains(material.Id)))
                        material.Group = string.Empty;
                });
                RefreshMaterials(_materialSelection.FirstOrDefault());
                NotificationCenter.Publish(UiText.Get("Материалы исключены из группы"),UiText.Get("Выбранные материалы снова отображаются без группы."));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            NotificationCenter.Publish(UiText.Get("Не удалось изменить группу"),error.Message);
        }
    }

    private async Task<string?> AskMaterialGroupNameAsync()
    {
        var input = new TextBox { PlaceholderText = UiText.Get("Например, Kronospan") };
        var dialog = new ContentDialog
        {
            Title = UiText.Get("Объединить материалы в группу"),
            Content = input,
            PrimaryButtonText = UiText.Get("Объединить"),
            CloseButtonText = UiText.Get("Отмена"),
            XamlRoot = Root.XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text : null;
    }

    private void MaterialSplitterPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not Button splitter) return;
        splitter.Focus(FocusState.Pointer);
        _activeMaterialSplitter = splitter;
        _materialSplitterStartX = e.GetCurrentPoint(MaterialsPage).Position.X;
        _materialSplitterStartWidth = splitter == MaterialCategoriesSplitter
            ? MaterialCategoriesColumn.Width.Value
            : MaterialListColumn.Width.Value;
        splitter.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void MaterialSplitterMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_activeMaterialSplitter is null || !e.Pointer.IsInContact) return;
        var delta = e.GetCurrentPoint(MaterialsPage).Position.X - _materialSplitterStartX;
        var categories = _activeMaterialSplitter == MaterialCategoriesSplitter;
        var other = categories ? MaterialListColumn.Width.Value : MaterialCategoriesColumn.Width.Value;
        var width = UiLayout.MaterialColumnWidth(_materialSplitterStartWidth + delta, MaterialsPage.ActualWidth, other, categories);
        if (_activeMaterialSplitter == MaterialCategoriesSplitter)
            MaterialCategoriesColumn.Width = new GridLength(width);
        else
            MaterialListColumn.Width = new GridLength(width);
        e.Handled = true;
    }

    private void MaterialSplitterReleased(object sender, PointerRoutedEventArgs e)
    {
        _activeMaterialSplitter?.ReleasePointerCapture(e.Pointer);
        _activeMaterialSplitter = null;
        e.Handled = true;
    }

    private void MaterialSplitterCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _activeMaterialSplitter = null;
        e.Handled = true;
    }

    private void MaterialSplitterKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.Left or VirtualKey.Right)) return;
        var categories = ReferenceEquals(sender, MaterialCategoriesSplitter);
        var column = categories ? MaterialCategoriesColumn : MaterialListColumn;
        var other = categories ? MaterialListColumn.Width.Value : MaterialCategoriesColumn.Width.Value;
        var step = e.Key == VirtualKey.Left ? -16 : 16;
        column.Width = new GridLength(UiLayout.MaterialColumnWidth(column.Width.Value + step, MaterialsPage.ActualWidth, other, categories));
        e.Handled = true;
    }

    private async Task UpdateMaterialTexturePreviewAsync()
    {
        var request = _materialPreviewRequests.Begin();
        var material = _materialPage.SelectedMaterial?.Material;
        var source = material?.Texture ?? "";
        if (material is not null && source.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) && ProductImageData.Read(source) is { } bytes)
        {
            var normalized = await ImageResizeService.ResizeAsync(bytes, 100, 100, cropToBounds: true);
            if (!_materialPreviewRequests.IsCurrent(request) || material != _materialPage.SelectedMaterial?.Material) return;
            if (!string.IsNullOrWhiteSpace(normalized) && normalized != source)
            {
                material.Texture = normalized;
                source = normalized;
            }
        }
        if (!_materialPreviewRequests.IsCurrent(request)) return;
        await ImagePreviewService.SetVisibleAsync(MaterialTexturePreview, source,
            isCurrent: () => _materialPreviewRequests.IsCurrent(request) && material == _materialPage.SelectedMaterial?.Material);
    }

    private async void PickMaterialTextureClick(object sender, RoutedEventArgs e)
    {
        if (_materialPage.SelectedMaterial is not { } choice) return;
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        var texture = await ImageResizeService.FromFileAsync(file.Path, 100, 100, cropToBounds: true);
        if (texture is null)
        {
            await _dialogs.ShowMessageAsync(UiText.Get("Не удалось загрузить текстуру"),UiText.Get("Выберите корректное изображение PNG или JPEG."));
            return;
        }
        choice.Material.Texture = texture;
        await UpdateMaterialTexturePreviewAsync();
        ShowSuccess(UiText.Get("Текстура материала загружена"));
    }

    private void MaterialUnitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingMaterials) return;
        _materialPage.UpdateSheetFields();
    }

    private void MaterialPriceModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingMaterials || _materialPage is null) return;
        _materialPage.UpdatePriceModeFields();
    }

    private void LoadSettings()
    {
        if (ThemeBox is null || AccentBox is null) return;
        _loadingSettings = true;
        _settingsPage.Load(App.Preferences.Current);
        LanguageBox.SelectedItem = LanguageBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == UiText.NormalizeLanguage(App.Preferences.Current.Language));
        RefreshCloudSettings();
        CursorRevealSwitch.IsOn = App.Preferences.Current.CursorRevealEnabled;
        AcrylicBackgroundSwitch.IsOn = App.Preferences.Current.AcrylicBackgroundEnabled;
          DetailingImagesSwitch.IsOn = App.Preferences.Current.DetailingIncludeImages;
        _loadingSettings = false;
        ApplyCompactMode();
        ApplyAnimations();
    }

    private async void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || LanguageBox.SelectedItem is not ComboBoxItem item) return;
        var language = UiText.NormalizeLanguage(item.Tag?.ToString());
        if (language == App.Preferences.Current.Language) return;
        App.Preferences.SetLanguage(language);
        if (await _dialogs.ConfirmLanguageRestartAsync(language))
        {
            App.RestartRequested = true;
            Close();
        }
    }

    private void ThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || ThemeBox.SelectedItem is not ComboBoxItem item) return;
        App.Preferences.SetTheme(item.Tag?.ToString() ?? "auto");
        App.Preferences.Apply(this);
        ApplyTitleBarForeground();
    }

    private void AccentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || AccentBox.SelectedItem is not ComboBoxItem item) return;
        App.Preferences.SetAccent(item.Tag?.ToString() ?? "system");
        App.Preferences.Apply(this);
        ApplyTitleBarForeground();
    }

    private void CompactModeToggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        App.Preferences.SetCompactMode(CompactModeSwitch.IsOn);
        ApplyCompactMode();
    }

    private void AnimationsToggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        App.Preferences.SetAnimationsEnabled(AnimationsSwitch.IsOn);
        ApplyAnimations();
    }

    private void AcrylicBackgroundToggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        App.Preferences.SetAcrylicBackgroundEnabled(AcrylicBackgroundSwitch.IsOn);
    }

    private void CursorRevealToggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        App.Preferences.SetCursorRevealEnabled(CursorRevealSwitch.IsOn);
    }

    private void CutColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string palette }) return;
        App.Preferences.SetCutPalette(palette);
    }


    private void CutLayoutOptionsChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingSettings) return;
        App.Preferences.SetCutLayoutOptions(CutTrimBox.Value, CutGapBox.Value);
    }
    private void CutExportFormatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || CutExportFormatBox.SelectedItem is not ComboBoxItem item) return;
        App.Preferences.SetCutExportFormat(item.Tag?.ToString() ?? "pdf");
    }

    private void DetailingImagesToggled(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings) return;
        App.Preferences.SetDetailingIncludeImages(DetailingImagesSwitch.IsOn);
    }
    private void DetailingExportFormatChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings || DetailingExportFormatBox.SelectedItem is not ComboBoxItem item) return;
        App.Preferences.SetDetailingExportFormat(item.Tag?.ToString() ?? "pdf");
    }
    private void ApplyCompactMode() => _settingsPage.ApplyCompactMode(App.Preferences.Current.CompactMode);

    private void ApplyAnimations() => _settingsPage.ApplyAnimations(App.Preferences.Current.AnimationsEnabled);
    private void SettingsSectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag?.ToString() ?? "appearance";
        _settingsPage.ShowSection(tag);
    }

    private async void CreateCutMapClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is not { } project || _buildingCut) return;
        _buildingCut = true;
        if (sender is Button button) button.IsEnabled = false;
        try { await _reports.OpenCutReportAsync(project); }
        catch (Exception error) { await _dialogs.ShowMessageAsync(UiText.Get("Не удалось построить раскрой"),error.Message); }
        finally
        {
            _buildingCut = false;
            if (sender is Button control) control.IsEnabled = true;
        }
    }
    private bool _buildingCut;
    private async void ProductThumbnailLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Image image)
            await ImagePreviewService.SetVisibleAsync(image, image.Tag as string ?? "");
    }

    private async void CreateDetailingClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is not { } project || _buildingDetailing) return;
        _buildingDetailing = true;
        if (sender is Button button) button.IsEnabled = false;
        try { await _reports.OpenDetailingReportAsync(project); }
        catch (Exception error) { await _dialogs.ShowMessageAsync(UiText.Get("Не удалось создать деталировку"),error.Message); }
        finally
        {
            _buildingDetailing = false;
            if (sender is Button control) control.IsEnabled = true;
        }
    }
    private bool _buildingDetailing;

    private bool _buildingMaterialSummary;
    private async void CreateMaterialSummaryClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is not { } project || _buildingMaterialSummary) return;
        _buildingMaterialSummary = true;
        if (sender is Button button) button.IsEnabled = false;
        try { await _reports.OpenMaterialSummaryAsync(project); }
        catch (Exception error) { await _dialogs.ShowMessageAsync(UiText.Get("Сводная таблица"), error.Message); }
        finally
        {
            _buildingMaterialSummary = false;
            if (sender is Button control) control.IsEnabled = true;
        }
    }

    private async void ExportDatabaseClick(object sender, RoutedEventArgs e)
    {
        var result = await _databaseTransfer.ExportAsync();
        if (result is not null) ShowDatabaseMessage(result);
    }

    private async void ImportDatabaseClick(object sender, RoutedEventArgs e)
    {
        var result = await _databaseTransfer.ImportAsync();
        if (result is null) return;

        if (result.Imported && result.Succeeded)
        {
            RefreshCustomers();
            RefreshProjects(select: null, allowAutoSelect: false);
            RefreshMaterials();

            if (App.Cloud.IsSignedIn)
            {
                var cloudResult = await RunCloudOperationAsync(() => App.Cloud.PublishImportedDatabaseAsync());
                if (cloudResult.Succeeded)
                    result = result with { Title = UiText.Get("База импортирована и опубликована"), Message = "Импортированная база сохранена локально и в Supabase." };
                else
                    result = result with { Title = UiText.Get("База импортирована локально"), Message = $"Не удалось опубликовать её в Supabase: {cloudResult.Message}" };
                RefreshCloudSettings();
            }
        }

        ShowDatabaseMessage(result);
    }

    private void ShowDatabaseMessage(DatabaseTransferResult result)
    {
        NotificationCenter.Publish(result.Title,result.Message);
    }

    private void RefreshAfterCloudDatabaseLoad()
    {
        var selectedId = SelectedProject?.Id;
        RefreshCustomers();
        RefreshProjects(select: selectedId is null ? null : App.State.Database.Projects.FirstOrDefault(project => project.Id == selectedId), allowAutoSelect: false);
        RefreshMaterials();
        LoadSettings();
    }

    private void RefreshCloudSettings()
    {
        if (CloudSyncStatusText is null) return;
        var cloudCard = CloudCard;
        var loginGrid = CloudLoginGrid;
        var description = CloudDescription;
        var statusBadge = CloudStatusBadge;
        var actions = CloudActionsPanel;
        CloudUiSnapshot? preview = null;
#if VITANCUT_UI_REVIEW
        preview = CloudUiSnapshot.Preview(App.Paths.IsDevelopment,
            Environment.GetEnvironmentVariable("VITANCUT_UI_CLOUD_PREVIEW"));
#endif
        var snapshot = preview ?? new CloudUiSnapshot(App.Cloud.IsEnabled, App.Cloud.IsSignedIn,
            _cloudUiOperations > 0 || App.Cloud.IsOperationInProgress, App.Cloud.HasError, App.Cloud.Status);
        var view = snapshot.Presentation;
        var controlsEnabled = view.ControlsEnabled && preview is null;
        var connected = view.ShowBadge;
        CloudOperationProgress.IsIndeterminate = view.IsBusy;
        CloudOperationProgress.Visibility = view.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        CloudEmailBox.IsEnabled = controlsEnabled;
        CloudPasswordBox.IsEnabled = controlsEnabled;
        CloudSignInButton.IsEnabled = controlsEnabled;

        if (!snapshot.Enabled)
        {
            if (loginGrid is not null) loginGrid.Visibility = Visibility.Collapsed;
            if (description is not null)
            {
                description.Text = UiText.Get("Облачные функции отключены в изолированном режиме разработки.");
                description.Visibility = Visibility.Visible;
            }
            if (actions is not null) actions.Visibility = Visibility.Collapsed;
            if (statusBadge is not null) statusBadge.Visibility = Visibility.Collapsed;
            CloudSyncStatusText.Text = UiText.Get("Тестовые данные хранятся локально.");
            CloudSyncStatusText.Foreground = new SolidColorBrush(Color.FromArgb(255, 175, 175, 175));
            CloudSyncStatusText.Visibility = Visibility.Visible;
            return;
        }

        if (loginGrid is not null) loginGrid.Visibility = view.ShowLogin ? Visibility.Visible : Visibility.Collapsed;
        if (description is not null) description.Visibility = Visibility.Collapsed;
        if (actions is not null) actions.Visibility = Visibility.Collapsed;
        CloudSyncStatusText.Visibility = Visibility.Collapsed;
        CloudSyncStatusText.Text = UiText.Get(snapshot.Status);
        CloudSyncStatusText.Foreground = new SolidColorBrush(snapshot.Error
            ? Color.FromArgb(255, 255, 107, 107)
            : Color.FromArgb(255, 175, 175, 175));
        CloudSyncStatusText.Visibility = view.ShowStatus ? Visibility.Visible : Visibility.Collapsed;

        if (statusBadge is not null)
        {
            statusBadge.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            statusBadge.BorderThickness = new Thickness(1);
            statusBadge.BorderBrush = snapshot.Error ? (Brush)Application.Current.Resources["UiDangerBrush"]
                : new SolidColorBrush(Color.FromArgb(150, 62, 207, 142));
            statusBadge.Background = view.IsHealthy ? new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(230, 18, 62, 45), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(230, 20, 102, 70), Offset = 1 }
                }
            } : new SolidColorBrush(snapshot.Error && !view.IsBusy
                ? Color.FromArgb(255, 56, 27, 31) : Color.FromArgb(255, 28, 40, 35));
            if (_cloudIntervalBox is null)
            {
            var intervalBox = new ComboBox { Width = 150 };
            _cloudIntervalBox = intervalBox;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(intervalBox,UiText.Get("Интервал автопубликации"));
            foreach (var minutes in new[] { 5, 15, 30, 60 })
                intervalBox.Items.Add(new ComboBoxItem { Content = UiText.Format($"Раз в {minutes} мин."), Tag = minutes });
            intervalBox.SelectedIndex = new[] { 5, 15, 30, 60 }.ToList().IndexOf(App.Preferences.Current.CloudPublishIntervalMinutes);
            intervalBox.SelectionChanged += CloudPublishIntervalChanged;

            var schedule = new Grid { ColumnSpacing = 12 };
            schedule.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            schedule.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            schedule.Children.Add(new TextBlock { Text = UiText.Get("Автопубликация изменений"), Foreground = new SolidColorBrush(Colors.White), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(intervalBox, 1);
            schedule.Children.Add(intervalBox);
            var syncNow = new Button { Content = UiText.Get("Синхронизировать сейчас"), HorizontalAlignment = HorizontalAlignment.Left };
            _cloudSyncNowButton = syncNow;
            syncNow.Click += ManualCloudSyncClick;
            _cloudBadgeTitle = new TextBlock { Text = UiText.Get("Облачная синхронизация работает"), Foreground = new SolidColorBrush(Colors.White), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(_cloudBadgeTitle, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);

            statusBadge.Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    _cloudBadgeTitle,
                    new TextBlock { Text = "Supabase · Cutlist", Foreground = new SolidColorBrush(Color.FromArgb(220, 218, 255, 232)), FontSize = 12 },
                    schedule,
                    syncNow
                }
            };
            }
            if (_cloudBadgeTitle is not null)
                _cloudBadgeTitle.Text = UiText.Get(view.Title);
            if (_cloudSyncNowButton is not null) _cloudSyncNowButton.IsEnabled = controlsEnabled;
            _cloudIntervalBox!.IsEnabled = controlsEnabled;
            var desiredInterval = new[] { 5, 15, 30, 60 }.ToList().IndexOf(App.Preferences.Current.CloudPublishIntervalMinutes);
            if (_cloudIntervalBox!.SelectedIndex != desiredInterval) _cloudIntervalBox.SelectedIndex = desiredInterval;
        }

        cloudCard.Background = view.IsHealthy
            ? new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(255, 16, 30, 25), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(255, 11, 57, 39), Offset = 1 }
                }
            }
            : new SolidColorBrush(Color.FromArgb(255, 24, 24, 24));
        cloudCard.BorderBrush = snapshot.Error ? (Brush)Application.Current.Resources["UiDangerBrush"]
            : new SolidColorBrush(connected ? Color.FromArgb(175, 62, 207, 142) : Color.FromArgb(255, 48, 48, 48));
        CloudSyncStatusText.Text = UiText.Get(view.IsBusy ? view.Title : snapshot.Status);
        ConfigureCloudPublishTimer();
    }

    private async Task<T> RunCloudOperationAsync<T>(Func<Task<T>> operation)
    {
        _cloudUiOperations++;
        try
        {
            RefreshCloudSettings();
            return await operation();
        }
        finally
        {
            _cloudUiOperations--;
            RefreshCloudSettings();
        }
    }

    private void CloudPublishIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: ComboBoxItem { Tag: int minutes } }) return;
        App.Preferences.SetCloudPublishIntervalMinutes(minutes);
        ConfigureCloudPublishTimer();
    }

    private async void CloudSignInClick(object sender, RoutedEventArgs e)
    {
        var result = await RunCloudOperationAsync(() => App.Cloud.SignInAsync(CloudEmailBox.Text, CloudPasswordBox.Password));
        if (result.Succeeded && result.DatabaseChanged) RefreshAfterCloudDatabaseLoad();
        ShowCloudResult(result);
        RefreshCloudSettings();
    }

    private async Task CopyCatalogProductToCounterpartyAsync(CustomerProductItem item)
    {
        var targets = App.Catalog.GetCounterparties()
            .Where(name => !string.Equals(name, item.Counterparty, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
        if (targets.Count == 0)
        {
            await _dialogs.ShowMessageAsync(UiText.Get("Нет другого заказчика"),UiText.Get("Сначала добавьте ещё одного заказчика в каталоге."));
            return;
        }

        var targetBox = new ComboBox { ItemsSource = targets, SelectedIndex = 0, MinWidth = 280 };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.ActualTheme,
            Title = UiText.Get("Копировать изделие"),
            Content = targetBox,
            PrimaryButtonText = UiText.Get("Копировать"),
            CloseButtonText = UiText.Get("Отмена"),
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || targetBox.SelectedItem is not string target) return;
        App.Catalog.CopyProduct(item.Counterparty, item.Product, target);
        _catalogPage.RenderProducts();
    }

    private async void ManualCloudSyncClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) button.IsEnabled = false;
        var progressWindow = new Views.CloudPublishWindow();
        _activeCloudPublishWindow = progressWindow;
        progressWindow.Activate();
        try
        {
            var result = await RunCloudOperationAsync(() => App.Cloud.PublishAsync());
            if (!result.Succeeded) result = App.Cloud.RequireSignInAfterSyncFailure(result);
            ShowCloudResult(result);
            if (result.Succeeded && result.DatabaseChanged) RefreshAfterCloudDatabaseLoad();
            RefreshCloudSettings();
            progressWindow.Complete(result.Succeeded, result.Succeeded
                ? "Изменения успешно опубликованы в Supabase."
                : "Изменения сохранены на этом компьютере и будут отправлены позже. " + result.Message);
            await Task.Delay(result.Succeeded ? 700 : 2200);
        }
        catch (Exception error)
        {
            progressWindow.Complete(false, "Изменения сохранены на этом компьютере и будут отправлены позже.");
            NotificationCenter.Publish(UiText.Get("Синхронизация не выполнена"),error.Message);
            await Task.Delay(2200);
        }
        finally
        {
            if (!_shutdownInProgress)
            {
                progressWindow.Close();
                if (ReferenceEquals(_activeCloudPublishWindow, progressWindow)) _activeCloudPublishWindow = null;
                if (sender is Button control) control.IsEnabled = true;
            }
        }
    }

    private async Task RestoreCloudSessionAfterLaunchAsync()
    {
        var result = await RunCloudOperationAsync(() => App.Cloud.RestoreSessionAsync());
        RefreshCloudSettings();
        if (result.Succeeded)
        {
            if (result.DatabaseChanged) RefreshAfterCloudDatabaseLoad();
            return;
        }

        NotificationCenter.Publish(UiText.Get("Облачная синхронизация не выполнена"),result.Message);
    }

    private async void DownloadCloudDatabaseClick(object sender, RoutedEventArgs e)
    {
        var discardLocalChanges = false;
        if (App.Cloud.HasUnsyncedLocalChanges)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                RequestedTheme = Root.ActualTheme,
                Title = UiText.Get("Заменить локальные изменения?"),
                Content = UiText.Get("Несинхронизированные локальные изменения будут заменены облачной версией. Сначала опубликуйте их, если они нужны."),
                PrimaryButtonText = UiText.Get("Загрузить облачную версию"),
                CloseButtonText = UiText.Get("Отмена"),
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            discardLocalChanges = true;
        }

        var result = await RunCloudOperationAsync(() => App.Cloud.DownloadAsync(discardLocalChanges));
        ShowCloudResult(result);
        if (result.Succeeded) RefreshAfterCloudDatabaseLoad();
        RefreshCloudSettings();
    }

    private async void PublishCloudDatabaseClick(object sender, RoutedEventArgs e)
    {
        ShowCloudResult(await RunCloudOperationAsync(() => App.Cloud.PublishAsync()));
        RefreshCloudSettings();
    }

    private async void CloudHistoryClick(object sender, RoutedEventArgs e)
    {
        var history = await RunCloudOperationAsync(() => App.Cloud.GetHistoryAsync());
        if (!history.Succeeded)
        {
            ShowCloudResult(new CloudSyncResult(false, history.Message));
            return;
        }

        var picker = new ComboBox { MinWidth = 320, SelectedIndex = 0 };
        foreach (var version in history.Versions)
            picker.Items.Add(new ComboBoxItem { Content = version.Label, Tag = version });

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            RequestedTheme = Root.ActualTheme,
            Title = UiText.Get("История облачных версий"),
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = UiText.Get("Восстановление заменит только локальную рабочую копию. Перед этим она будет сохранена в резерв."),
                        TextWrapping = TextWrapping.Wrap
                    },
                    picker
                }
            },
            PrimaryButtonText = UiText.Get("Восстановить локально"),
            CloseButtonText = UiText.Get("Отмена"),
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || picker.SelectedItem is not ComboBoxItem { Tag: CloudSnapshotVersion selectedVersion }) return;

        var result = await RunCloudOperationAsync(() => App.Cloud.RestoreHistoryAsync(selectedVersion.Revision));
        ShowCloudResult(result);
        if (result.Succeeded) RefreshAfterCloudDatabaseLoad();
        RefreshCloudSettings();
    }

    private void ShowCloudResult(CloudSyncResult result)
    {
        NotificationCenter.Publish(result.Succeeded ? UiText.Get("Облачная синхронизация") : UiText.Get("Синхронизация не выполнена"),result.Message);
    }

    private void UpdateResponsiveLayout()
    {
        _projectLayout.Update(SelectedProject is not null, _showingProjectListOnNarrow);
        if (ProjectWorkListsGrid is not null)
        {
            var stackLists = ProjectDetailPane.ActualWidth > 0 && ProjectDetailPane.ActualWidth < 850;
            ProjectWorkListsGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            ProjectWorkListsGrid.ColumnDefinitions[1].Width = stackLists ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            ProjectWorkListsGrid.RowDefinitions[1].Height = stackLists ? GridLength.Auto : new GridLength(0);
            Grid.SetColumn(PayrollPanel, stackLists ? 0 : 1);
            Grid.SetRow(PayrollPanel, stackLists ? 1 : 0);
            var headerHeight = ProjectWorkListsGrid.TransformToVisual(ProjectPanel).TransformPoint(new Windows.Foundation.Point()).Y + ProjectPanel.VerticalOffset;
            var listHeight = UiLayout.WorkListHeight(ProjectPanel.ActualHeight, headerHeight);
            const double listPanelChrome = 88;
            ProjectWorkListsGrid.MaxHeight = (listHeight + listPanelChrome) * (stackLists ? 2 : 1) + (stackLists ? 12 : 0);
            ProductsList.Height = listHeight;
            PayrollsList.Height = listHeight;
            ProductsList.MaxHeight = listHeight;
            PayrollsList.MaxHeight = listHeight;
        }
        if (SettingsColumnsHost is null || SettingsRightColumn is null) return;

        var singleColumn = Root.ActualWidth < 1320;
        SettingsColumnsHost.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        SettingsColumnsHost.ColumnDefinitions[1].Width = singleColumn ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        SettingsColumnsHost.RowDefinitions[1].Height = singleColumn ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(SettingsRightColumn, singleColumn ? 0 : 1);
        Grid.SetRow(SettingsRightColumn, singleColumn ? 1 : 0);
    }

    private IntPtr MinSizeSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr refData)
    {
        if (message == WmGetMinMaxInfo)
        {
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            info.MinTrackSize.X = MinimumWindowWidth;
            info.MinTrackSize.Y = MinimumWindowHeight;
            Marshal.StructureToPtr(info, lParam, false);
            return IntPtr.Zero;
        }

        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private delegate IntPtr WindowSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam, UIntPtr subclassId, UIntPtr refData);

    [DllImport("Comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, WindowSubclassProc subclassProc, UIntPtr subclassId, UIntPtr refData);

    [DllImport("Comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }
}
