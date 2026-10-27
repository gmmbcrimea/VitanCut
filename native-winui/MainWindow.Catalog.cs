using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;
using Windows.ApplicationModel.DataTransfer;

namespace VitanCut.WinUI;

public sealed partial class MainWindow
{
    private const string CatalogDragFormat = "VitanCut.CatalogProductIds";
    private Border? _catalogDropHighlight;
    private readonly Dictionary<DependencyProperty, object> _catalogDropOriginalValues = [];

    private void HighlightCatalogDropTarget(ListViewItem? target)
    {
        var surface = target is null ? null : FindCatalogDropSurface(target);
        if (ReferenceEquals(surface, _catalogDropHighlight)) return;
        if (_catalogDropHighlight is { } previous)
            foreach (var (property, value) in _catalogDropOriginalValues)
                if (value == DependencyProperty.UnsetValue) previous.ClearValue(property);
                else previous.SetValue(property, value);
        _catalogDropOriginalValues.Clear();
        _catalogDropHighlight = surface;
        if (surface is null) return;
        foreach (var property in new[] { Border.BorderBrushProperty, Border.BackgroundProperty })
            _catalogDropOriginalValues[property] = surface.ReadLocalValue(property);
        var accent = (SolidColorBrush)Application.Current.Resources["AppAccentBrush"];
        surface.BorderBrush = accent;
        var color = accent.Color;
        color.A = 80;
        surface.Background = new SolidColorBrush(color);
    }

    private static Border? FindCatalogDropSurface(DependencyObject root)
    {
        if (root is Border { Tag: "catalog-drop-surface" } border) return border;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindCatalogDropSurface(VisualTreeHelper.GetChild(root, index)) is { } surface) return surface;
        return null;
    }

    private void CatalogDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs e) => HighlightCatalogDropTarget(null);

    private void CatalogTargetDragLeave(object sender, DragEventArgs e) => HighlightCatalogDropTarget(CatalogDropContainer(e));
    private Flyout CreateProjectCatalogPicker(Project project)
    {
        var flyout = new Flyout();
        var panel = new StackPanel { Spacing = 8, Width = Math.Max(240, Math.Min(400, Root.ActualWidth - 80)) };
        var create = new Button { Content = UiText.Get("Новое изделие"), HorizontalAlignment = HorizontalAlignment.Stretch };
        create.Click += (_, _) => { flyout.Hide(); OpenProductWindow(project, null); };
        panel.Children.Add(create);
        var selectedMode = 0;
        var modeGrid = new Grid { ColumnSpacing = 2 };
        var modeButtons = new List<RadioButton>();
        var modeNames = new[] { "Этот заказчик", "Все заказчики", "По группам" };
        var modeGroupName = "ProjectCatalogView-" + Guid.NewGuid().ToString("N");
        for (var index = 0; index < modeNames.Length; index++)
        {
            modeGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = UiText.Get(modeNames[index]);
            var segment = new RadioButton { Style = (Style)Application.Current.Resources["CatalogSegmentStyle"],
                Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
                MinWidth = 0, MinHeight = 36, GroupName = modeGroupName, Tag = index, HorizontalAlignment = HorizontalAlignment.Stretch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(segment, label);
            Grid.SetColumn(segment, index);
            modeGrid.Children.Add(segment);
            modeButtons.Add(segment);
        }
        panel.Children.Add(new Border { Child = modeGrid, Padding = new Thickness(2), CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["SectionSurfaceBrush"],
            BorderBrush = (Brush)Application.Current.Resources["PanelStrokeBrush"], BorderThickness = new Thickness(1) });
        var body = new StackPanel { Spacing = 4 };
        panel.Children.Add(new ScrollViewer { Content = body, MaxHeight = 340, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        async Task SelectAsync(CustomerProductItem item)
        {
            flyout.Hide();
            try
            {
                var added = App.Projects.AddCatalogProduct(project, item.Product.Id, item.Counterparty);
                FillProjectCard();
                ProductsList.SelectedItem = added;
                ProductsList.ScrollIntoView(added);
                ShowSuccess(UiText.Format($"Сохранено: {added.Name}"));
            }
            catch (Exception error) { await _dialogs.ShowMessageAsync(UiText.Get("Не удалось сохранить"), error.Message); }
        }
        ListView MakeList(IEnumerable<CustomerProductItem> products)
        {
            var list = new ListView { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true };
            ScrollViewer.SetVerticalScrollMode(list, ScrollMode.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            foreach (var item in products)
            {
                var content = new Grid { ColumnSpacing = 10, Tag = item };
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var image = new Image { Width = 40, Height = 40, Tag = item.Product.Image, Stretch = Stretch.Uniform };
                image.Loaded += ProductThumbnailLoaded;
                content.Children.Add(image);
                var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 3 };
                labels.Children.Add(new TextBlock { Text = item.Product.Name, TextWrapping = TextWrapping.Wrap });
                labels.Children.Add(new TextBlock { Text = item.Counterparty, Opacity = .7, TextWrapping = TextWrapping.Wrap });
                Grid.SetColumn(labels, 1);
                content.Children.Add(labels);
                var info = new Button { Style = (Style)Application.Current.Resources["CatalogInfoButtonStyle"], DataContext = item, VerticalAlignment = VerticalAlignment.Center };
                ToolTipService.SetToolTip(info, UiText.Get("Справка об изделии"));
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(info, UiText.Get("Справка об изделии"));
                info.Click += (_, args) => { flyout.Hide(); CatalogProductInfoClick(info, args); };
                Grid.SetColumn(info, 2);
                content.Children.Add(info);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(content, item.Product.Name);
                list.Items.Add(content);
            }
            list.ItemClick += async (_, args) => { if (args.ClickedItem is FrameworkElement { Tag: CustomerProductItem item }) await SelectAsync(item); };
            list.KeyDown += async (_, args) =>
            {
                if (args.Key != Windows.System.VirtualKey.Enter || list.SelectedItem is not FrameworkElement { Tag: CustomerProductItem item }) return;
                args.Handled = true;
                await SelectAsync(item);
            };
            return list;
        }
        void Render()
        {
            body.Children.Clear();
            var products = App.Catalog.Browse();
            if (selectedMode == 0)
            {
                var own = products.Where(item => item.Counterparty == project.Counterparty).ToList();
                body.Children.Add(MakeList(own));
                if (own.Count == 0) body.Children.Add(new TextBlock { Text = UiText.Get("Изделий у этого заказчика пока нет."), TextWrapping = TextWrapping.Wrap });
                return;
            }
            foreach (var group in products.GroupBy(item => selectedMode == 1 ? item.Counterparty : item.Product.Group?.Trim() ?? "", StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(group => group.Key.Length == 0).ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var expander = new Expander { Header = group.Key.Length == 0 ? UiText.Get("Без группы") : group.Key,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                expander.Expanding += (_, _) => expander.Content ??= MakeList(group);
                body.Children.Add(expander);
            }
        }
        foreach (var segment in modeButtons)
            segment.Checked += (_, _) => { selectedMode = (int)segment.Tag; Render(); };
        modeButtons[0].IsChecked = true;
        flyout.Content = panel;
        return flyout;
    }

    private void CatalogViewChanged(object sender, RoutedEventArgs e)
    {
        if (_catalogPage is null) return;
        _catalogPage.GroupMode = CatalogByGroup.IsChecked == true;
        CatalogSidebarTitle.Text = UiText.Get(_catalogPage.GroupMode ? "Группы" : "Заказчики");
        CatalogCustomerActions.Visibility = _catalogPage.GroupMode ? Visibility.Collapsed : Visibility.Visible;
        CreateCatalogGroupButton.Visibility = _catalogPage.GroupMode ? Visibility.Visible : Visibility.Collapsed;
        _catalogPage.Refresh();
    }

    private void CatalogSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_catalogPage is null) return;
        _catalogPage.SearchText = CatalogSearchBox.Text;
        var searching = !string.IsNullOrWhiteSpace(CatalogSearchBox.Text);
        CatalogSidebar.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(CatalogProductsPane, searching ? 0 : 1);
        Grid.SetColumnSpan(CatalogProductsPane, searching ? 2 : 1);
        _catalogPage.RenderProducts();
    }

    private List<CustomerProductItem> SelectedCatalogProducts(CustomerProductItem? clicked = null)
    {
        var selected = CustomerProjectsList.SelectedItems.OfType<CustomerProductItem>().ToList();
        if (clicked is not null && !selected.Any(item => item.Product.Id == clicked.Product.Id)) return [clicked];
        return selected;
    }

    private async Task AssignCatalogGroupAsync(CustomerProductItem clicked, bool remove)
    {
        var selected = SelectedCatalogProducts(clicked);
        var group = "";
        if (!remove)
        {
            var groups = new ComboBox { IsEditable = true, ItemsSource = App.Catalog.GetProductGroups().Where(name => name.Length > 0).ToList(),
                Text = clicked.Product.Group, Header = UiText.Get("Название группы"), PlaceholderText = UiText.Get("Название группы"),
                HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 280 };
            var create = new Button { Content = UiText.Get("Создать группу") };
            var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            create.Click += (_, _) =>
            {
                try
                {
                    var name = App.Catalog.CreateProductGroup(groups.Text);
                    groups.ItemsSource = App.Catalog.GetProductGroups().Where(groupName => groupName.Length > 0).ToList();
                    groups.SelectedItem = name;
                    groups.Text = name;
                    errorText.Visibility = Visibility.Collapsed;
                    _catalogPage.Refresh();
                }
                catch (Exception error) { errorText.Text = error.Message; errorText.Visibility = Visibility.Visible; }
            };
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(groups);
            content.Children.Add(create);
            content.Children.Add(errorText);
            var result = await new ContentDialog { XamlRoot = Root.XamlRoot, Title = UiText.Get("Добавить в группу"), Content = content,
                PrimaryButtonText = UiText.Get("Добавить"), CloseButtonText = UiText.Get("Отмена"), RequestedTheme = Root.ActualTheme }.ShowAsync();
            if (result != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(groups.Text)) return;
            group = groups.Text;
        }
        if (string.Equals(group.Trim(), UiText.Get("Без группы"), StringComparison.CurrentCultureIgnoreCase)) group = "";
        try
        {
            App.Catalog.SetProductGroup(selected.Select(item => item.Product.Id), group);
            _catalogPage.Refresh();
        }
        catch (Exception error) { await _dialogs.ShowMessageAsync(UiText.Get("Не удалось сохранить"), error.Message); }
    }

    private async void CreateCatalogGroupClick(object sender, RoutedEventArgs e)
    {
        var name = new TextBox { PlaceholderText = UiText.Get("Название группы"), MinWidth = 280 };
        var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(name);
        content.Children.Add(errorText);
        string? created = null;
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = UiText.Get("Создать группу"), Content = content,
            PrimaryButtonText = UiText.Get("Создать"), CloseButtonText = UiText.Get("Отмена"), RequestedTheme = Root.ActualTheme };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { created = App.Catalog.CreateProductGroup(name.Text); }
            catch (Exception error) { args.Cancel = true; errorText.Text = error.Message; errorText.Visibility = Visibility.Visible; }
        };
        await dialog.ShowAsync();
        if (created is not null) _catalogPage.Refresh(created);
    }

    private async void CatalogProductInfoClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: CustomerProductItem item }) return;
        var content = new StackPanel { Spacing = 12, MinWidth = 320, MaxWidth = 620 };
        var image = new Image { Height = 260, Stretch = Stretch.Uniform };
        await ImagePreviewService.SetVisibleAsync(image, item.Product.Image);
        content.Children.Add(image);
        var group = string.IsNullOrWhiteSpace(item.Product.Group) ? UiText.Get("Без группы") : item.Product.Group;
        var created = item.Product.CreatedAt == DateTimeOffset.MinValue ? UiText.Get("Неизвестно") : item.Product.CreatedAt.ToLocalTime().ToString("d");
        content.Children.Add(new TextBlock { Text = $"{UiText.Get("Заказчик")}: {item.Counterparty}\n{UiText.Get("Группа")}: {group}\n{UiText.Get("Дата создания")}: {created}", TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = UiText.Get("Деталировка"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var detail in item.Product.Details.Take(12))
        {
            var calculation = Calculator.Detail(App.State, item.Product, detail);
            content.Children.Add(new TextBlock { Text = $"{detail.Name} · {calculation.Material?.Name} · {Calculator.Number(calculation.Length)} × {Calculator.Number(calculation.Width)} · {Calculator.Number(detail.Qty)} {UiText.Get("шт.")}", TextWrapping = TextWrapping.Wrap });
        }
        if (item.Product.Details.Count > 12)
            content.Children.Add(new TextBlock { Text = UiText.Format($"Ещё деталей: {item.Product.Details.Count - 12}") });
        var projects = App.Catalog.UsingProjects(item.Product);
        content.Children.Add(new TextBlock { Text = UiText.Get("Используется в проектах"), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(new TextBlock { Text = projects.Count == 0 ? UiText.Get("Проекты не найдены") : string.Join("\n", projects.Select(project => project.Name)), TextWrapping = TextWrapping.Wrap });
        await new ContentDialog { XamlRoot = Root.XamlRoot, Title = item.Product.Name,
            Content = new ScrollViewer { Content = content, MaxHeight = Math.Max(200, Root.ActualHeight - 220) },
            CloseButtonText = UiText.Get("Понятно"), RequestedTheme = Root.ActualTheme }.ShowAsync();
    }

    private void CatalogDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        var ids = e.Items.OfType<CustomerProductItem>().Select(item => item.Product.Id).ToArray();
        e.Cancel = ids.Length == 0;
        if (!e.Cancel) e.Data.SetData(CatalogDragFormat, System.Text.Json.JsonSerializer.Serialize(ids));
        e.Data.RequestedOperation = _catalogPage.GroupMode ? DataPackageOperation.Move : DataPackageOperation.Copy | DataPackageOperation.Move;
    }

    private ListViewItem? CatalogDropContainer(DragEventArgs args)
    {
        foreach (var hit in VisualTreeHelper.FindElementsInHostCoordinates(args.GetPosition(Root), CustomersList))
            for (var node = hit as DependencyObject; node is not null && node != CustomersList; node = VisualTreeHelper.GetParent(node))
                if (node is ListViewItem { Content: string } target) return target;
        return null;
    }

    private void CatalogTargetDragOver(object sender, DragEventArgs e)
    {
        var target = e.DataView.Contains(CatalogDragFormat) ? CatalogDropContainer(e) : null;
        HighlightCatalogDropTarget(target);
        if (target is not null)
        { e.AcceptedOperation = _catalogPage.GroupMode ? DataPackageOperation.Move : DataPackageOperation.Copy; e.Handled = true; }
    }

    private async void CatalogTargetDrop(object sender, DragEventArgs e)
    {
        var target = CatalogDropContainer(e)?.Content as string;
        HighlightCatalogDropTarget(null);
        if (target is null || !e.DataView.Contains(CatalogDragFormat)) return;
        e.Handled = true;
        var deferral = e.GetDeferral();
        try
        {
            if (await e.DataView.GetDataAsync(CatalogDragFormat) is not string payload) return;
            var ids = System.Text.Json.JsonSerializer.Deserialize<string[]>(payload);
            if (ids is not { Length: > 0 }) return;
            if (_catalogPage.GroupMode)
                App.Catalog.SetProductGroup(ids, target == UiText.Get("Без группы") ? "" : target);
            else
            {
                var choice = await new ContentDialog { XamlRoot = Root.XamlRoot, Title = UiText.Get("Копировать или переместить?"), Content = target,
                    PrimaryButtonText = UiText.Get("Копировать"), SecondaryButtonText = UiText.Get("Переместить"), CloseButtonText = UiText.Get("Отмена"), RequestedTheme = Root.ActualTheme }.ShowAsync();
                if (choice == ContentDialogResult.None) return;
                App.Catalog.TransferProducts(ids, target, choice == ContentDialogResult.Primary);
            }
            _catalogPage.Refresh(target);
        }
        catch (Exception error) { await _dialogs.ShowMessageAsync(UiText.Get("Не удалось сохранить"), error.Message); }
        finally { deferral.Complete(); }
    }
}
