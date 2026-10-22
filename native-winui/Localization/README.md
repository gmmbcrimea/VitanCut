# Interface Localization

Supported interface languages: Russian (source), English, German, Spanish,
and Simplified Chinese. Select a language in Settings > Appearance and restart
the application. The active language stays unchanged until restart so open
editors are not recreated before restart. The confirmation uses the selected
language and offers Restart now or acknowledgment. Restart follows the normal
shutdown/synchronization path; its new process waits for the previous process
to exit before loading the database. Save open work before restarting.

## Data Safety

The language preference is local to this installation. Database import and
cloud restoration preserve the local choice. User names, material categories,
formula expressions and stored business data are not translated. Localization
does not change the numeric culture used by calculations.

The application reads embedded JSON dictionaries offline. It makes no
translation service requests at runtime. Operating-system dialogs and built-in
framework accessibility descriptions may retain the Windows language.

## Maintenance

- Wrap explicit code-generated UI labels with `UiText.Get`; use `UiText.Format`
  for interpolated UI messages, keeping user values as arguments.
- Mark literal XAML properties with `UiLocalization.Properties`. Do not mark
  bindings or user-editable data. Keep language names in their native form.
- Preserve format placeholders, whitespace and formula syntax in dictionaries.
- `scripts/Localize-Ui` is a .NET 9 maintenance tool, not a runtime dependency.
  Its collector/rewriter is intended for reviewable, mechanical source updates.
- `Translate-Catalog.ps1` fills missing static strings through an external
  translation service and applies `overrides.json`. Do not supply user data or
  secrets. Review generated translations, particularly furniture terminology.
- Run `native-winui-tests/VitanCut.CoreChecks.csproj` and build the WinUI app
  after dictionary or localization changes.

## Verification

The localization checks cover dictionary completeness and placeholders,
language persistence, import/cloud preservation, numeric culture, and formula
invariance. Native preview testing covers language selection and restart,
English and German layouts, and Chinese font rendering. These checks do not
replace native-speaker review or visual testing of every dialog and export.
