param(
    [Parameter(Mandatory = $true)]
    [string] $Version
)

$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$') {
    throw "Release version must be stable SemVer X.Y.Z, received '$Version'."
}

$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root 'native-winui/VitanCut.WinUI.csproj'
$notesPath = Join-Path $root "release-notes/v$Version.md"
[xml] $project = Get-Content -LiteralPath $projectPath -Raw
$propertyGroup = $project.Project.PropertyGroup | Select-Object -First 1
$expectedAssemblyVersion = "$Version.0"
if ([string]$propertyGroup.DefineConstants -match 'VITANCUT_UI_REVIEW') {
    throw 'A stable release must not enable UI-review isolation or preview services.'
}

$actual = @{
    Version = [string] $propertyGroup.Version
    AssemblyVersion = [string] $propertyGroup.AssemblyVersion
    FileVersion = [string] $propertyGroup.FileVersion
    InformationalVersion = [string] $propertyGroup.InformationalVersion
}
$expected = @{
    Version = $Version
    AssemblyVersion = $expectedAssemblyVersion
    FileVersion = $expectedAssemblyVersion
    InformationalVersion = $Version
}

foreach ($key in $expected.Keys) {
    if ($actual[$key] -ne $expected[$key]) {
        throw "$key mismatch: expected '$($expected[$key])', found '$($actual[$key])'."
    }
}

if (-not (Test-Path -LiteralPath $notesPath -PathType Leaf)) {
    throw "Release notes are missing: $notesPath"
}

$notes = (Get-Content -LiteralPath $notesPath -Raw).Trim()
if ($notes.Length -lt 10 -or $notes -match '(?im)^\s*(full changelog|полный список изменений)\s*$') {
    throw 'Release notes must contain a concise, non-empty summary for this version.'
}

Write-Output "Release metadata is consistent for $Version."
