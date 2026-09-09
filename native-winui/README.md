# Vitan-Cut WinUI

This is the new primary native Windows path for Vitan-Cut:

- WinUI 3
- Windows App SDK
- XAML windows and controls
- Separate task windows for projects and materials

## Upstream References

- Windows App SDK: https://github.com/microsoft/windowsappsdk
- WinUI Gallery: https://github.com/microsoft/WinUI-Gallery

Use `microsoft/windowsappsdk` for platform direction, package/runtime guidance, and Windows App SDK APIs. Use `microsoft/WinUI-Gallery` for design patterns, Fluent control usage, adaptive layouts, accessibility examples, and visual assets where their license allows reuse.

Current design baseline from WinUI Gallery:

- Fluent surfaces, 8px panel radius, theme resources, and `InfoBar`/`NavigationView`/`CommandBar` patterns.
- Icon-first command buttons for add/open/delete/save/settings.
- Separate task windows for settings, materials, catalog, and project editing instead of forcing all workflows into the main window.

The previous `native/` Win32/GDI app remains as a temporary prototype and logic donor.

## Build

Install the Visual Studio workload:

```text
Windows application development
```

Make sure the Windows App SDK / WinUI tooling is installed. The project currently pins `Microsoft.WindowsAppSDK` `1.8.250916003` because it starts reliably on the current machine; do not switch back to floating `2.*` until we explicitly validate runtime startup. Then run:

```cmd
native-winui\build-release.cmd
```

If this is the first build, Visual Studio or `dotnet` will restore `Microsoft.WindowsAppSDK` from NuGet.

Run the release build:

```cmd
native-winui\run-release.cmd
```

## Current Scope

See [QA-2026-09-07.md](QA-2026-09-07.md) for the current verified changes, regression checks and known limitations. The historical feature list below is not a test-completion checklist.

Run the core regression checks from the repository root:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.dotnet-home'
dotnet run --project native-winui-tests/VitanCut.CoreChecks.csproj
```

The checks use a temporary database. Set `VITANCUT_DATABASE_PATH` in the app process environment to use a separate database for UI testing. Successful saves retain the previous file as `database.json.bak`; failed database reads never overwrite the original.

- Main projects dashboard matching the requested layout: top bar, left projects column, right project/empty state column.
- Project sorting by created date, updated date, name, or counterparty.
- Counterparty visibility filters in the projects column.
- Add-project dialog.
- Separate project editor window.
- Separate materials editor window.
- Separate settings window with appearance, theme, accent color, cutting, and detailing sections.
- Separate catalog window placeholder.
- JSON database compatibility with the existing Electron app.
- Ported basic totals and detail-cost calculations.

## Direction

Use Microsoft Learn Windows desktop documentation as the baseline:

- https://learn.microsoft.com/en-us/windows/apps/desktop/
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/
- https://learn.microsoft.com/en-us/windows/apps/winui/
- https://learn.microsoft.com/en-us/windows/apps/design/
