# GitHub Releases

Vitan-Cut reads the newest GitHub Release on startup. The release tag must be a version such as `v1.0.1`; a portable asset whose name includes `portable` and ends in `.zip` is required for one-click updates.

## Publish a version

1. Raise `Version`, `AssemblyVersion`, `FileVersion`, and `InformationalVersion` together in `VitanCut.WinUI.csproj`.
2. Run `powershell -ExecutionPolicy Bypass -File native-winui/package-portable.ps1 -Version 1.0.1`.
3. Create a public GitHub Release with tag `v1.0.1` and upload `dist/VitanCut-portable-win-x64-1.0.1.zip`.

The application checks `https://github.com/gmmbcrimea/Vitan-K-Furniture-App/releases`. If the repository is moved, change `RepositoryOwner` and `RepositoryName` in `Services/AppUpdateService.cs` before publishing the next version.
