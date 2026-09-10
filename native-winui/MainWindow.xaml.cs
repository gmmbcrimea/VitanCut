using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Controllers;
using VitanCut.WinUI.Services;
using Windows.UI;
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
    private bool _loadingMaterials;
    // XAML assigns initial control values while the window is being constructed.
    // Ignore those events until persisted preferences are loaded explicitly.
    private bool _loadingSettings = true;
    private bool _firstLoad = true;
    private bool _showingProjectListOnNarrow = true;
    private bool _catalogActionConfigured;
    private Border? _updateCard;
    private SymbolIcon? _updateIcon;
    private TextBlock? _updateTitle;
    private TextBlock? _updateDescription;
    private Button? _installUpdateButton;
    private Button? _checkUpdatesButton;
    private ProgressBar? _updateProgress;
    private Timer? _updateTimer;
    private Timer? _cloudPublishTimer;
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
        _projectPage = new ProjectPageController(
            App.State, ProjectsList, NoProjectsPanel, EmptyStatePanel, ProjectPanel,
            ProjectTitleText, ProjectCustomerText, ProjectAddressText,
            MetricAreaText, MetricMaterialsText, MetricPayrollText, MetricTotalText,
            ProductsList, PayrollsList, NoProductsPanel, NoPayrollsPanel);
        _projectLayout = new ProjectLayoutController(
            ContentHost, ProjectsPage, ProjectListPane, ProjectDetailPane, BackToProjectsButton, MetricsGrid,
            MetricAreaCard, MetricMaterialsCard, MetricPayrollCard, MetricTotalCard);
        _materialPage = new MaterialPageController(
            App.MaterialLibrary, MaterialCategoriesList, MaterialsList, MaterialListEmptyPanel,
            MaterialParametersEmptyPanel, MaterialParametersPanel, MaterialTypeBox, MaterialNameBox,
            MaterialUnitBox, MaterialCostBox, MaterialSheetLengthBox, MaterialSheetWidthBox, MaterialTextureBox);
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
        ArrangeSettingsColumns();
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
        CustomersTabButton.Content = "Каталог";
        CatalogTabButton.Visibility = Visibility.Collapsed;
        ConfigureTopNavigation();
        ConfigureCloudSurface();
        ConfigureUpdateSurface();
        CustomerProjectsTitle.Loaded += (_, _) => ConfigureCatalogActions();
        ConfigureMaterialParameterActions();
        ConfigureTitleBar();
        AppWindow.Closing += MainWindowClosing;
        App.Preferences.Apply(this);
        ApplyTitleBarForeground();
        Root.SizeChanged += (_, _) => UpdateResponsiveLayout();
        Root.Loaded += async (_, _) =>
        {
            ArrangeSettingsColumns();
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
        content.Children.Add(new TextBlock { Text = label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center });
        button.Content = content;
        ToolTipService.SetToolTip(button, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
    }

    private void ConfigureCloudSurface()
    {
        var cloudCard = DatabasePanel.Children.OfType<Border>().LastOrDefault();
        if (cloudCard?.Child is not StackPanel cloudPanel) return;
        if (cloudPanel.Children[0] is Grid header && header.Children.OfType<Border>().FirstOrDefault() is { } iconSurface)
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
            if (header.Children.OfType<StackPanel>().FirstOrDefault() is { } title) title.Visibility = Visibility.Collapsed;
        }
    }

    private void ConfigureUpdateSurface()
    {
        _updateIcon = new SymbolIcon { Symbol = Symbol.Sync, Foreground = new SolidColorBrush(Color.FromArgb(255, 138, 153, 168)) };
        _updateTitle = new TextBlock { Text = "Обновления", Style = (Style)Application.Current.Resources["CardTitleTextBlockStyle"] };
        _updateDescription = new TextBlock { Text = "Проверка обновлений выполняется каждые 30 минут.", Style = (Style)Application.Current.Resources["MutedTextBlockStyle"], TextWrapping = TextWrapping.Wrap };
        _installUpdateButton = new Button
        {
            Content = "Обновить",
            Visibility = Visibility.Collapsed,
            Background = new SolidColorBrush(Color.FromArgb(255, 196, 61, 61)),
            Foreground = new SolidColorBrush(Colors.White)
        };
        _installUpdateButton.Click += InstallUpdateClick;
        _checkUpdatesButton = new Button { Content = "Проверить сейчас" };
        _checkUpdatesButton.Click += CheckUpdatesClick;
        _updateProgress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 6,
            Visibility = Visibility.Collapsed
        };

        var header = new Grid { ColumnSpacing = 14 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.Children.Add(_updateIcon);
        var text = new StackPanel { Spacing = 4 };
        text.Children.Add(_updateTitle);
        text.Children.Add(_updateDescription);
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
    }

    private void RefreshUpdateSurface(AppUpdateInfo update)
    {
        if (_updateCard is null || _updateTitle is null || _updateDescription is null || _updateIcon is null || _installUpdateButton is null || _checkUpdatesButton is null || _updateProgress is null) return;
        _checkUpdatesButton.IsEnabled = true;
        _checkUpdatesButton.Content = "Проверить сейчас";
        _updateProgress.Visibility = Visibility.Collapsed;
        _updateDescription.Text = update.Message;
        switch (update.Availability)
        {
            case UpdateAvailability.UpToDate:
                _updateIcon.Symbol = Symbol.Accept;
                _updateIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 62, 207, 142));
                _updateTitle.Text = "Приложение обновлено";
                _updateCard.BorderBrush = new SolidColorBrush(Color.FromArgb(165, 62, 207, 142));
                _installUpdateButton.Visibility = Visibility.Collapsed;
                break;
            case UpdateAvailability.Available:
                _updateIcon.Symbol = Symbol.Important;
                _updateIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 255, 107, 107));
                _updateTitle.Text = "Доступно обновление";
                _updateCard.BorderBrush = new SolidColorBrush(Color.FromArgb(175, 255, 107, 107));
                _installUpdateButton.Content = $"Обновить до {AppUpdateService.FormatVersion(update.LatestVersion!)}";
                _installUpdateButton.Visibility = Visibility.Visible;
                break;
            default:
                _updateIcon.Symbol = Symbol.Important;
                _updateIcon.Foreground = new SolidColorBrush(Color.FromArgb(255, 242, 192, 108));
                _updateTitle.Text = "Не удалось проверить обновления";
                _updateCard.BorderBrush = new SolidColorBrush(Color.FromArgb(125, 242, 192, 108));
                _installUpdateButton.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private async Task InitializeOnlineServicesAsync()
    {
        await RestoreCloudSessionAfterLaunchAsync();
    }

    private void StartBackgroundTimers()
    {
        _updateTimer ??= new Timer(_ => DispatcherQueue.TryEnqueue(async () => await CheckForUpdatesAsync()));
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
        if (_shutdownRequested || _shutdownInProgress) return;
        if (!App.Cloud.HasUnsyncedLocalChanges) return;

        args.Cancel = true;
        _shutdownInProgress = true;
        _ = PublishBeforeExitAsync();
    }

    private async Task PublishBeforeExitAsync()
    {
        var progressWindow = new Views.CloudPublishWindow();
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
        Application.Current.Exit();
    }

    private async Task CheckForUpdatesAsync()
    {
        var update = await App.Updates.CheckAsync();
        RefreshUpdateSurface(update);
        if (update.Availability != UpdateAvailability.Available) return;

        var updateButton = new Button { Content = "Обновить" };
        updateButton.Click += InstallUpdateClick;
        AppInfoBar.Title = "Доступно обновление";
        AppInfoBar.Message = update.Message;
        AppInfoBar.Severity = InfoBarSeverity.Warning;
        AppInfoBar.ActionButton = updateButton;
        AppInfoBar.IsOpen = true;
    }

    private async void CheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        if (_checkUpdatesButton is null) return;
        _checkUpdatesButton.IsEnabled = false;
        _checkUpdatesButton.Content = "Проверяем...";
        try { await CheckForUpdatesAsync(); }
        finally
        {
            _checkUpdatesButton.IsEnabled = true;
            _checkUpdatesButton.Content = "Проверить сейчас";
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
        _cloudPublishTimer ??= new Timer(_ => DispatcherQueue.TryEnqueue(async () => await PublishCloudChangesAutomaticallyAsync()));
        _cloudPublishTimer.Change(TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(minutes));
        _activeCloudPublishInterval = minutes;
    }

    private async Task PublishCloudChangesAutomaticallyAsync()
    {
        if (_cloudPublishInProgress || !App.Cloud.IsSignedIn || !App.Cloud.HasUnsyncedLocalChanges) return;
        _cloudPublishInProgress = true;
        try
        {
            var result = await App.Cloud.PublishAsync();
            RefreshCloudSettings();
            if (result.Succeeded) return;
            AppInfoBar.Title = "Не удалось опубликовать изменения";
            AppInfoBar.Message = result.Message;
            AppInfoBar.Severity = InfoBarSeverity.Warning;
            AppInfoBar.ActionButton = null;
            AppInfoBar.IsOpen = true;
        }
        finally
        {
            _cloudPublishInProgress = false;
        }
    }

    private async void InstallUpdateClick(object sender, RoutedEventArgs e)
    {
        var update = App.Updates.LastCheck;
        if (update is null || !update.CanInstall) return;
        if (_installUpdateButton is not null)
        {
            _installUpdateButton.IsEnabled = false;
            _installUpdateButton.Content = "Скачивание обновления...";
        }

        if (_updateProgress is not null)
        {
            _updateProgress.Value = 0;
            _updateProgress.IsIndeterminate = true;
            _updateProgress.Visibility = Visibility.Visible;
        }

        var progress = new Progress<UpdateDownloadProgress>(status =>
        {
            if (_updateProgress is null || _updateDescription is null) return;
            if (status.Percent is { } percent)
            {
                _updateProgress.IsIndeterminate = false;
                _updateProgress.Value = percent;
                _updateDescription.Text = $"Скачивание обновления: {percent:0}%";
            }
            else
            {
                _updateProgress.IsIndeterminate = true;
                _updateDescription.Text = "Скачивание обновления...";
            }
        });

        if (await App.Updates.InstallAsync(update, progress))
        {
            if (_updateProgress is not null)
            {
                _updateProgress.IsIndeterminate = false;
                _updateProgress.Value = 100;
            }
            if (_updateDescription is not null) _updateDescription.Text = "Файлы подготовлены. Перезапуск приложения...";
            AppInfoBar.Title = "Обновление готово";
            AppInfoBar.Message = "Приложение перезапустится после замены файлов.";
            AppInfoBar.Severity = InfoBarSeverity.Success;
            AppInfoBar.ActionButton = null;
            AppInfoBar.IsOpen = true;
            await Task.Delay(700);
            Application.Current.Exit();
            return;
        }

        if (_installUpdateButton is not null)
        {
            _installUpdateButton.IsEnabled = true;
            _installUpdateButton.Content = "Повторить обновление";
        }
        if (_updateProgress is not null) _updateProgress.Visibility = Visibility.Collapsed;
        AppInfoBar.Title = "Не удалось установить обновление";
        AppInfoBar.Message = "Проверьте подключение к сети и права на изменение папки приложения.";
        AppInfoBar.Severity = InfoBarSeverity.Error;
        AppInfoBar.ActionButton = null;
        AppInfoBar.IsOpen = true;
    }

    private void ArrangeSettingsColumns()
    {
        var leftColumn = SettingsColumnsHost.Children.OfType<StackPanel>()
            .FirstOrDefault(panel => !ReferenceEquals(panel, SettingsRightColumn) && Grid.GetColumn(panel) == 0);
        if (leftColumn is null) return;

        // Remove from both known columns before inserting. This works both during initial
        // XAML construction and after a responsive layout pass.
        leftColumn.Children.Remove(DatabasePanel);
        leftColumn.Children.Remove(CuttingPanel);
        leftColumn.Children.Remove(DetailingPanel);
        SettingsRightColumn.Children.Remove(DatabasePanel);
        SettingsRightColumn.Children.Remove(CuttingPanel);
        SettingsRightColumn.Children.Remove(DetailingPanel);

        var nextToAppearance = Math.Max(0, leftColumn.Children.IndexOf(AppearancePanel) + 1);
        leftColumn.Children.Insert(nextToAppearance, CuttingPanel);
        leftColumn.Children.Insert(nextToAppearance + 1, DetailingPanel);
        SettingsRightColumn.Children.Insert(0, DatabasePanel);
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

    private void ConfigureMaterialParameterActions()
    {
        // The editor needs a visible save command; creation belongs to the material list.
    }

    private void ConfigureCatalogActions()
    {
        if (_catalogActionConfigured) return;
        if (VisualTreeHelper.GetParent(CustomerProjectsTitle) is not StackPanel header ||
            VisualTreeHelper.GetParent(header) is not Grid container)
        {
            return;
        }

        var addButton = new Button
        {
            Content = "Добавить изделие",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Style = (Style)Application.Current.Resources["AccentButtonStyle"]
        };
        addButton.Click += AddCatalogProductClick;
        container.Children.Add(addButton);
        _catalogActionConfigured = true;
    }

    private void TopTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tab }) ShowTab(tab);
    }

    private void ShowTab(string tab)
    {
        _navigation.Show(tab, App.Preferences.Current.AnimationsEnabled);
        if (tab == "customers") RefreshCustomers();
        if (tab == "materials") RefreshMaterials(SelectedMaterial);
        if (tab == "settings") LoadSettings();
        UpdateResponsiveLayout();
        DispatcherQueue.TryEnqueue(() => ThemeService.ApplyLayout(Root, App.Preferences.Current));
    }

    private void ConfigureNestedScrolling() =>
        _nestedScrolling.Attach(ProductsList, PayrollsList);

    private void ConfigureContextMenuSelection() =>
        _contextMenus.Configure(ProjectsList, ProductsList, PayrollsList, CustomersList, CustomerProjectsList, MaterialsList);

    private void ListViewRightTappedSelectItem(object sender, RightTappedRoutedEventArgs e) =>
        _contextMenus.Show(sender, e, BuildContextMenu);

    private MenuFlyout BuildContextMenu(ListView list, object? data)
    {
        var menu = new MenuFlyout();
        var menuData = data ?? list.SelectedItem;
        void Add(string text, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        if (list == CustomerProjectsList)
        {
            Add("Добавить", () => AddCatalogProductClick(list, new RoutedEventArgs()));
            if (menuData is CustomerProductItem item)
            {
                Add("Изменить", () => OpenCatalogProductWindow(item.Counterparty, item.Product));
                Add("Удалить", async () =>
                {
                    if (!await _dialogs.ConfirmDeleteAsync("Удалить из каталога?", item.Product.Name + ". Изделия в проектах сохранятся.")) return;
                    try { App.Catalog.DeleteProduct(item.Counterparty, item.Product); _catalogPage.RenderProducts(); }
                    catch (Exception error) { await _dialogs.ShowMessageAsync("Не удалось удалить", error.Message); }
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

        if (list == MaterialsList)
        {
            Add("Добавить", () => AddMaterialClick(list, new RoutedEventArgs()));
            if (menuData is MaterialChoice choice)
            {
                Add("Изменить", () =>
                {
                    MaterialsList.SelectedItem = choice;
                    FillMaterial();
                    MaterialNameBox.Focus(FocusState.Programmatic);
                });
                Add("Удалить", async () => await DeleteMaterialAsync(choice));
            }
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
        var edit = new MenuFlyoutItem { Text = "Изменить" };
        edit.Click += (_, _) => OpenProductWindow(project, product);
        var remove = new MenuFlyoutItem { Text = "Удалить" };
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
        if (await _projectCommands.DeleteProductAsync(project, product)) FillProjectCard();
    }

    private void AddPayrollClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is not { } project) return;
        _projectCommands.AddPayroll(project);
        FillProjectCard();
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
        if (SelectedProject is { } project && await _projectCommands.DeletePayrollAsync(project, index)) FillProjectCard();
    }

    private void ProjectsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingProjects) return;
        if (SelectedProject is not null && _projectLayout.IsNarrow)
        {
            _showingProjectListOnNarrow = false;
        }
        FillProjectCard();
    }

    private void BackToProjectsClick(object sender, RoutedEventArgs e)
    {
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
        _materialPage.Refresh(select);
        _loadingMaterials = false;
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
            await _dialogs.ShowMessageAsync("Не удалось создать материал", error.Message);
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
                ShowSuccess("Материал сохранён");
            }
        }
    }

    private void SettingsSaveClick(object sender, RoutedEventArgs e)
    {
        _settingsPage.CommitCutLayout(App.Preferences);
        App.State.Save();
        ShowSuccess("Настройки сохранены");
    }

    private void ShowSuccess(string message)
    {
        AppInfoBar.Title = "Готово";
        AppInfoBar.Message = message;
        AppInfoBar.Severity = InfoBarSeverity.Success;
        AppInfoBar.IsOpen = true;
    }

    private async void DeleteMaterialClick(object sender, RoutedEventArgs e)
    {
        if (SelectedMaterial is { } choice) await DeleteMaterialAsync(choice);
    }

    private async Task DeleteMaterialAsync(MaterialChoice choice)
    {
        if (await _materialCommands.DeleteAsync(choice)) RefreshCurrentMaterialCategory();
    }

    private void RefreshCurrentMaterialCategory() => _materialPage.RefreshCurrentCategory();
    private void MaterialCategoriesSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingMaterials) return;
        _loadingMaterials = true;
        _materialPage.OnCategoryChanged();
        _loadingMaterials = false;
    }

    private void MaterialsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingMaterials) return;
        _materialPage.RenderSelected();
    }

    private void MaterialUnitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingMaterials) return;
        _materialPage.UpdateSheetFields();
    }

    private void LoadSettings()
    {
        if (ThemeBox is null || AccentBox is null) return;
        _loadingSettings = true;
        _settingsPage.Load(App.Preferences.Current);
        RefreshCloudSettings();
        CursorRevealSwitch.IsOn = App.Preferences.Current.CursorRevealEnabled;
          DetailingImagesSwitch.IsOn = App.Preferences.Current.DetailingIncludeImages;
        _loadingSettings = false;
        ApplyCompactMode();
        ApplyAnimations();
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
        catch (Exception error) { await _dialogs.ShowMessageAsync("Не удалось построить раскрой", error.Message); }
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
            image.Visibility = await ImagePreviewService.SetAsync(image, image.Tag as string ?? "") ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CreateDetailingClick(object sender, RoutedEventArgs e)
    {
        if (SelectedProject is { } project) _reports.OpenDetailingReport(project);
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
                var cloudResult = await App.Cloud.PublishImportedDatabaseAsync();
                if (cloudResult.Succeeded)
                    result = result with { Title = "База импортирована и опубликована", Message = "Импортированная база сохранена локально и в Supabase." };
                else
                    result = result with { Title = "База импортирована локально", Message = $"Не удалось опубликовать её в Supabase: {cloudResult.Message}" };
                RefreshCloudSettings();
            }
        }

        ShowDatabaseMessage(result);
    }

    private void ShowDatabaseMessage(DatabaseTransferResult result)
    {
        DatabaseInfoBar.Severity = result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        DatabaseInfoBar.Title = result.Title;
        DatabaseInfoBar.Message = result.Message;
        DatabaseInfoBar.IsOpen = true;
    }

    private void RefreshAfterCloudDatabaseLoad()
    {
        RefreshCustomers();
        RefreshProjects(select: null, allowAutoSelect: false);
        RefreshMaterials();
        LoadSettings();
    }

    private void RefreshCloudSettings()
    {
        if (CloudSyncStatusText is null) return;
        var cloudCard = DatabasePanel.Children.OfType<Border>().LastOrDefault();
        if (cloudCard?.Child is not StackPanel cloudPanel) return;
        var loginGrid = CloudEmailBox.Parent as Grid;
        var description = cloudPanel.Children.OfType<TextBlock>().FirstOrDefault();
        var statusBadge = cloudPanel.Children.OfType<Border>().FirstOrDefault();
        var actions = cloudPanel.Children.OfType<StackPanel>()
            .FirstOrDefault(panel => panel.Orientation == Orientation.Horizontal && panel.Children.OfType<Button>().Any());
        var connected = App.Cloud.IsSignedIn;

        if (loginGrid is not null) loginGrid.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        if (description is not null) description.Visibility = Visibility.Collapsed;
        if (actions is not null) actions.Visibility = Visibility.Collapsed;
        CloudSyncStatusText.Visibility = Visibility.Collapsed;

        if (statusBadge is not null)
        {
            statusBadge.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
            statusBadge.BorderThickness = new Thickness(1);
            statusBadge.BorderBrush = new SolidColorBrush(Color.FromArgb(150, 62, 207, 142));
            statusBadge.Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(230, 18, 62, 45), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(230, 20, 102, 70), Offset = 1 }
                }
            };
            var intervalBox = new ComboBox { Width = 150 };
            foreach (var minutes in new[] { 5, 15, 30, 60 })
                intervalBox.Items.Add(new ComboBoxItem { Content = $"Раз в {minutes} мин.", Tag = minutes });
            intervalBox.SelectedIndex = new[] { 5, 15, 30, 60 }.ToList().IndexOf(App.Preferences.Current.CloudPublishIntervalMinutes);
            intervalBox.SelectionChanged += CloudPublishIntervalChanged;

            var schedule = new Grid { ColumnSpacing = 12 };
            schedule.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            schedule.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            schedule.Children.Add(new TextBlock { Text = "Автопубликация изменений", Foreground = new SolidColorBrush(Colors.White), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(intervalBox, 1);
            schedule.Children.Add(intervalBox);

            statusBadge.Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Облачная синхронизация работает", Foreground = new SolidColorBrush(Colors.White), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                    new TextBlock { Text = "Supabase · Cutlist", Foreground = new SolidColorBrush(Color.FromArgb(220, 218, 255, 232)), FontSize = 12 },
                    schedule
                }
            };
        }

        cloudCard.Background = connected
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
        cloudCard.BorderBrush = new SolidColorBrush(connected ? Color.FromArgb(175, 62, 207, 142) : Color.FromArgb(255, 48, 48, 48));
        CloudSyncStatusText.Text = App.Cloud.Status;
        ConfigureCloudPublishTimer();
    }

    private void CloudPublishIntervalChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: ComboBoxItem { Tag: int minutes } }) return;
        App.Preferences.SetCloudPublishIntervalMinutes(minutes);
        ConfigureCloudPublishTimer();
    }

    private async void CloudSignInClick(object sender, RoutedEventArgs e)
    {
        var result = await App.Cloud.SignInAsync(CloudEmailBox.Text, CloudPasswordBox.Password);
        if (result.Succeeded && App.Cloud.ShouldDownloadInitialSnapshot)
        {
            result = await App.Cloud.DownloadInitialSnapshotAsync();
            if (result.Succeeded) RefreshAfterCloudDatabaseLoad();
        }
        ShowCloudResult(result);
        RefreshCloudSettings();
        if (App.Cloud.IsSignedIn) AppInfoBar.IsOpen = false;
    }

    private async Task RestoreCloudSessionAfterLaunchAsync()
    {
        var result = await App.Cloud.RestoreSessionAsync();
        RefreshCloudSettings();
        if (result.Succeeded)
        {
            if (App.Cloud.ShouldDownloadInitialSnapshot)
            {
                result = await App.Cloud.DownloadInitialSnapshotAsync();
                if (result.Succeeded)
                {
                    RefreshAfterCloudDatabaseLoad();
                    RefreshCloudSettings();
                    return;
                }
            }
            else if (App.Cloud.HasUnsyncedLocalChanges)
            {
                result = await App.Cloud.PublishAsync();
                RefreshCloudSettings();
                if (result.Succeeded) return;
            }
            else return;
        }

        var settingsButton = new Button { Content = "Настройки" };
        settingsButton.Click += (_, _) => ShowTab("settings");
        AppInfoBar.Title = "Облачная синхронизация";
        AppInfoBar.Message = result.Message;
        AppInfoBar.Severity = InfoBarSeverity.Warning;
        AppInfoBar.ActionButton = settingsButton;
        AppInfoBar.IsOpen = true;
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
                Title = "Заменить локальные изменения?",
                Content = "Несинхронизированные локальные изменения будут заменены облачной версией. Сначала опубликуйте их, если они нужны.",
                PrimaryButtonText = "Загрузить облачную версию",
                CloseButtonText = "Отмена",
                DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            discardLocalChanges = true;
        }

        var result = await App.Cloud.DownloadAsync(discardLocalChanges);
        ShowCloudResult(result);
        if (result.Succeeded) RefreshAfterCloudDatabaseLoad();
        RefreshCloudSettings();
    }

    private async void PublishCloudDatabaseClick(object sender, RoutedEventArgs e)
    {
        ShowCloudResult(await App.Cloud.PublishAsync());
        RefreshCloudSettings();
    }

    private async void CloudHistoryClick(object sender, RoutedEventArgs e)
    {
        var history = await App.Cloud.GetHistoryAsync();
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
            Title = "История облачных версий",
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Восстановление заменит только локальную рабочую копию. Перед этим она будет сохранена в резерв.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    picker
                }
            },
            PrimaryButtonText = "Восстановить локально",
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || picker.SelectedItem is not ComboBoxItem { Tag: CloudSnapshotVersion selectedVersion }) return;

        var result = await App.Cloud.RestoreHistoryAsync(selectedVersion.Revision);
        ShowCloudResult(result);
        if (result.Succeeded) RefreshAfterCloudDatabaseLoad();
        RefreshCloudSettings();
    }

    private void ShowCloudResult(CloudSyncResult result)
    {
        DatabaseInfoBar.Severity = result.Succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        DatabaseInfoBar.Title = result.IsConflict ? "Конфликт облачной синхронизации" : result.Succeeded ? "Облачная синхронизация" : "Не удалось синхронизировать";
        DatabaseInfoBar.Message = result.Message;
        DatabaseInfoBar.IsOpen = true;
    }

    private void UpdateResponsiveLayout()
    {
        _projectLayout.Update(SelectedProject is not null, _showingProjectListOnNarrow);
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
