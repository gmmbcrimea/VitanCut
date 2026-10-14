using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public static class DetailRotation
{
    public static bool IsLocked(Detail detail, Material? material) =>
        material?.Unit == "m2" && !detail.AllowRotation;

    public static bool CanRotate(Detail detail, Material? material) =>
        material?.Unit == "m2" && !IsLocked(detail, material);

    public static string Reason(Detail detail, Material? material) =>
        !detail.AllowRotation ? "Поворот детали запрещён" : "Разрешить поворот детали";
}
