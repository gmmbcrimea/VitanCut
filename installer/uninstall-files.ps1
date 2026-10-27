param([Parameter(Mandatory)][string]$Target, [int]$ParentPid, [switch]$RemoveCloudTaskOnly)
$ErrorActionPreference = 'Stop'

function Remove-VitanCutCloudHealthTask {
    param([string]$InstallationRoot, [string]$UserId, [object]$Scheduler)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($UserId))).Replace('-', '').Substring(0, 16) }
    finally { $sha.Dispose() }
    $taskName = 'VitanCut.CloudHealth-' + $hash
    $Scheduler.Connect()
    $folder = $Scheduler.GetFolder('\')
    try { $task = $folder.GetTask($taskName) }
    catch {
        for ($error = $_.Exception; $null -ne $error; $error = $error.InnerException) {
            if ($error.HResult -eq -2147024894) { return $false }
        }
        throw
    }
    $actions = $task.Definition.Actions
    if ($actions.Count -ne 1) { return $false }
    $action = $actions.Item(1)
    if ($action.Type -ne 0 -or $action.Arguments -ne '--cloud-health-check') { return $false }
    $expected = [IO.Path]::GetFullPath((Join-Path $InstallationRoot 'VitanCut.WinUI.exe'))
    $actual = [IO.Path]::GetFullPath($action.Path.Trim('"'))
    if (-not [string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase)) { return $false }
    $task.Enabled = $false
    if ($task.GetInstances(0).Count -gt 0) { $task.Stop(0) }
    $folder.DeleteTask($taskName, 0)
    return $true
}

if ($RemoveCloudTaskOnly) {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    Remove-VitanCutCloudHealthTask -InstallationRoot $Target -UserId $sid -Scheduler (New-Object -ComObject Schedule.Service) | Out-Null
    return
}
if ($ParentPid -le 0) { throw 'A valid uninstaller parent process is required.' }
Wait-Process -Id $ParentPid -ErrorAction SilentlyContinue
$root = [IO.Path]::GetFullPath($Target).TrimEnd('\')
$manifest = Join-Path $root 'VitanCut.install-files.txt'
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) { throw 'Installation manifest is missing.' }
$files = @(Get-Content -LiteralPath $manifest -Encoding UTF8)
foreach ($relative in $files) {
    if (-not $relative -or [IO.Path]::IsPathRooted($relative)) { throw 'Invalid manifest path.' }
    $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $path.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase) -or
        $path.StartsWith($root + '\data\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe manifest path.' }
    $ancestor = $path
    while ($ancestor -and $ancestor.Length -ge $root.Length) {
        $entry = Get-Item -LiteralPath $ancestor -Force -ErrorAction SilentlyContinue
        if ($entry -and ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked installation path is not supported.' }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
}
foreach ($relative in $files) {
    $path = [IO.Path]::GetFullPath((Join-Path $root $relative))
    if (Test-Path -LiteralPath $path -PathType Leaf) { Remove-Item -LiteralPath $path -Force }
}
Remove-Item -LiteralPath $manifest -Force
# Only empty known directories are removed; databases and unrelated files survive.
$directories = @($files | ForEach-Object { [IO.Path]::GetDirectoryName((Join-Path $root $_)) } | Sort-Object -Unique -Descending)
foreach ($directory in $directories) {
    try { [IO.Directory]::Delete($directory, $false) } catch { }
}
try { [IO.Directory]::Delete($root, $false) } catch { }
Remove-Item -LiteralPath $PSCommandPath -Force
