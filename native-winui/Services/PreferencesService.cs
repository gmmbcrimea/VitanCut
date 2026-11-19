using Microsoft.UI.Xaml;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public sealed class PreferencesService(AppState state)
{
    public AppPreferences Current => state.Database.Preferences;

    public event EventHandler? Changed;

    public void SetLanguage(string language) => Commit(() => Current.Language = UiText.NormalizeLanguage(language));
    public void SetMaterialPickerOrder(string order) =>
        Commit(() => Current.MaterialPickerOrder = MaterialPickerHistory.NormalizeOrder(order));

    public void SetTheme(string theme)
    {
        Commit(() => Current.Theme = theme is "light" or "dark" ? theme : "auto");
    }

    public void SetAccent(string accent)
    {
        Commit(() => Current.AccentColor = AccentPalette.Find(accent).Id);
    }

    public void SetCompactMode(bool value)
    {
        Commit(() => Current.CompactMode = value);
    }

    public void SetAnimationsEnabled(bool value)
    {
        Commit(() => Current.AnimationsEnabled = value);
    }

    public void SetInterfaceScale(string scale) => Commit(() => Current.InterfaceScale = InterfaceOptions.Scale(scale));
    public void SetMoreTransparency(bool value) => Commit(() => Current.MoreTransparency = value);
    public void SetAnimationSpeed(double speed) => Commit(() => Current.AnimationSpeed = InterfaceOptions.Speed(speed));
    public void SetCursorRevealRadius(double radius) => Commit(() => Current.CursorRevealRadius = InterfaceOptions.Radius(radius));
    public void SetCutFileNameFormat(string format) => Commit(() => Current.CutFileNameFormat = InterfaceOptions.FileNameFormat(format));
    public void SetDetailingFileNameFormat(string format) => Commit(() => Current.DetailingFileNameFormat = InterfaceOptions.FileNameFormat(format));
    public void SetRemainderColor(string color) => Commit(() => Current.RemainderColor = InterfaceOptions.Remainder(Current.CutPalette, color));
    public void SetContrastCutLines(bool value) => Commit(() => Current.ContrastCutLines = value);

    public void SetAcrylicBackgroundEnabled(bool value)
    {
        Commit(() => Current.AcrylicBackgroundEnabled = value);
    }

    public void SetCursorRevealEnabled(bool value)
    {
        Commit(() => Current.CursorRevealEnabled = value);
    }

    public void SetCutPalette(string palette)
    {
        Commit(() =>
        {
            Current.CutPalette = palette is "green" or "yellow" or "orange" or "purple" ? palette : "blue";
            Current.RemainderColor = InterfaceOptions.Remainder(Current.CutPalette, Current.RemainderColor);
        });
    }


    public void SetCutLayoutOptions(double trim, double gap)
    {
        if (!double.IsFinite(trim) || !double.IsFinite(gap)) return;
        Commit(() =>
        {
            Current.CutTrim = Math.Max(0, trim);
            Current.CutGap = Math.Max(0, gap);
        });
    }
    public void SetCutUsefulRemainder(double minimum)
    {
        if (!double.IsFinite(minimum)) return;
        Commit(() => Current.CutUsefulRemainder = Math.Max(0, minimum));
    }
    public void SetCloudPublishIntervalMinutes(int minutes)
    {
        Commit(() => Current.CloudPublishIntervalMinutes = minutes is 5 or 15 or 30 or 60 ? minutes : 15);
    }
    public void SetCutExportFormat(string format)
    {
        Commit(() => Current.CutExportFormat = format == "xlsx" ? "xlsx" : "pdf");
    }

    public void SetDetailingIncludeImages(bool value)
    {
        Commit(() => Current.DetailingIncludeImages = value);
    }
    public void SetDetailingExportFormat(string format)
    {
        Commit(() => Current.DetailingExportFormat = format == "xlsx" ? "xlsx" : "pdf");
    }
    public void Apply(Window window) => ThemeService.Apply(window, Current);

    private void Commit(Action mutation)
    {
        state.CommitMutation(mutation);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
