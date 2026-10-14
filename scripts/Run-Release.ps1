$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
[xml] $project = Get-Content -LiteralPath (Join-Path $root 'native-winui/VitanCut.WinUI.csproj') -Raw
$version = [string] ($project.Project.PropertyGroup | Select-Object -First 1).Version
$app = Join-Path $root "release/$version/VitanCut.WinUI.exe"
if (-not (Test-Path -LiteralPath $app -PathType Leaf)) {
    & (Join-Path $PSScriptRoot 'Build-Release.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
Start-Process -FilePath $app -WorkingDirectory (Split-Path -Parent $app)
