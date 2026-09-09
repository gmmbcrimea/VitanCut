# Glass Workspace

## Direction

The second design pass changes the application shell, not its business model.
The previous horizontal tabs become a 104 px navigation rail with the existing
logo, native icons, labels and tooltips. Settings remain available at the bottom.
Projects retain their list/detail relationship; commands sit below the project
heading, leaving long names room to wrap.

Data surfaces are deliberately quiet. Full-height sections replace nested rounded
frames, settings use separators, and tables retain their existing columns and
editing controls. The total has a saturated accent surface; area, materials and
payroll use restrained mint, neutral and amber markers. The user's chosen accent
and system/light/dark preference are preserved.

Contrast refinement: the shell is darker than working panes in dark mode and
cool gray around white panes in light mode. Settings sections and list wells have
distinct solid fills; borders and muted labels are stronger. The layout and
effects budget are unchanged. Both theme variants were inspected after this pass.

Rounded-surface refinement: working panes and settings sections now have 8 px
corners and continuous borders; list wells use 6 px corners. A static glass
gradient gives panes depth without adding new blur or animation. The navigation
and data layout remain unchanged.

Accent options share one palette used by preferences, both settings views and
the theme renderer. Teal, cyan, violet, pink, coral and amber supplement the
existing blue, green, graphite and system choices. The picker shows swatches.
Foreground selection evaluates both ends of the accent gradient. The check suite
now has 105 passing checks, including every accent's persistence and a minimum
4.5:1 foreground contrast across each predefined accent gradient.

Potential future appearance settings (proposals, not implemented): three glass
intensity presets, normal/strong section contrast, and an independent readable
text scale. Keep these separate from accent color and compact spacing.

Shell junction refinement: the title bar and navigation now share one acrylic
root background. The rail's vertical divider is removed, and the workspace has
an 8 px top gutter (4 px in compact mode), keeping pane edges clear of the header.

The appearance toggle "Подсветка под курсором" controls a native cursor reveal.
It defaults on, applies immediately and persists across restarts. One 160 px
Composition radial gradient follows the pointer on supported surfaces, clipped
to their bounds and rounded corners. Light mode uses a subtle accent tint;
dark mode uses a soft neutral light. Cutting canvases are excluded.
Handlers are removed when disabled; scrolling, dragging, unloading or window
deactivation clears the effect. System advanced-effects and high-contrast
settings suppress it. No timer, per-row blur or custom shader is used.

Cursor-host regression fix: a SpriteVisual is now created for each active host
and hidden, detached and disposed on departure. Reusing one handed-in visual
between XAML hosts reproduced a stale attachment: the spot used a list row's
coordinates while still drawing on the previous pane. Brush and clip resources
remain shared within the window; only one sprite is active, with no allocations
for movement inside the same element.

Native regression scenario: hover the right project pane, a left project row,
another point in that row, the right pane again, then a navigation button.
At every transition the old spot must disappear and the new spot must be clipped
to the current host. Repeat after toggling the effect off and on. This scenario
was reproduced before the fix and visually verified after it; core tests alone
do not exercise XAML composition-host ownership.

## Implementation

- `App.xaml`: shared surfaces, typography, command styles and navigation states.
- `ThemeService.cs`: live brush updates, OS effects fallback and density settings.
- `CursorReveal.cs`: shared pointer illumination and window-lifetime cleanup.
- `CommandAppearance.cs`: native icons for existing named commands; events unchanged.
- `MainWindow.xaml`: navigation rail and project presentation.
- `ProjectLayoutController.cs`: metric layout accounts for space occupied by navigation.
- Child windows share the glass background and acrylic command bars.
- Segoe UI is used as a single native font family, without CSS-style fallback syntax.

## Effects Budget

Desktop acrylic supplies the backdrop. In-app acrylic is restricted to navigation
and command bars. Rows and fields do not create independent blur effects.
Reflective edges and the total's depth are static linear gradients. Only the
project sidebar uses a ThemeShadow; no continuous custom animation or refraction
shader was added. Advanced-effects/high-contrast settings select opaque fallback
surfaces and disable the sidebar elevation.

References: [Microsoft acrylic](https://learn.microsoft.com/en-us/windows/apps/develop/ui/in-app-acrylic)
and [acrylic guidance](https://learn.microsoft.com/en-us/windows/apps/design/style/acrylic).
Cursor illumination uses [CompositionRadialGradientBrush](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.compositionradialgradientbrush?view=windows-app-sdk-1.8)
and [CompositionRoundedRectangleGeometry](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.composition.compositionroundedrectanglegeometry?view=windows-app-sdk-1.8).

## Verification

- Release x64 build: successful, no warnings or errors.
- All 108 core checks pass, including formulas, rotation restrictions,
  material validation, database safety, PDF/XLSX exports, accent contrast and
  cursor-reveal preference persistence.
- Application resource keys checked for duplicates.
- Native screenshots inspected at 1426 x 746: project overview, settings,
  light/dark theme switching, green selection states and material categories.
- Product editor opened maximized at 1920 x 1032; columns and command bars inspected.
- UI checks used a temporary copy of the database, not the production database.
- Cursor reveal and its immediate off state were inspected in the native window;
  the unified shell was checked in light and dark modes.
- A startup failure from AccessibilitySettings.HighContrastChanged subscription
  was found and removed. System color/effects events remain the refresh source.
- No GPU benchmark, hardware low-power test, live high-contrast test or full
  minimum-window/DPI matrix was performed. Existing column-width core checks pass.

The initial generated concept was a material reference, not a data or layout
specification. The subsequent request for a more radical redesign superseded its
horizontal navigation. Real branding, product images and existing actions remain;
invented concept data and controls were not copied into the application.
