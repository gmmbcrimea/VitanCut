using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VitanCut.WinUI.Controllers;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;
using Windows.ApplicationModel.DataTransfer;

namespace VitanCut.WinUI;

public sealed partial class MainWindow
{
    private const string MaterialDragFormat = "VitanCut.MaterialGroupMove";
    private sealed record MaterialDrag(string Token, string Category, string[] Ids);
    private MaterialDrag? _materialDrag;
    private Border? _materialDropHighlight;

    private void MaterialDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        var dragged = e.Items.OfType<MaterialChoice>().ToList();
        var category = _materialPage.SelectedCategory;
        e.Cancel = category is null || dragged.Count == 0 || dragged.Any(choice => choice.Type != category);
        _materialDrag = null;
        if (e.Cancel) return;
        var selected = _materialSelection.Any(choice => dragged.Any(item => item.Material.Id == choice.Material.Id))
            ? _materialSelection : dragged;
        if (selected.Any(choice => choice.Type != category)) { e.Cancel = true; return; }
        _materialDrag = new(Guid.NewGuid().ToString("N"), category!, selected.Select(choice => choice.Material.Id).Distinct().ToArray());
        e.Data.SetData(MaterialDragFormat, _materialDrag.Token);
        e.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private bool CanDropMaterial(DragEventArgs e, MaterialGroupView group) =>
        _materialDrag is { } drag && drag.Category == _materialPage.SelectedCategory && e.DataView.Contains(MaterialDragFormat) &&
        group.Items.All(choice => choice.Type == drag.Category) && drag.Ids.Any(id =>
            App.State.FindMaterial(drag.Category, id) is { } material &&
            !string.Equals(material.Group.Trim(), group.Name, StringComparison.CurrentCultureIgnoreCase));

    private void HighlightMaterialGroup(Border? target)
    {
        if (ReferenceEquals(target, _materialDropHighlight)) return;
        if (_materialDropHighlight is { } previous)
        {
            previous.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            previous.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
        _materialDropHighlight = target;
        if (target is null) return;
        var accent = (SolidColorBrush)Application.Current.Resources["AppAccentBrush"];
        target.BorderBrush = accent;
        var tint = accent.Color;
        tint.A = 60;
        target.Background = new SolidColorBrush(tint);
    }

    private void MaterialGroupDragOver(object sender, DragEventArgs e)
    {
        var target = sender as Border;
        var allowed = target?.DataContext is MaterialGroupView group && CanDropMaterial(e, group);
        HighlightMaterialGroup(allowed ? target : null);
        e.AcceptedOperation = allowed ? DataPackageOperation.Move : DataPackageOperation.None;
        e.Handled = true;
    }

    private void MaterialGroupDragLeave(object sender, DragEventArgs e)
    {
        if (sender is Border target && ReferenceEquals(target, _materialDropHighlight))
        {
            var point = e.GetPosition(target);
            if (!UiLayout.ContainsPoint(point.X, point.Y, target.ActualWidth, target.ActualHeight)) HighlightMaterialGroup(null);
        }
    }

    private void MaterialDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs e)
    {
        HighlightMaterialGroup(null);
        _materialDrag = null;
    }

    private async void MaterialGroupDrop(object sender, DragEventArgs e)
    {
        HighlightMaterialGroup(null);
        if (sender is not Border { DataContext: MaterialGroupView group } || !CanDropMaterial(e, group) || _materialDrag is not { } drag) return;
        e.Handled = true;
        var deferral = e.GetDeferral();
        try
        {
            if (await e.DataView.GetDataAsync(MaterialDragFormat) is not string token || token != drag.Token ||
                _materialPage.SelectedCategory != drag.Category) return;
            if (!App.MaterialLibrary.MoveToGroup(drag.Category, drag.Ids, group.Name)) return;
            group.IsExpanded = true;
            var focus = App.MaterialLibrary.GetChoices(drag.Category).FirstOrDefault(choice => drag.Ids.Contains(choice.Material.Id));
            RefreshMaterials(focus);
            ShowSuccess(UiText.Get(group.Name.Length == 0 ? "Материалы исключены из группы" : "Материалы объединены"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            NotificationCenter.Publish(UiText.Get("Не удалось изменить группу"), error.Message);
        }
        finally
        {
            _materialDrag = null;
            deferral.Complete();
        }
    }
}
