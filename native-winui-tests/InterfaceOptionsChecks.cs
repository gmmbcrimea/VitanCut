using System.Text.Json;
using VitanCut.WinUI.Models;
using VitanCut.WinUI.Services;

internal static class InterfaceOptionsChecks
{
    internal static void Run(Action<bool, string> check, string output)
    {
        var preferences = new AppPreferences();
        check(preferences.InterfaceScale == "normal" && preferences.CompactMode && !preferences.MoreTransparency &&
            preferences.AnimationSpeed == 1 && preferences.CursorRevealRadius == 1 && !preferences.ContrastCutLines,
            "New interface options default to normal density, standard effects and no extra transparency");
        check(InterfaceOptions.ControlHeight("compact") == 28 && InterfaceOptions.ControlHeight("normal") == 32 &&
            InterfaceOptions.ControlHeight("large") == 40 &&
            InterfaceOptions.Inset(24, "compact") < InterfaceOptions.Inset(24, "normal") &&
            InterfaceOptions.Inset(24, "normal") < InterfaceOptions.Inset(24, "large") &&
            InterfaceOptions.Gap(0, "compact") == 0,
            "Three density levels reduce padding before changing the size of controls");
        check(JsonSerializer.Deserialize<AppPreferences>("{\"CompactMode\":true}")!.InterfaceScale == "normal" &&
            JsonSerializer.Deserialize<AppPreferences>("{\"CompactMode\":false}")!.InterfaceScale == "large",
            "Legacy compact and regular preferences map to normal and large density");
        foreach (var scale in new[] { "compact", "normal", "large" })
            check(InterfaceOptions.NavigationHeight(scale) >= 20 + 16 + 10 + InterfaceOptions.NavigationGap(scale) &&
                InterfaceOptions.NavigationHeight(scale) > InterfaceOptions.ControlHeight(scale),
                "Navigation reserves space for the icon, label, borders and padding rather than using input height: " + scale);
        preferences.InterfaceScale = "compact";
        check(JsonSerializer.Deserialize<AppPreferences>(JsonSerializer.Serialize(preferences))!.InterfaceScale == "compact",
            "The densest scale survives legacy CompactMode compatibility serialization");
        foreach (var speed in new[] { .25, .5, 1, 2 })
            check(InterfaceOptions.Duration(TimeSpan.FromMilliseconds(180), speed).TotalMilliseconds == 180 / speed,
                "Animation speed changes transition time rather than operation timing: " + speed);
        check(InterfaceOptions.Speed(0) == 1 && InterfaceOptions.Radius(double.NaN) == 1 &&
            InterfaceOptions.Scale(null) == "normal", "Malformed appearance options safely return to defaults");
        check(InterfaceOptions.Remainder("green", "green") == "red" && InterfaceOptions.Remainder("blue", "green") == "green" &&
            CutColors.RemainderFill("green", "green") == CutColors.RemainderFill("blue", "red") &&
            CutColors.CutLine(false) == "#596575" && CutColors.CutLine(true) == "#C62828",
            "Useful remainders contrast with green sheets and cut lines have distinct grey and red modes");

        var date = new DateTime(2026, 11, 19, 12, 30, 0);
        var expected = new Dictionary<string, string>
        {
            ["project-address-date"] = "Report_Project_Street_2026-11-19_12-30",
            ["project-date"] = "Report_Project_2026-11-19_12-30",
            ["address-date"] = "Report_Street_2026-11-19_12-30",
            ["project-address"] = "Report_Project_Street"
        };
        foreach (var (format, name) in expected)
            check(ReportFileName.Create("Report", "Project", date, "Street", format) == name,
                "Export filename respects the selected fields: " + format);
        check(ReportFileName.Create("Report", "Project", date, "", "address-date") == expected["project-date"] &&
            !ReportFileName.Create("Report", "Project", date, "Street: 1/2", "project-address").Contains('/'),
            "Report filenames handle missing addresses and sanitize address characters");
        check(ReportFileName.Create("Report", new string('a', 300), date, new string('b', 300), "project-address-date").Length < 240,
            "Combined project and address names remain within the Windows filename budget");
        var notes = AppUpdateService.SummarizeReleaseNotes("- First change\n- Second change\n- Third change", "v1");
        check(notes == "First change\nSecond change\nThird change", "Update notes display one change per line");

        var state = new AppState();
        var path = Path.Combine(output, "interface-options.json");
        state.Load(path);
        preferences.MoreTransparency = true;
        preferences.AnimationSpeed = .5;
        preferences.CursorRevealRadius = 2;
        preferences.CutFileNameFormat = "project-address";
        preferences.DetailingFileNameFormat = "address-date";
        preferences.ContrastCutLines = true;
        preferences.RemainderColor = "red";
        state.Database.Preferences = preferences;
        state.Save();
        var restored = new AppState();
        restored.Load(path);
        var actual = restored.Database.Preferences;
        check(actual.InterfaceScale == "compact" && actual.AnimationSpeed == .5 && actual.CursorRevealRadius == 2 &&
            actual.MoreTransparency && actual.ContrastCutLines && actual.CutFileNameFormat == "project-address" &&
            actual.DetailingFileNameFormat == "address-date", "Appearance and export options persist across restarts");
        restored.ReplaceFromCloudSnapshot(JsonSerializer.Serialize(new Database()));
        actual = restored.Database.Preferences;
        check(actual.InterfaceScale == "compact" && actual.MoreTransparency && actual.AnimationSpeed == .5 &&
            actual.CursorRevealRadius == 2 && actual.ContrastCutLines, "Cloud replacement keeps local interface preferences");
    }
}
