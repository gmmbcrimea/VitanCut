$ErrorActionPreference = 'Stop'
$root = Join-Path (Split-Path -Parent $PSScriptRoot) ('dist\installer-checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root | Out-Null
$app = [ordered]@{ tag_name = 'v1.2.5'; draft = $false; prerelease = $false; assets = @(
    @{name='VitanCut-portable-win-x64-1.2.5.zip.sha256'; browser_download_url='https://example.invalid/hash'},
    @{name='VitanCut-portable-win-x64-1.2.5.zip'; browser_download_url='https://github.com/gmmbcrimea/VitanCut/releases/download/v1.2.5/VitanCut-portable-win-x64-1.2.5.zip'}
) }
$installer = @{tag_name='installer-v1.1.1'; assets=@(@{name='VitanCut.Setup.exe'})}
$prerelease = @{tag_name='v9.0.0'; prerelease=$true; assets=@()}
@($installer, $prerelease, $app) | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath "$root\releases.json" -Encoding UTF8
& "$PSScriptRoot\select-release.ps1" -Metadata "$root\releases.json" -Output $root
if ((Get-Content -LiteralPath "$root\app-version.txt" -Raw) -ne '1.2.5') { throw 'Incorrect application version.' }
if ((Get-Content -LiteralPath "$root\archive-url.txt" -Raw) -notmatch '/v1.2.5/VitanCut-portable-win-x64-1.2.5.zip$') { throw 'Incorrect archive.' }
Write-Output 'PASS: installer/prerelease/checksum releases skipped; stable archive selected.'
$target = Join-Path $root "user's installation"
New-Item -ItemType Directory -Path "$target\data", "$target\Assets" | Out-Null
[IO.File]::WriteAllText("$target\data\database.json", 'preserved database')
[IO.File]::WriteAllText("$target\personal.txt", 'preserved extra file')
[IO.File]::WriteAllText("$target\VitanCut.WinUI.exe", 'synthetic binary')
[IO.File]::WriteAllText("$target\Assets\logo.png", 'synthetic resource')
@('VitanCut.WinUI.exe','Assets\logo.png') | Set-Content -LiteralPath "$target\VitanCut.install-files.txt" -Encoding UTF8
$uninstall = Join-Path $root 'uninstall-test.ps1'
Copy-Item -LiteralPath "$PSScriptRoot\uninstall-files.ps1" -Destination $uninstall
& $uninstall -Target $target -ParentPid 2147483647
if ((Get-Content -LiteralPath "$target\data\database.json" -Raw) -ne 'preserved database' -or
    -not (Test-Path -LiteralPath "$target\personal.txt") -or
    (Test-Path -LiteralPath "$target\VitanCut.WinUI.exe")) { throw 'Uninstall preservation check failed.' }
Write-Output 'PASS: manifest uninstall removes program files, preserving database and unrelated files.'
@('..\releases.json') | Set-Content -LiteralPath "$target\VitanCut.install-files.txt" -Encoding UTF8
Copy-Item -LiteralPath "$PSScriptRoot\uninstall-files.ps1" -Destination $uninstall
$rejected = $false
try { & $uninstall -Target $target -ParentPid 2147483647 } catch { $rejected = $true }
if (-not $rejected -or -not (Test-Path -LiteralPath "$root\releases.json")) { throw 'Traversal was not rejected.' }
Write-Output 'PASS: paths escaping installation directory are rejected before deletion.'
