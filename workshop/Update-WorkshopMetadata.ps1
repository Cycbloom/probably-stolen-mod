#requires -Version 7.0
param(
    [ValidateSet('Query','Metadata','Publish')][string]$Mode = 'Metadata',
    [string]$GameRoot = (Join-Path $PSScriptRoot '../../..')
)
$ErrorActionPreference = 'Stop'
$game = (Resolve-Path -LiteralPath $GameRoot).Path
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo 'obj/workshop-publisher'
$sdk = Join-Path $game 'Probably Stolen_Data/Managed/com.rlabrecque.steamworks.net.dll'
$native = Join-Path $game 'Probably Stolen_Data/Plugins/x86_64'
$content = ''
if ($Mode -eq 'Publish') {
    $release = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'latest-release.txt') -Raw).Trim()
    $record = Get-Content -LiteralPath (Join-Path $release 'release-record.json') -Raw | ConvertFrom-Json
    $content = (Resolve-Path -LiteralPath $record.contentFolder).Path
    if ($record.publishedFileId -ne '3840982832') { throw 'Wrong Workshop item.' }
    if (@(Get-ChildItem -LiteralPath $content -File -Recurse).Count -ne @($record.files).Count) { throw 'Release file list changed.' }
    foreach ($entry in $record.files) {
        if ((Get-FileHash -LiteralPath (Join-Path $content $entry.path)).Hash -ne $entry.sha256) { throw "Release file changed: $($entry.path)" }
    }
    $receipt = Join-Path $release 'steam-client-receipt'
} else {
    $receipt = Join-Path $output ($Mode.ToLowerInvariant() + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
Add-Type -Path $sdk
$references = @($sdk) + @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' -File | ForEach-Object FullName)
Add-Type -Path (Join-Path $PSScriptRoot 'WorkshopPublisher.cs') -ReferencedAssemblies $references
$previousAppId = $env:SteamAppId
$previousGameId = $env:SteamGameId
try {
    $env:SteamAppId = '4348910'
    $env:SteamGameId = '4348910'
    $result = [WorkshopPublisher]::Main([string[]]@($native, $PSScriptRoot, $receipt, $Mode.ToLowerInvariant(), $content))
    if ($result -ne 0) { throw "Steam operation incomplete; inspect $receipt before retrying." }
} finally {
    $env:SteamAppId = $previousAppId
    $env:SteamGameId = $previousGameId
}
Write-Output "Steam receipts: $receipt"
