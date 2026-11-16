#requires -Version 7.0
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$metadata = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'workshop.json') -Raw | ConvertFrom-Json
$manifest = [xml](Get-Content -LiteralPath (Join-Path $repo 'manifest.xml') -Raw)
if ([string]$manifest.Manifest.Author -ne $metadata.author) { throw 'Author in manifest and workshop.json must match.' }
if ([string]$manifest.Manifest.ID -ne 'local.dumping_ground_map') { throw 'Keep the existing persistent mod ID.' }
foreach ($asset in 'assets/preview.png','assets/preview.jpg','description.bbcode.txt','PLAYER-README.txt') {
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $asset))) { throw "Missing publishing asset: $asset" }
}
if ((Get-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/preview.jpg')).Length -ge 1MB) { throw 'Upload preview must be below 1 MB.' }
if (-not $SkipBuild) {
    & (Join-Path $repo 'build.ps1') -Check
}
& (Join-Path $repo 'tools/verify.ps1') -Scan

# A fresh immutable directory avoids stale files without recursively deleting anything.
$release = Join-Path $PSScriptRoot ('releases/' + $manifest.Manifest.ModVersion + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
$content = Join-Path $release 'content'
$textures = Join-Path $content 'Textures/Paper'
New-Item -ItemType Directory -Path $textures -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'dist/DumpingGroundMap.dll'),(Join-Path $repo 'manifest.xml') -Destination $content
$textureNames = @('dumping_ground_map','dumping_ground_map_art','dumping_ground_map_base','dumping_ground_landmarks',
    'dumping_ground_gate','dumping_ground_scrap','dumping_ground_refuse','dumping_ground_med',
    'dumping_ground_weapons','dumping_ground_electronics','dumping_ground_vehicles','dumping_ground_water')
foreach ($name in $textureNames) {
    Copy-Item -LiteralPath (Join-Path $repo "dist/Textures/Paper/$name.png") -Destination $textures
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/preview.png') -Destination $content
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'PLAYER-README.txt') -Destination (Join-Path $content 'README.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets/preview.jpg') -Destination $release
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'description.bbcode.txt') -Destination $release

$publishedId = if ($metadata.publishedFileId) { [string]$metadata.publishedFileId } else { '0' }
if ($publishedId -notmatch '^\d+$') { throw 'Invalid publishedFileId in workshop.json.' }
$vdfPath = Join-Path $PSScriptRoot 'upload.vdf'
if (Test-Path -LiteralPath $vdfPath) {
    $previousVdf = Get-Content -LiteralPath $vdfPath -Raw
    if ($previousVdf -notmatch '"publishedfileid"\s+"(\d+)"') { throw 'Cannot read existing Workshop ID; keep upload.vdf and repair it before preparing an update.' }
    $previousId = $Matches[1]
    if ($publishedId -ne '0' -and $previousId -ne $publishedId) { throw 'Existing VDF item ID differs from workshop.json; resolve the item ID before preparing.' }
    $publishedId = $previousId
}
function ConvertTo-VdfString([string]$value) {
    # Keep actual line feeds in quoted VDF values; SteamCMD does not decode literal \n here.
    return $value.Replace('\','\\').Replace('"','\"').Replace("`r",'')
}
$fields = [ordered]@{
    appid = [string]$metadata.appId
    publishedfileid = $publishedId
    contentfolder = $content
    previewfile = (Join-Path $release 'preview.jpg')
    title = [string]$metadata.title
    description = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'description.bbcode.txt') -Raw)
    changenote = [string]$metadata.changeNote
}
# For existing items, omit visibility so uploads preserve the current Workshop setting.
$visibility = 'preserve'
if ($publishedId -eq '0') {
    $fields.visibility = [string]$metadata.visibility
    $visibility = [string]$metadata.visibility
}
$vdf = @('"workshopitem"','{')
foreach ($key in $fields.Keys) { $vdf += ('    "{0}"    "{1}"' -f $key,(ConvertTo-VdfString $fields[$key])) }
$vdf += '}'
$vdfText = ($vdf -join "`r`n") + "`r`n"
Set-Content -LiteralPath $vdfPath -Value $vdfText -Encoding utf8NoBOM -NoNewline
Set-Content -LiteralPath (Join-Path $release 'upload.vdf') -Value $vdfText -Encoding utf8NoBOM -NoNewline

$fileRecords = @(Get-ChildItem -LiteralPath $content -File -Recurse | Sort-Object FullName | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($content.Length + 1).Replace('\','/'); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
})
$record = [ordered]@{
    version = [string]$manifest.Manifest.ModVersion; author = [string]$manifest.Manifest.Author
    targetGame = @($manifest.Manifest.GameVersions.Version); preparedAtUtc = [DateTime]::UtcNow.ToString('o')
    contentFolder = $content; visibility = $visibility; publishedFileId = $publishedId
    binaryVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $content 'DumpingGroundMap.dll')).ProductVersion
    runtimeVerified = $false; files = $fileRecords
}
$record | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $release 'release-record.json') -Encoding utf8NoBOM
Compress-Archive -LiteralPath $content -DestinationPath (Join-Path $release 'ScrapyardExpansion.zip')
Set-Content -LiteralPath (Join-Path $PSScriptRoot 'latest-release.txt') -Value $release -Encoding utf8NoBOM
Write-Output "Prepared: $release"
Write-Output "Workshop ID: $publishedId; visibility: $visibility (existing items keep their current setting)"
Write-Output "Upload config: $vdfPath"
Write-Output "Content contains $($fileRecords.Count) files; no source, saves, personal config or old Paper directory."
