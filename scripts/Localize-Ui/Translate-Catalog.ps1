$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$folder = Join-Path $root 'native-winui/Localization'
$sources = @(Get-Content -LiteralPath (Join-Path $folder 'source.json') -Raw | ConvertFrom-Json)
$languages = @{ en = 'en'; de = 'de'; es = 'es'; 'zh-Hans' = 'zh-CN' }
$options = [Text.UTF8Encoding]::new($false)
$failures = [Collections.Generic.List[string]]::new()
foreach ($language in @('en','de','es','zh-Hans')) {
    $path = Join-Path $folder "$language.json"
    $catalog = if (Test-Path -LiteralPath $path) { Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable } else { @{} }
    $pending = @($sources | Where-Object { -not $catalog.ContainsKey($_) })
    for ($index = 0; $index -lt $pending.Count;) {
        $batch = [Collections.Generic.List[string]]::new()
        $length = 0
        while ($index -lt $pending.Count -and $batch.Count -lt 20 -and $length -lt 1800) {
            $batch.Add($pending[$index]); $length += $pending[$index].Length; $index++
        }
        $query = ($batch | ForEach-Object { $_ -replace "`r?`n", ' ZXBRZX ' }) -join "`n"
        $url = 'https://translate.googleapis.com/translate_a/single?client=gtx&sl=ru&tl=' + $languages[$language] + '&dt=t&q=' + [uri]::EscapeDataString($query)
        $result = Invoke-RestMethod -Uri $url -MaximumRetryCount 3 -RetryIntervalSec 2
        $translated = (($result[0] | ForEach-Object { $_[0] }) -join '').TrimEnd("`r", "`n") -split "`r?`n"
        if ($translated.Count -ne $batch.Count) {
            $translated = foreach ($source in $batch) {
                $single = 'https://translate.googleapis.com/translate_a/single?client=gtx&sl=ru&tl=' + $languages[$language] + '&dt=t&q=' + [uri]::EscapeDataString($source)
                $r = Invoke-RestMethod -Uri $single -MaximumRetryCount 3 -RetryIntervalSec 2
                ($r[0] | ForEach-Object { $_[0] }) -join ''
            }
        }
        for ($item = 0; $item -lt $batch.Count; $item++) {
            $source = $batch[$item]
            $value = ($translated[$item] -replace '(?i)\s*ZXBRZX\s*', "`n") -replace '\{\s*(\d+)\s*\}', '{$1}'
            $a = @([regex]::Matches($source, '\{\d+(?:,[^}:]+)?(?::[^}]+)?\}') | ForEach-Object Value | Sort-Object)
            $b = @([regex]::Matches($value, '\{\d+(?:,[^}:]+)?(?::[^}]+)?\}') | ForEach-Object Value | Sort-Object)
            if (($a -join '|') -ne ($b -join '|')) {
                $failures.Add("$language : $source")
                $value = $source
            }
            $catalog[$source] = $value.Trim()
        }
        [IO.File]::WriteAllText($path, ($catalog | ConvertTo-Json -Depth 4), $options)
        Write-Output "$language $index/$($pending.Count)"
    }
    $overrides = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'overrides.json') -Raw | ConvertFrom-Json -AsHashtable
    $languageIndex = @('en','de','es','zh-Hans').IndexOf($language)
    foreach ($key in $overrides.Keys) { $catalog[$key] = $overrides[$key][$languageIndex] }
    foreach ($key in @($catalog.Keys)) {
        $leading = [regex]::Match($key, '^\s*').Value
        $trailing = [regex]::Match($key, '\s*$').Value
        $catalog[$key] = $leading + $catalog[$key].Trim() + $trailing
    }
    [IO.File]::WriteAllText($path, ($catalog | ConvertTo-Json -Depth 4), $options)
}
if ($failures.Count) { Write-Output 'Messages requiring manual placeholder review:'; $failures }
