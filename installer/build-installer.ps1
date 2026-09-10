param(
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$vsDevCmd = "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\Common7\Tools\VsDevCmd.bat"
if (-not (Test-Path $vsDevCmd)) { throw "Не найдена Visual Studio Build Tools." }

$root = Split-Path -Parent $PSScriptRoot
$output = Join-Path $root "dist\installer"
New-Item -ItemType Directory -Force -Path $output | Out-Null
& cmd.exe /d /c "call `"$vsDevCmd`" -arch=x64 -host_arch=x64 && rc /nologo /fo `"$PSScriptRoot\resources.res`" `"$PSScriptRoot\resources.rc`" && cl /nologo /std:c++17 /O1 /MT /EHsc /DUNICODE /D_UNICODE `"$PSScriptRoot\main.cpp`" `"$PSScriptRoot\resources.res`" /Fe`"$output\VitanCut.Setup.exe`" /link /SUBSYSTEM:WINDOWS user32.lib gdi32.lib comctl32.lib ole32.lib oleaut32.lib shell32.lib winhttp.lib"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Remove-Item "$PSScriptRoot\resources.res" -Force -ErrorAction SilentlyContinue
Write-Host "Installer created: $(Join-Path $output 'VitanCut.Setup.exe')"
