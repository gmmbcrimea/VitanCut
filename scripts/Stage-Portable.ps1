param(
    [Parameter(Mandatory = $true)]
    [string] $BuildDirectory,
    [Parameter(Mandatory = $true)]
    [string] $OutputDirectory,
    [Parameter(Mandatory = $true)]
    [string] $Version
)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "Portable package version must be stable SemVer X.Y.Z, received '$Version'."
}

$source = (Resolve-Path -LiteralPath $BuildDirectory).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $output -PathType Container)) {
    New-Item -ItemType Directory -Force -Path $output | Out-Null
}
$output = (Resolve-Path -LiteralPath $output).Path
$separator = [IO.Path]::DirectorySeparatorChar
if ($output.Equals($source, [StringComparison]::OrdinalIgnoreCase) -or
    $source.StartsWith($output.TrimEnd($separator) + $separator, [StringComparison]::OrdinalIgnoreCase) -or
    $output.StartsWith($source.TrimEnd($separator) + $separator, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build and output directories must not contain one another.'
}

foreach ($required in @('VitanCut.WinUI.exe', 'VitanCut.WinUI.dll', 'VitanCut.WinUI.deps.json', 'VitanCut.WinUI.runtimeconfig.json', 'VitanCut.WinUI.pri')) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $required) -PathType Leaf)) {
        throw "Build output is incomplete; missing $required."
    }
}

$packageName = "VitanCut-portable-win-x64-$Version"
$archivePath = Join-Path $output "$packageName.zip"
$checksumPath = "$archivePath.sha256"
if ((Test-Path -LiteralPath $archivePath) -or (Test-Path -LiteralPath $checksumPath)) {
    throw "Package output already exists; refusing to overwrite: $archivePath"
}

$stage = Join-Path $output ".stage-$([guid]::NewGuid().ToString('N'))"
$archiveCreated = $false
try {
    New-Item -ItemType Directory -Path $stage | Out-Null
    $linkedDirectories = Get-ChildItem -LiteralPath $source -Directory -Recurse |
        Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }
    if ($linkedDirectories) { throw "Build output contains a linked directory: $($linkedDirectories[0].FullName)" }
    $files = Get-ChildItem -LiteralPath $source -File -Recurse
    foreach ($file in $files) {
        if ($file.Extension -in @('.zip', '.sha256')) { continue }
        if (($file.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Build output contains a linked file: $($file.FullName)"
        }
        $relative = [IO.Path]::GetRelativePath($source, $file.FullName)
        $segments = $relative -split '[\\/]'
        if ($segments | Where-Object { $_ -in @('data', 'logs', 'rollback', 'temp', '.test-runtime') }) {
            continue
        }
        $destination = Join-Path $stage $relative
        $destinationDirectory = Split-Path -Parent $destination
        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $false)
    $archiveCreated = $true
    $sha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($checksumPath, "$sha256  $([IO.Path]::GetFileName($archivePath))`n", [Text.Encoding]::ASCII)

    $zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $entries = @($zip.Entries | ForEach-Object { $_.FullName -replace '\\', '/' })
        foreach ($required in @('VitanCut.WinUI.exe', 'VitanCut.WinUI.dll', 'VitanCut.WinUI.deps.json', 'VitanCut.WinUI.runtimeconfig.json', 'VitanCut.WinUI.pri')) {
            if ($required -notin $entries) { throw "Portable archive is missing $required." }
        }
        if ($entries | Where-Object { ($_ -split '/')[0] -in @('data', 'logs', 'rollback', 'temp') }) {
            throw 'Portable archive unexpectedly contains runtime data or state directories.'
        }
    }
    finally { $zip.Dispose() }

    Write-Output "Portable package staged: $archivePath"
    Write-Output "SHA-256: $sha256"
}
catch {
    if ($archiveCreated -and (Test-Path -LiteralPath $archivePath)) { Remove-Item -LiteralPath $archivePath -Force }
    if (Test-Path -LiteralPath $checksumPath) { Remove-Item -LiteralPath $checksumPath -Force }
    throw
}
finally {
    if (Test-Path -LiteralPath $stage) {
        $stageFull = [IO.Path]::GetFullPath($stage)
        $outputPrefix = $output.TrimEnd($separator) + $separator
        if (-not $stageFull.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Split-Path -Leaf $stageFull).StartsWith('.stage-', [StringComparison]::Ordinal)) {
            throw "Refusing to clean an unexpected staging path: $stageFull"
        }
        Remove-Item -LiteralPath $stageFull -Recurse -Force
    }
}
