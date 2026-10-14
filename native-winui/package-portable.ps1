param([string] $Version = '')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml] $project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VitanCut.WinUI.csproj') -Raw
$actualVersion = [string] ($project.Project.PropertyGroup | Select-Object -First 1).Version
if (-not $Version) { $Version = $actualVersion }
if ($Version -ne $actualVersion) { throw 'Package version must match the application version.' }
& (Join-Path $root 'scripts/Build-Release.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$build = Join-Path $root "release/$Version"
$temporaryRoot = Join-Path $root '.test-runtime'
$staging = Join-Path $temporaryRoot ('package-' + [guid]::NewGuid().ToString('N'))
try {
    & (Join-Path $root 'scripts/Stage-Portable.ps1') -BuildDirectory $build -OutputDirectory $staging -Version $Version
    $name = "VitanCut-portable-win-x64-$Version.zip"
    foreach ($fileName in @($name, "$name.sha256")) {
        Move-Item -LiteralPath (Join-Path $staging $fileName) -Destination (Join-Path $build $fileName) -Force
    }
    Write-Output "Portable release: $(Join-Path $build $name)"
}
finally {
    if (Test-Path -LiteralPath $staging) {
        $expectedPrefix = [IO.Path]::GetFullPath($temporaryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $actual = [IO.Path]::GetFullPath($staging)
        if (-not $actual.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Split-Path -Leaf $actual).StartsWith('package-', [StringComparison]::Ordinal)) {
            throw 'Unexpected package staging directory.'
        }
        Remove-Item -LiteralPath $actual -Recurse -Force
    }
}
