using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI;

public sealed partial class MainWindow
{
    private bool _projectWorkListLayoutQueued;
    private bool _updatingProjectQuantity;
    private bool _deletingProjectProducts;

    private void QueueProjectWorkListLayout()
    {
        if (_projectWorkListLayoutQueued) return;
        _projectWorkListLayoutQueued = true;
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _projectWorkListLayoutQueued = false;
            if (ProjectPanel.IsLoaded && ProjectPanel.Visibility == Visibility.Visible) UpdateResponsiveLayout();
        })) _projectWorkListLayoutQueued = false;
    }

    private static bool IsQuantityInput(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is NumberBox) return true;
        return false;
    }

    private void ProjectQuantityBackgroundPressed(object sender, PointerRoutedEventArgs args)
    {
        if (Root.XamlRoot is not { } root) return;
        NumberBox? field = null;
        for (var node = FocusManager.GetFocusedElement(root) as DependencyObject; node is not null;
             node = VisualTreeHelper.GetParent(node))
            if (node is NumberBox { Tag: Product } number) { field = number; break; }
        if (field is null) return;
        var source = args.OriginalSource as DependencyObject;
        for (; source is not null && source != Root; source = VisualTreeHelper.GetParent(source))
            if (source is ButtonBase or TextBox or NumberBox or ComboBox or ToggleSwitch or ScrollBar or Slider)
                return;
        // Input from a popup belongs to its own tree, not to the empty editor background.
        if (source != Root) return;
        NumberBoxInput.Read(field);
        ProjectQuantityFocusTarget.IsTabStop = true;
        if (!ProjectQuantityFocusTarget.Focus(FocusState.Programmatic))
            ProjectQuantityFocusTarget.IsTabStop = false;
    }

    private void ProjectProductQuantityLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not NumberBox { Tag: Product product } box) return;
        SetQuantityField(box, product.Qty);
    }

    private void SetQuantityField(NumberBox box, double quantity)
    {
        _updatingProjectQuantity = true;
        try { box.Value = quantity; }
        finally { _updatingProjectQuantity = false; }
    }

    private void ProjectProductQuantityLostFocus(object sender, RoutedEventArgs args)
    {
        if (sender is not NumberBox { Tag: Product product } box || SelectedProject is not { } project) return;
        if (project.Products.FirstOrDefault(item => item.Id == product.Id) is { } current)
            SetQuantityField(box, current.Qty);
    }

    private void ProjectProductQuantityChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loadingProjects || _updatingProjectQuantity || !sender.IsLoaded ||
            sender.Tag is not Product product || SelectedProject is not { } project || double.IsNaN(sender.Value)) return;
        var current = project.Products.FirstOrDefault(item => item.Id == product.Id);
        if (current is null || current.Qty == sender.Value) return;
        try
        {
            App.Projects.SetProductQuantity(project, product.Id, sender.Value);
            _projectPage.RefreshCalculations(project, PayrollLabel);
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            var restored = App.Projects.Find(project.Id)?.Products.FirstOrDefault(item => item.Id == product.Id);
            SetQuantityField(sender, restored?.Qty ?? current.Qty);
            NotificationCenter.Publish(UiText.Get("Не удалось сохранить"), UiText.Get(error.Message));
        }
    }

    private async Task DeleteSelectedProjectProductsAsync(Project project, IReadOnlyList<Product> selected)
    {
        if (_deletingProjectProducts || selected.Count == 0) return;
        _deletingProjectProducts = true;
        try
        {
            if (selected.Count == 1)
            {
                await DeleteProductAsync(project, selected[0]);
                return;
            }
            if (!await _dialogs.ConfirmDeleteAsync(UiText.Get("Удалить изделия?"),
                UiText.Format($"Удалить выбранные изделия ({selected.Count}) из проекта?"))) return;
            App.Projects.DeleteProducts(project, selected.Select(product => product.Id));
            FillProjectCard();
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowMessageAsync(UiText.Get("Не удалось удалить"), error.Message);
        }
        finally { _deletingProjectProducts = false; }
    }
}
