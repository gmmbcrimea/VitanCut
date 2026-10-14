$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root 'native-winui/VitanCut.WinUI.csproj'
[xml] $project = Get-Content -LiteralPath $projectPath -Raw
$version = [string] ($project.Project.PropertyGroup | Select-Object -First 1).Version
& (Join-Path $PSScriptRoot 'Validate-Release.ps1') -Version $version
$output = Join-Path $root "release/$version"
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
foreach ($resource in @('VitanCut.WinUI.pri')) {
    if (-not (Test-Path -LiteralPath (Join-Path $output $resource) -PathType Leaf)) {
        throw "Release is missing a required XAML resource: $resource"
    }
}
Write-Output "Release: $output"
