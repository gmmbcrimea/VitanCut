# Release validation

The primary repository contains the 1.2.19 sources, including automatic amount conversion when switching between unit and sheet/roll price modes. Local builds are stored in `release/<version>`; previous builds are retained. `scripts/Build-Release.ps1` publishes a self-contained application and checks that its XAML resource index is present. UI-review isolation is not enabled in production builds.

## Validate version metadata

Keep `Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion` in `VitanCut.WinUI.csproj` consistent and add `release-notes/vX.Y.Z.md` containing only that version's changes. Validate them with:

```powershell
./scripts/Validate-Release.ps1 -Version X.Y.Z
```

The workflow `.github/workflows/release.yml` publishes portable archives on version tags with version-specific notes. `.github/workflows/refactor-validation.yml` performs additional checks. No GitHub publication was requested for the local 1.2.0 migration. Installer and live cloud deployment checks remain documented in the historical audit.
