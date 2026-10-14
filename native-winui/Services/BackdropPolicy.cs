namespace VitanCut.WinUI.Services;

internal static class BackdropPolicy
{
    public static bool UseAcrylic(bool requested, bool advancedEffects, bool highContrast) =>
        requested && advancedEffects && !highContrast;
}
