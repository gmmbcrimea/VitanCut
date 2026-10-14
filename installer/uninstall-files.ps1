param([Parameter(Mandatory)][string]$Target, [Parameter(Mandatory)][int]$ParentPid)
$ErrorActionPreference = 'Stop'
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
