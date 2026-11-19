using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Runs counterparty commands with one consistent dialog and confirmation flow.
/// </summary>
public sealed class CatalogCommandsController(CatalogService catalog, DialogService dialogs)
{
    public async Task<string?> AddCounterpartyAsync()
    {
        if (await dialogs.PromptCounterpartyAsync(null) is not { } edit) return null;
        try { catalog.SaveCounterparty(null, edit.Name, edit.Logo); }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
        { await dialogs.ShowMessageAsync("Не удалось сохранить", error.Message); return null; }
        return edit.Name;
    }

    public async Task<string?> RenameCounterpartyAsync(string oldName)
    {
        if (await dialogs.PromptCounterpartyAsync(oldName) is not { } edit) return null;
        try { catalog.SaveCounterparty(oldName, edit.Name, edit.Logo); }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
        { await dialogs.ShowMessageAsync("Не удалось сохранить", error.Message); return null; }
        return edit.Name;
    }

    public async Task<bool> DeleteCounterpartyAsync(string name)
    {
        var message = $"Проекты и каталог заказчика \"{name}\" будут перенесены в \"Без заказчика\".";
        if (!await dialogs.ConfirmDeleteAsync(UiText.Get("Удалить заказчика?"),message)) return false;

        catalog.DeleteCounterparty(name);
        return true;
    }
}
