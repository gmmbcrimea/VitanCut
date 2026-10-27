param(
    [Parameter(Mandatory = $true)][string] $Executable,
    [Parameter(Mandatory = $true)][string] $TaskXml
)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
if (-not $executablePath.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Only a test build inside this repository may be executed.'
}
$testRoot = Join-Path $root ('.test-runtime/cloud-health-native-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null

[xml] $xml = Get-Content -LiteralPath $TaskXml -Raw
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$xml.Task.Principals.Principal.UserId = $sid
$xml.Task.Actions.Exec.Command = $executablePath
$xml.Task.Actions.Exec.WorkingDirectory = Split-Path -Parent $executablePath
$scheduler = New-Object -ComObject Schedule.Service
$scheduler.Connect()
$folder = $scheduler.GetFolder('\')
# TASK_VALIDATE_ONLY (1) validates the actual definition without registering it.
$folder.RegisterTask('VitanCut.Validation-' + [guid]::NewGuid().ToString('N'), $xml.OuterXml, 1, $sid, $null, 3, $null) | Out-Null
Write-Output 'PASS: Windows Task Scheduler validates the generated XML without creating a task.'

foreach ($argument in @('--cloud-health-check', '--remove-cloud-health-task')) {
    $start = [Diagnostics.ProcessStartInfo]::new($executablePath)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WorkingDirectory = Split-Path -Parent $executablePath
    $start.ArgumentList.Add($argument)
    $start.Environment['VITANCUT_ENVIRONMENT'] = 'Development'
    $start.Environment['VITANCUT_DATA_ROOT'] = Join-Path $testRoot 'data'
    $process = [Diagnostics.Process]::Start($start)
    try {
        $limit = [DateTime]::UtcNow.AddSeconds(30)
        while (-not $process.HasExited) {
            $process.Refresh()
            if ($process.MainWindowHandle -ne [IntPtr]::Zero) { throw "Headless command opened a window: $argument" }
            if ([DateTime]::UtcNow -gt $limit) { throw "Headless command did not exit: $argument" }
            Start-Sleep -Milliseconds 100
        }
        if ($process.ExitCode -ne 0) { throw "Headless command failed ($($process.ExitCode)): $argument" }
        Write-Output "PASS: $argument exits successfully without a window in isolated development mode."
    }
    finally {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}
if (Test-Path -LiteralPath (Join-Path $testRoot 'data/database.json')) { throw 'Headless mode unexpectedly created a user database.' }
Write-Output 'PASS: Headless commands do not create a database or register production tasks.'
