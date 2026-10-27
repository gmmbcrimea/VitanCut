param([string]$Version = '1.1.4', [switch]$Preview)

$ErrorActionPreference = 'Stop'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Visual Studio C++ tools not found.' }
$vsDevCmd = Join-Path $installation 'Common7\Tools\VsDevCmd.bat'
$output = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist\installer'
New-Item -ItemType Directory -Force -Path $output | Out-Null
$define = if ($Preview) { '/D VITANCUT_UI_ONLY' } else { '' }
Push-Location $PSScriptRoot
try {
    & cmd.exe /d /c "call `"$vsDevCmd`" -arch=x64 -host_arch=x64 >nul && rc /nologo $define /fo `"$output\resources.res`" resources.rc && cl /nologo /utf-8 /std:c++17 /O1 /MT /EHsc /DUNICODE /D_UNICODE $define main.cpp `"$output\resources.res`" /Fo`"$output\main.obj`" /Fe`"$output\VitanCut.Setup.exe`" /link /SUBSYSTEM:WINDOWS /INCREMENTAL:NO user32.lib gdi32.lib comctl32.lib ole32.lib oleaut32.lib shell32.lib winhttp.lib advapi32.lib"
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
    & cmd.exe /d /c "call `"$vsDevCmd`" -arch=x64 -host_arch=x64 >nul && cl /nologo /utf-8 /std:c++17 /O1 /MT /EHsc layout-checks.cpp /Fo`"$output\layout-checks.obj`" /Fe`"$output\layout-checks.exe`" /link /INCREMENTAL:NO"
    if ($LASTEXITCODE -ne 0) { throw 'Layout test build failed.' }
    & "$output\layout-checks.exe"
    if ($LASTEXITCODE -ne 0) { throw 'Layout checks failed.' }
    $exe = Get-Item -LiteralPath "$output\VitanCut.Setup.exe"
    if ($exe.Length -gt 1000000) { throw "Installer exceeds 1 MB: $($exe.Length) bytes." }
    if ($exe.VersionInfo.FileVersion -ne $Version) { throw 'Installer version mismatch.' }
    (Get-FileHash -LiteralPath $exe.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  VitanCut.Setup.exe' |
        Set-Content -LiteralPath "$output\VitanCut.Setup.exe.sha256" -Encoding ascii
    Write-Output "Installer ${Version}: $($exe.Length) bytes. Preview=$Preview"
} finally { Pop-Location }
