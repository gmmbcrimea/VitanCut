# GitHub Releases

Vitan-Cut reads the newest GitHub Release on startup. The release tag must be a version such as `v1.0.1`; a portable asset whose name includes `portable` and ends in `.zip` is required for one-click updates.

## Publish a version

1. Raise `Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion` together in `VitanCut.WinUI.csproj`.
2. Add `release-notes/vX.Y.Z.md` with only the changes in that version. The release workflow uses this file verbatim and does not generate a cumulative changelog.
3. Run `powershell -ExecutionPolicy Bypass -File native-winui/package-portable.ps1 -Version X.Y.Z`.
4. Push the matching tag, for example `git push origin vX.Y.Z`. GitHub Actions builds the archive and creates the public GitHub Release automatically.

The application checks `https://github.com/gmmbcrimea/VitanCut/releases`. If the repository is moved, change `RepositoryOwner` and `RepositoryName` in `Services/AppUpdateService.cs` before publishing the next version.
