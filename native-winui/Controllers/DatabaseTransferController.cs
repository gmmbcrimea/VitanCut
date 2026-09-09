using Microsoft.UI.Xaml;
using VitanCut.WinUI.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace VitanCut.WinUI.Controllers;

public sealed record DatabaseTransferResult(bool Imported, bool Succeeded, string Title, string Message);

/// <summary>
/// Owns JSON database transfer and the WinUI file pickers used by that workflow.
/// </summary>
public sealed class DatabaseTransferController(Window owner, AppState state)
{
    public async Task<DatabaseTransferResult?> ExportAsync()
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = $"vitan-cut-base-{DateTime.Now:yyyyMMdd-HHmm}"
        };
        picker.FileTypeChoices.Add("JSON", new List<string> { ".json" });
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));

        var file = await picker.PickSaveFileAsync();
        if (file is null) return null;

        await FileIO.WriteTextAsync(file, state.ExportJson());
        return new DatabaseTransferResult(false, true, "База сохранена", file.Path);
    }

    public async Task<DatabaseTransferResult?> ImportAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".json");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(owner));

        var file = await picker.PickSingleFileAsync();
        if (file is null) return null;

        try
        {
            state.ImportJson(await FileIO.ReadTextAsync(file));
            return new DatabaseTransferResult(true, true, "База импортирована", file.Path);
        }
        catch (Exception exception)
        {
            return new DatabaseTransferResult(false, false, "Не удалось импортировать JSON", exception.Message);
        }
    }
}