using Microsoft.UI.Xaml;
using VitanCut.WinUI.Models;

namespace VitanCut.WinUI.Services;

public sealed class PreferencesService(AppState state)
{
    public AppPreferences Current => state.Database.Preferences;

    public event EventHandler? Changed;

    public void SetTheme(string theme)
    {
        Current.Theme = theme is "light" or "dark" ? theme : "auto";
        Commit();
    }

    public void SetAccent(string accent)
    {
        Current.AccentColor = AccentPalette.Find(accent).Id;
        Commit();
    }

    public void SetCompactMode(bool value)
    {
        Current.CompactMode = value;
        Commit();
    }

    public void SetAnimationsEnabled(bool value)
    {
        Current.AnimationsEnabled = value;
        Commit();
    }

    public void SetCursorRevealEnabled(bool value)
    {
        Current.CursorRevealEnabled = value;
        Commit();
    }

    public void SetCutPalette(string palette)
    {
        Current.CutPalette = palette is "green" or "yellow" or "orange" or "purple" ? palette : "blue";
        Commit();
    }


    public void SetCutLayoutOptions(double trim, double gap)
    {
        if (!double.IsFinite(trim) || !double.IsFinite(gap)) return;
        Current.CutTrim = Math.Max(0, trim);
        Current.CutGap = Math.Max(0, gap);
        Commit();
    }
    public void SetCutUsefulRemainder(double minimum)
    {
        if (!double.IsFinite(minimum)) return;
        Current.CutUsefulRemainder = Math.Max(0, minimum);
        Commit();
    }
    public void SetCloudPublishIntervalMinutes(int minutes)
    {
        Current.CloudPublishIntervalMinutes = minutes is 5 or 15 or 30 or 60 ? minutes : 15;
        Commit();
    }
    public void SetCutExportFormat(string format)
    {
        Current.CutExportFormat = format == "xlsx" ? "xlsx" : "pdf";
        Commit();
    }

    public void SetDetailingIncludeImages(bool value)
    {
        Current.DetailingIncludeImages = value;
        Commit();
    }
    public void SetDetailingExportFormat(string format)
    {
        Current.DetailingExportFormat = format == "xlsx" ? "xlsx" : "pdf";
        Commit();
    }
    public void Apply(Window window) => ThemeService.Apply(window, Current);

    private void Commit()
    {
        state.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
