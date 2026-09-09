using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

namespace VitanCut.WinUI.Controllers;

/// <summary>
/// Applies the material editor state and owns destructive material commands.
/// </summary>
public sealed class MaterialCommandsController(MaterialService materials, DialogService dialogs)
{
    public MaterialChoice AddFromEditor(MaterialPageController editor)
    {
        var type = editor.EditorGroup;
        var material = new Material { Name = materials.CreateUniqueName("Новый материал") };
        materials.Add(type, material);
        return new MaterialChoice(type, material);
    }

    public async Task<MaterialChoice?> SaveFromEditorAsync(MaterialPageController editor, MaterialChoice choice)
    {
        var proposedName = editor.EditorName;
        if (materials.ExistsName(proposedName, choice.Material))
        {
            await dialogs.ShowMessageAsync("Материал уже существует", $"Материал «{proposedName}» уже есть в базе. Выберите другое название.");
            return null;
        }
        var type = editor.EditorGroup;
        var draft = new Material { Id = choice.Material.Id };
        editor.ApplyEditor(draft);
        try { materials.Validate(draft, choice.Material); }
        catch (InvalidOperationException error)
        {
            await dialogs.ShowMessageAsync("Проверьте материал", error.Message);
            return null;
        }
        var material = choice.Material;
        var previous = new Material { Name = material.Name, Unit = material.Unit, Cost = material.Cost, SheetLength = material.SheetLength, SheetWidth = material.SheetWidth, TextureDirection = material.TextureDirection };
        try
        {
            Copy(draft, material);
            return materials.Save(choice, type);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Copy(previous, material);
            await dialogs.ShowMessageAsync("Не удалось сохранить", error.Message);
            return null;
        }
    }

    private static void Copy(Material source, Material target)
    {
        target.Name = source.Name; target.Unit = source.Unit; target.Cost = source.Cost;
        target.SheetLength = source.SheetLength; target.SheetWidth = source.SheetWidth;
        target.TextureDirection = source.TextureDirection;
    }

    public async Task<bool> DeleteAsync(MaterialChoice choice)
    {
        if (materials.IsUsed(choice))
        {
            await dialogs.ShowMessageAsync("Материал используется", "Материал указан в деталях проекта или каталога. Сначала замените его в этих деталях.");
            return false;
        }
        if (!await dialogs.ConfirmDeleteAsync("Удалить материал?", $"{choice.Type} / {choice.Material.Name}"))
            return false;

        materials.Delete(choice);
        return true;
    }
}
