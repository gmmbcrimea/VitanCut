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

# Extract only the cleanup function; mocks must never touch the real scheduler.
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'uninstall-files.ps1'), [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count) { throw 'Uninstall script has syntax errors.' }
$cleanup = $ast.Find({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Remove-VitanCutCloudHealthTask' }, $true)
if (-not $cleanup) { throw 'Task cleanup function is missing.' }
. ([scriptblock]::Create($cleanup.Extent.Text))

function New-MockScheduler {
    param([string]$Command, [string]$Arguments = '--cloud-health-check', [switch]$Missing, [switch]$Denied)
    $action = [pscustomobject]@{ Type = 0; Path = $Command; Arguments = $Arguments }
    $actions = [pscustomobject]@{ Count = 1; Action = $action }
    $actions | Add-Member ScriptMethod Item { param($index) $this.Action }
    $task = [pscustomobject]@{ Enabled = $true; Definition = [pscustomobject]@{ Actions = $actions }; Stopped = $false }
    $task | Add-Member ScriptMethod GetInstances { param($flags) [pscustomobject]@{Count=1} }
    $task | Add-Member ScriptMethod Stop { param($flags) $this.Stopped=$true }
    $folder = [pscustomobject]@{ Task = $task; Missing = [bool]$Missing; Denied = [bool]$Denied; Deleted = '' }
    $folder | Add-Member ScriptMethod GetTask {
        param($name)
        if ($this.Missing) { throw [Runtime.InteropServices.COMException]::new('missing', -2147024894) }
        if ($this.Denied) { throw [UnauthorizedAccessException]::new('denied') }
        $this.Task
    }
    $folder | Add-Member ScriptMethod DeleteTask { param($name,$flags) $this.Deleted=$name }
    $scheduler = [pscustomobject]@{ Folder = $folder }
    $scheduler | Add-Member ScriptMethod Connect { }
    $scheduler | Add-Member ScriptMethod GetFolder { param($path) $this.Folder }
    return $scheduler
}

$expectedExe = Join-Path $target 'VitanCut.WinUI.exe'
$scheduler = New-MockScheduler -Command $expectedExe
if (-not (Remove-VitanCutCloudHealthTask -InstallationRoot $target -UserId 'S-1-5-21-test' -Scheduler $scheduler) -or
    $scheduler.Folder.Deleted -notmatch '^VitanCut\.CloudHealth-[A-F0-9]{16}$' -or
    $scheduler.Folder.Task.Enabled -or -not $scheduler.Folder.Task.Stopped) { throw 'Managed task cleanup failed.' }
Write-Output 'PASS: uninstall disables, stops and deletes only the managed per-user cloud task.'
foreach ($mock in @(
    (New-MockScheduler -Command (Join-Path $root 'other/VitanCut.WinUI.exe')),
    (New-MockScheduler -Command $expectedExe -Arguments '--other-task')
)) {
    if ((Remove-VitanCutCloudHealthTask -InstallationRoot $target -UserId 'S-1-5-21-test' -Scheduler $mock) -or $mock.Folder.Deleted -or -not $mock.Folder.Task.Enabled) {
        throw 'Unrelated installation or task was changed.'
    }
}
Write-Output 'PASS: unrelated installations and task commands are preserved.'
$missing = New-MockScheduler -Command $expectedExe -Missing
if (Remove-VitanCutCloudHealthTask -InstallationRoot $target -UserId 'S-1-5-21-test' -Scheduler $missing) { throw 'Missing task should be a no-op.' }
Write-Output 'PASS: older installations with no cloud task can still be uninstalled.'
$denied = New-MockScheduler -Command $expectedExe -Denied
$rejected = $false
try { Remove-VitanCutCloudHealthTask -InstallationRoot $target -UserId 'S-1-5-21-test' -Scheduler $denied } catch { $rejected = $true }
if (-not $rejected) { throw 'Scheduler access denial must not be reported as successful cleanup.' }
Write-Output 'PASS: task permission errors abort cleanup rather than silently removing application files.'
