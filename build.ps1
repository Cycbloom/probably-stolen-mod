param(
    [switch]$Install,
    [string]$GameRoot = (Join-Path $PSScriptRoot '../..'),
    [switch]$Check
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $GameRoot).Path
$managed = Join-Path $root 'Probably Stolen_Data/Managed'
$framework = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2'
$compiler = Join-Path $env:windir 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$dist = Join-Path $PSScriptRoot 'dist'
$intermediate = Join-Path $PSScriptRoot 'obj/release'
$binary = Join-Path $intermediate 'DumpingGroundMap.dll'
& (Join-Path $PSScriptRoot 'tools/verify-localization.ps1') -ProjectRoot $PSScriptRoot
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File -Recurse | Sort-Object FullName | ForEach-Object FullName)
if ($sources.Count -eq 0) { throw 'No C# sources found in src/.' }
$artKeys = 'dumping_ground_map_art','dumping_ground_map_base','dumping_ground_landmarks',
    'dumping_ground_gate','dumping_ground_scrap','dumping_ground_refuse','dumping_ground_med',
    'dumping_ground_weapons','dumping_ground_electronics','dumping_ground_vehicles','dumping_ground_water'
foreach ($key in $artKeys) {
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot "Paper/$key.png"))) {
        throw "Missing scene texture: $key"
    }
}
foreach ($required in $managed,$compiler,(Join-Path $framework 'Facades/netstandard.dll'),(Join-Path $PSScriptRoot 'Paper/dumping_ground_map.png')) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing build input: $required" }
}
if ($Install) {
    $running = Get-Process -Name 'Probably Stolen' -ErrorAction SilentlyContinue
    if ($running) { throw 'Close Probably Stolen before installing. Building without -Install is supported while it runs.' }
}
$refs = 'Assembly-CSharp','0Harmony','UnityEngine','UnityEngine.CoreModule','UnityEngine.UI',
    'Unity.TextMeshPro','UnityEngine.InputLegacyModule','UnityEngine.UIModule','UnityEngine.TextRenderingModule','UnityEngine.SharedInternalsModule'
$arguments = '/noconfig','/nologo','/t:library','/platform:x64','/optimize+','/codepage:65001','/nostdlib+',
    "/out:$binary","/r:$framework/mscorlib.dll","/r:$framework/System.dll",
    "/r:$framework/System.Core.dll","/r:$framework/Facades/netstandard.dll"
$arguments += @($refs | ForEach-Object { "/r:$managed/$_.dll" })

New-Item -ItemType Directory -Force -Path $intermediate | Out-Null
$lootSource = Join-Path $intermediate 'LootDefaults.cs'
& (Join-Path $PSScriptRoot 'tools/prepare-loot.ps1') -ProjectRoot $PSScriptRoot -GeneratedSource $lootSource
[xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.xml') -Raw
$releaseVersion = [version]$manifest.Manifest.ModVersion
$commit = 'unknown'
if (Get-Command git -ErrorAction SilentlyContinue) {
    $candidate = & git -C $PSScriptRoot rev-parse --short HEAD 2>$null
    if ($LASTEXITCODE -eq 0 -and $candidate -match '^[0-9a-f]+$') { $commit = $candidate }
}
$buildInfo = Join-Path $intermediate 'BuildInfo.cs'
$fileVersion = '{0}.{1}.{2}.0' -f $releaseVersion.Major,$releaseVersion.Minor,$releaseVersion.Build
Set-Content -LiteralPath $buildInfo -Encoding UTF8 -Value @"
using System.Reflection;
[assembly: AssemblyFileVersion("$fileVersion")]
[assembly: AssemblyInformationalVersion("$releaseVersion+$commit")]
"@
& $compiler @arguments @sources $buildInfo $lootSource
if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $LASTEXITCODE" }

if ($Check) {
    & (Join-Path $PSScriptRoot 'tools/verify.ps1') -GameRoot $root -Binary $binary
}
New-Item -ItemType Directory -Force -Path $dist | Out-Null
Copy-Item -LiteralPath $binary,(Join-Path $PSScriptRoot 'manifest.xml'),(Join-Path $PSScriptRoot 'LICENSE') -Destination $dist -Force
$distLoot = Join-Path $dist 'LootTables'
New-Item -ItemType Directory -Force -Path $distLoot | Out-Null
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'LootTables') -Filter '*.json' -File | Copy-Item -Destination $distLoot -Force
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'LootTables') -Filter '*.json' -File) {
    if ((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $distLoot $file.Name)).Hash) { throw "Packaged loot JSON differs: $($file.Name)" }
}
$distLocalization = Join-Path $dist 'Localization'
New-Item -ItemType Directory -Force -Path $distLocalization | Out-Null
foreach ($locale in 'zh','en') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "Localization/$locale.csv") -Destination $distLocalization -Force
    if ((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot "Localization/$locale.csv")).Hash -ne
        (Get-FileHash -LiteralPath (Join-Path $distLocalization "$locale.csv")).Hash) {
        throw "Packaged localization differs: $locale.csv"
    }
}
$paper = Join-Path $PSScriptRoot 'Paper'
$distPaper = Join-Path $dist 'Textures/Paper'
New-Item -ItemType Directory -Force -Path $distPaper | Out-Null
Get-ChildItem -LiteralPath $paper -Filter '*.png' -File | Copy-Item -Destination $distPaper -Force
foreach ($key in $artKeys) {
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $paper "$key.png")).Hash
    if ($sourceHash -ne (Get-FileHash -LiteralPath (Join-Path $distPaper "$key.png")).Hash) {
        throw "Packaged scene texture differs: $key"
    }
}
Write-Output ('PASS: ' + $artKeys.Count + ' scene/map textures packaged in Textures/Paper (native ModLoader path)')

if ($Install) {
    $destination = Join-Path $root 'Mods/DumpingGroundMap'
    $destinationPaper = Join-Path $destination 'Textures/Paper'
    New-Item -ItemType Directory -Force -Path $destinationPaper | Out-Null
    Copy-Item -LiteralPath (Join-Path $dist 'DumpingGroundMap.dll'),(Join-Path $dist 'manifest.xml'),(Join-Path $dist 'LICENSE') -Destination $destination -Force
    $destinationLoot = Join-Path $destination 'LootTables'
    New-Item -ItemType Directory -Force -Path $destinationLoot | Out-Null
    Get-ChildItem -LiteralPath $distLoot -Filter '*.json' -File | Copy-Item -Destination $destinationLoot -Force
    $destinationLocalization = Join-Path $destination 'Localization'
    New-Item -ItemType Directory -Force -Path $destinationLocalization | Out-Null
    foreach ($locale in 'zh','en') {
        Copy-Item -LiteralPath (Join-Path $distLocalization "$locale.csv") -Destination $destinationLocalization -Force
    }
    Get-ChildItem -LiteralPath $distPaper -Filter '*.png' -File | Copy-Item -Destination $destinationPaper -Force
    Write-Output "Installed to $destination"
}
Write-Output "Built $(Join-Path $dist 'DumpingGroundMap.dll') ($($sources.Count) source files)"
Write-Output "Version $releaseVersion; source $commit; SHA256 $((Get-FileHash -LiteralPath (Join-Path $dist 'DumpingGroundMap.dll')).Hash)"
