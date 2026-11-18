param(
    [string]$ProjectRoot = (Join-Path $PSScriptRoot '..'),
    [string]$GeneratedSource
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath $ProjectRoot).Path
$prefix = 'local.dumping_ground_map'
$regions = @('gate','scrap','refuse','med','weapons','electronics','vehicles','water')
$tables = [ordered]@{}
$groups = [ordered]@{}
function Get-LootAllocation($Entries, [int]$Total) {
    if (@($Entries).Count -gt $Total) { throw 'Category share too small for item count' }
    $sum = 0.0
    $largest = 0
    for ($i = 0; $i -lt $Entries.Count; $i++) {
        $sum += [double][single]$Entries[$i].weight
        if ([single]$Entries[$i].weight -gt [single]$Entries[$largest].weight) { $largest = $i }
    }
    $result = [int[]]::new($Entries.Count)
    $assigned = 0
    for ($i = 0; $i -lt $Entries.Count; $i++) {
        $result[$i] = [Math]::Max(1, [int][Math]::Round($Total * [double][single]$Entries[$i].weight / $sum, [MidpointRounding]::AwayFromZero))
        $assigned += $result[$i]
    }
    $result[$largest] += $Total - $assigned
    if ($result[$largest] -le 0) { throw 'Loot ratios exceed integer snapshot precision' }
    return ,$result
}
$files = @(Get-ChildItem -LiteralPath (Join-Path $repo 'LootTables') -Filter '*.json' -File | Sort-Object Name)
if ($files.Count -eq 0) { throw 'LootTables: no JSON files' }
foreach ($file in $files) {
    $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
    $data = $utf8.GetString([System.IO.File]::ReadAllBytes($file.FullName)).TrimStart([char]0xFEFF) | ConvertFrom-Json
    foreach ($property in $data.PSObject.Properties.Name) {
        if ($property -notin @('tables','groups','patches')) { throw "Unknown loot field: $property" }
    }
    if (@($data.patches).Where({ $null -ne $_ }).Count -gt 0) { throw 'This package registers private tables/groups only; do not patch global loot.' }
    foreach ($kind in @('tables','groups')) {
        $target = if ($kind -eq 'tables') { $tables } else { $groups }
        foreach ($list in $data.$kind) {
            if ($null -eq $list) { continue }
            if ($list.id -cnotmatch ('^' + [regex]::Escape($prefix) + '\.[a-z0-9_.]+$')) { throw "Non-private loot ID: $($list.id)" }
            if ($target.Contains($list.id)) { throw "Duplicate loot ID: $($list.id)" }
            if (@($list.entries).Count -eq 0 -or @($list.entries).Count -gt 4096) { throw "Empty/oversized loot list: $($list.id)" }
            $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($entry in $list.entries) {
                if ($entry.id -cnotmatch '^[a-zA-Z0-9_.:-]+$' -or -not $seen.Add([string]$entry.id)) { throw "Invalid/duplicate entry in $($list.id): $($entry.id)" }
                if ($entry.weight -isnot [ValueType] -or $entry.weight -is [bool]) { throw "Non-numeric weight: $($entry.id)" }
                $weight = [double]$entry.weight
                if ([double]::IsNaN($weight) -or [double]::IsInfinity($weight) -or $weight -le 0 -or $weight -gt 1000000) { throw "Invalid weight: $($entry.id)" }
            }
            $target.Add([string]$list.id, $list.entries)
        }
    }
}
if (-not $tables.Contains("$prefix.baseline") -or $groups.Count -ne $regions.Count) { throw 'Require baseline table and exactly eight region groups' }
if (@($tables["$prefix.baseline"]).Count -lt 2 -or 'junk' -cnotin @($tables["$prefix.baseline"].id)) { throw 'Baseline needs junk and non-junk entries' }
$localeKeys = @{}
foreach ($locale in @('en','zh')) {
    $localeKeys[$locale] = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($line in [System.IO.File]::ReadAllLines((Join-Path $repo "Localization/$locale.csv"))) {
        if ($line.Contains(',')) { [void]$localeKeys[$locale].Add($line.Substring(0,$line.IndexOf(',')).Trim()) }
    }
}
$used = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
[void]$used.Add("$prefix.baseline")
foreach ($region in $regions) {
    $groupId = "$prefix.region.$region"
    if (-not $groups.Contains($groupId)) { throw "Missing group: $groupId" }
    $items = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $shares = Get-LootAllocation $groups[$groupId] 10000
    $categoryIndex = 0
    foreach ($entry in $groups[$groupId]) {
        if (-not $tables.Contains($entry.id) -or -not $entry.id.StartsWith($groupId + '.', [StringComparison]::Ordinal)) { throw "Invalid category table in $groupId : $($entry.id)" }
        [void]$used.Add([string]$entry.id)
        $key = $entry.id.Substring($groupId.Length + 1)
        foreach ($locale in @('en','zh')) { if (-not $localeKeys[$locale].Contains($key)) { throw "Missing $locale category label: $key" } }
        foreach ($item in $tables[$entry.id]) { if (-not $items.Add([string]$item.id)) { throw "Duplicate item across $region categories: $($item.id)" } }
        [void](Get-LootAllocation $tables[$entry.id] $shares[$categoryIndex])
        $categoryIndex++
    }
    if (-not $items.Contains('junk') -or $items.Count -lt 2) { throw "Region needs junk and non-junk items: $region" }
}
if ($used.Count -ne $tables.Count) { throw 'Unreferenced category tables in LootTables' }

if ($GeneratedSource) {
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add('// Generated from LootTables/*.json. Do not edit or commit this build artifact.')
    $lines.Add('using System.Collections.Generic; namespace DumpingGroundMap { internal static class LootDefaults {')
    foreach ($kind in @('tables','groups')) {
        $source = if ($kind -eq 'tables') { $tables } else { $groups }
        $lines.Add("private static readonly Dictionary<string, LootEntry[]> $kind = new Dictionary<string, LootEntry[]> {")
        foreach ($id in $source.Keys) {
            $lines.Add('{ "' + $id + '", new LootEntry[] {')
            foreach ($entry in $source[$id]) {
                $literal = ([single]$entry.weight).ToString('R', [Globalization.CultureInfo]::InvariantCulture)
                $lines.Add('new LootEntry("' + $entry.id + '", ' + $literal + 'f, Core.Id),')
            }
            $lines.Add('}},')
        }
        $lines.Add('};')
    }
    $lines.Add('internal static List<LootEntry> Table(string id) { return new List<LootEntry>(tables[id]); }')
    $lines.Add('internal static List<LootEntry> Group(string id) { return new List<LootEntry>(groups[id]); }')
    $lines.Add('} }')
    [System.IO.File]::WriteAllLines($GeneratedSource, $lines, [System.Text.UTF8Encoding]::new($false))
}
Write-Output "PASS: LootTables JSON ($($files.Count) files, $($tables.Count) private tables, $($groups.Count) region groups; weights, references and category locales)"
