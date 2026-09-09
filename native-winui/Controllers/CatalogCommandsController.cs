using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Runs counterparty commands with one consistent dialog and confirmation flow.
/// </summary>
public sealed class CatalogCommandsController(CatalogService catalog, DialogService dialogs)
{
    public async Task<string?> AddCounterpartyAsync()
    {
        var name = await dialogs.PromptTextAsync("Добавить заказчика", "Название заказчика", "");
        if (string.IsNullOrWhiteSpace(name)) return null;

        catalog.AddCounterparty(name);
        return name;
    }

    public async Task<string?> RenameCounterpartyAsync(string oldName)
    {
        var newName = await dialogs.PromptTextAsync("Изменить заказчика", "Название заказчика", oldName);
        if (string.IsNullOrWhiteSpace(newName) || string.Equals(oldName, newName, StringComparison.CurrentCultureIgnoreCase))
            return null;

        catalog.RenameCounterparty(oldName, newName);
        return newName;
    }

    public async Task<bool> DeleteCounterpartyAsync(string name)
    {
        var message = $"Проекты и каталог заказчика \"{name}\" будут перенесены в \"Без заказчика\".";
        if (!await dialogs.ConfirmDeleteAsync("Удалить заказчика?", message)) return false;

        catalog.DeleteCounterparty(name);
        return true;
    }
}
