param([string]$ProjectRoot = (Join-Path $PSScriptRoot '..'))
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath $ProjectRoot).Path
$utf8 = New-Object System.Text.UTF8Encoding($false, $true)
function Read-Locale([string]$locale) {
    $path = Join-Path $repo "Localization/$locale.csv"
    $lines = [IO.File]::ReadAllLines($path, $utf8)
    $table = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    $lineNumber = 0
    foreach ($line in $lines) {
        ++$lineNumber
        if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith('#')) { continue }
        $comma = $line.IndexOf(',')
        if ($comma -lt 1) { throw "${path}:${lineNumber}: expected key,value without a header" }
        $key = $line.Substring(0, $comma).Trim()
        $value = $line.Substring($comma + 1)
        if ($key -notmatch '^[a-z][a-z0-9_]*$') { throw "Invalid localization key: $key" }
        if (-not ($value -cmatch '^"(?:[^"]|"")*"$')) { throw "${path}:${lineNumber}: invalid quoted CSV value" }
        $value = $value.Substring(1, $value.Length - 2).Replace('""','"').Replace('\n',"`n")
        if ($table.ContainsKey($key)) { throw "Duplicate localization key: $locale/$key" }
        if ([string]::IsNullOrWhiteSpace($value)) { throw "Empty localization value: $locale/$key" }
        $table.Add($key, $value)
    }
    return ,$table
}
$zh = Read-Locale 'zh'
$en = Read-Locale 'en'
if ($zh.Count -eq 0 -or $zh.Count -ne $en.Count) { throw 'Chinese and English key counts differ or are empty.' }
$formatArgs = [object[]]@(0..31 | ForEach-Object { "arg$_" })
foreach ($key in $zh.Keys) {
    if (-not $en.ContainsKey($key)) { throw "English key missing: $key" }
    $zhArgs = @([regex]::Matches($zh[$key], '(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    $enArgs = @([regex]::Matches($en[$key], '(?<!\{)\{(\d+)(?:[^{}]*)\}(?!\})') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
    if (($zhArgs -join ',') -cne ($enArgs -join ',')) { throw "Placeholder mismatch: $key" }
    foreach ($text in $zh[$key], $en[$key]) {
        [void][string]::Format([Globalization.CultureInfo]::InvariantCulture, $text, $formatArgs)
    }
}
Get-ChildItem -LiteralPath (Join-Path $repo 'src') -Filter '*.cs' -File -Recurse | ForEach-Object {
    $source = [IO.File]::ReadAllText($_.FullName, $utf8)
    if ($source.Contains('Copy.Text(')) { throw "Old bilingual call remains: $($_.FullName)" }
    foreach ($match in [regex]::Matches($source, 'Copy\.Get\("([a-z][a-z0-9_]+)"\s*[,)]')) {
        if (-not $en.ContainsKey($match.Groups[1].Value)) { throw "Missing literal key in $($_.Name): $($match.Groups[1].Value)" }
    }
}
Write-Output "PASS: Localization/zh.csv and en.csv ($($zh.Count) matching keys, UTF-8, CSV quoting and format placeholders)"
