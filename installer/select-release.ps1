param([Parameter(Mandatory)][string]$Metadata, [Parameter(Mandatory)][string]$Output)
$ErrorActionPreference = 'Stop'
$releases = @(Get-Content -LiteralPath $Metadata -Raw -Encoding UTF8 | ConvertFrom-Json)
foreach ($release in $releases) {
    if ($release.draft -or $release.prerelease -or $release.tag_name -notmatch '^v(\d+\.\d+\.\d+)$') { continue }
    $version = $Matches[1]
    $asset = @($release.assets | Where-Object {
        $_.name -eq "VitanCut-portable-win-x64-$version.zip" -and
        $_.browser_download_url -eq "https://github.com/gmmbcrimea/VitanCut/releases/download/v$version/VitanCut-portable-win-x64-$version.zip"
    }) | Select-Object -First 1
    if ($asset) {
        [IO.File]::WriteAllText((Join-Path $Output 'archive-url.txt'), $asset.browser_download_url, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $Output 'app-version.txt'), $version, [Text.UTF8Encoding]::new($false))
        exit 0
    }
}
throw 'No published stable application archive found.'
