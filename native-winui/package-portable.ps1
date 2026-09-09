param(
    [string]$Version = "1.0.1"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $projectRoot "native-winui\bin\x64\Release\net8.0-windows10.0.26100.0\win-x64"
$stagingDirectory = Join-Path $env:TEMP "VitanCut-portable-$Version"
$releaseDirectory = Join-Path $projectRoot "dist"
$archivePath = Join-Path $releaseDirectory "VitanCut-portable-win-x64-$Version.zip"

dotnet build (Join-Path $PSScriptRoot "VitanCut.WinUI.csproj") -c Release -p:Platform=x64
Remove-Item $stagingDirectory -Recurse -Force -ErrorAction SilentlyContinue
New-Item $stagingDirectory -ItemType Directory | Out-Null
Copy-Item (Join-Path $publishDirectory "*") $stagingDirectory -Recurse -Force
New-Item $releaseDirectory -ItemType Directory -Force | Out-Null
Remove-Item $archivePath -Force -ErrorAction SilentlyContinue
Compress-Archive -Path $stagingDirectory -DestinationPath $archivePath -CompressionLevel Optimal
Remove-Item $stagingDirectory -Recurse -Force

Write-Host "Portable release created: $archivePath"
