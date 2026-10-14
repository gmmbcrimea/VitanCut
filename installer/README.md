# VitanCut Installer 1.1.2

Production installer sources live on `codex/installer`, independently of the application on `main`.

Build with Visual Studio C++ tools and the Windows SDK:

```powershell
./installer/build-installer.ps1 -Version 1.1.2
./installer/contract-checks.ps1
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ./installer/contract-checks.ps1
```

Output: `dist/installer/VitanCut.Setup.exe` and its SHA256 checksum. The build rejects executables over 1,000,000 bytes and incorrect versions. Production embeds `app.manifest` and does not define `VITANCUT_UI_ONLY`.

For a non-installing UI preview, build with `-Preview`. Its manifest is `asInvoker`; the primary button only simulates progress. Do not publish this build. Rebuild without `-Preview` to obtain the production executable.

The production installer downloads the latest published stable application archive, not the installer release. JSON is parsed with Windows PowerShell. Selection excludes drafts, prereleases and checksum assets. Publisher is GMMB; the default folder is Documents/VitanCut. Taskbar pinning is best effort under Windows policies.

Installation writes a relative file manifest. Uninstall uses that manifest rather than recursively deleting the chosen folder, preserving `data` and unrelated files. Missing manifests, escaping paths and reparse points stop deletion. Registry/shortcut creation and a real installation have not been run against the developer's working installation during these release checks.

Release tags use `installer-v1.1.2`. The installer workflow checks PowerShell 7 and Windows PowerShell 5.1 and publishes EXE/checksum with `make_latest: false`, leaving the application release as GitHub's latest release.
