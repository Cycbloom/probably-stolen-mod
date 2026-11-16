#requires -Version 7.0
param(
    [Parameter(Mandatory=$true)][string]$Account,
    [string]$SteamCmdPath = (Join-Path $PSScriptRoot '../../_tools/SteamCMD/steamcmd.exe'),
    [switch]$CheckOnly,
    [switch]$DescriptionOnly
)
$ErrorActionPreference = 'Stop'
$vdfPath = Join-Path $PSScriptRoot 'upload.vdf'
if (-not (Test-Path -LiteralPath $vdfPath)) { throw 'Run Prepare-Workshop.ps1 first.' }
$steamCommand = Get-Command $SteamCmdPath -ErrorAction SilentlyContinue
if ($null -eq $steamCommand -and -not $PSBoundParameters.ContainsKey('SteamCmdPath')) {
    $steamCommand = Get-Command 'steamcmd.exe' -ErrorAction SilentlyContinue
}
if ($null -eq $steamCommand) { throw 'SteamCMD not found. Install SteamCMD and pass its full path using -SteamCmdPath.' }
$vdf = Get-Content -LiteralPath $vdfPath -Raw
if ($vdf -notmatch '"publishedfileid"\s+"(\d+)"') { throw 'Invalid upload.vdf.' }
$previousId = $Matches[1]
if ($DescriptionOnly) {
    if ($previousId -eq '0') { throw 'Description-only updates require an existing Workshop item ID.' }
    if ($vdf -notmatch '"appid"\s+"(\d+)"') { throw 'Missing appid.' }
    $appId = $Matches[1]
    $description = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'description.bbcode.txt') -Raw
    if ([string]::IsNullOrWhiteSpace($description)) { throw 'Description is empty.' }
    if ($description.Contains('\n')) { throw 'Use actual line breaks in the description, not literal backslash-n.' }
    if ([Text.Encoding]::UTF8.GetByteCount($description) -ge 8000) { throw 'Description exceeds the upload size limit.' }
    $description = $description.Replace('\','\\').Replace('"','\"').Replace("`r",'')
    $metadataVdf = @"
"workshopitem"
{
    "appid"    "$appId"
    "publishedfileid"    "$previousId"
    "description"    "$description"
}
"@
    $vdfPath = Join-Path $PSScriptRoot 'description-update.vdf'
    Set-Content -LiteralPath $vdfPath -Value $metadataVdf -Encoding utf8NoBOM
    $receiptPath = Join-Path $PSScriptRoot 'description-update-after-steamcmd.vdf'
    Write-Output "Description-only update for Workshop item $previousId; using the current Chinese/English combined text."
} else {
    if ($vdf -notmatch '"contentfolder"\s+"([^"\r\n]+)"') { throw 'Missing contentfolder.' }
    $content = $Matches[1].Replace('\\','\')
    if (-not (Test-Path -LiteralPath (Join-Path $content 'manifest.xml'))) { throw 'Prepared content folder is missing.' }
    $recordPath = Join-Path (Split-Path -Parent $content) 'release-record.json'
    $record = Get-Content -LiteralPath $recordPath -Raw | ConvertFrom-Json
    if (@(Get-ChildItem -LiteralPath $content -File -Recurse).Count -ne @($record.files).Count) {
        throw 'Release file list changed; prepare again.'
    }
    foreach ($entry in $record.files) {
        $target = Join-Path $content $entry.path
        if (-not (Test-Path -LiteralPath $target)) { throw "Release file missing: $($entry.path)" }
        if ((Get-FileHash -LiteralPath $target).Hash -ne $entry.sha256) { throw "Release file changed; prepare again: $($entry.path)" }
    }
    $receiptPath = Join-Path (Split-Path -Parent $content) 'upload-after-steamcmd.vdf'
}
if ($CheckOnly) {
    if ($DescriptionOnly) { Write-Output 'PASS: existing item ID, SteamCMD and description-only VDF checked. No login or upload performed.' }
    else { Write-Output "PASS: PowerShell $($PSVersionTable.PSVersion), SteamCMD and all $(@($record.files).Count) release files checked. No login or upload performed." }
    return
}
Write-Output 'SteamCMD will ask for your password and Steam Guard code directly. This script does not store either.'
& $steamCommand.Source '+login' $Account '+workshop_build_item' $vdfPath '+quit'
if ($LASTEXITCODE -ne 0) { throw "SteamCMD exited with $LASTEXITCODE. Check its upload log and the item page before retrying." }
$updated = Get-Content -LiteralPath $vdfPath -Raw
if ($updated -notmatch '"publishedfileid"\s+"([1-9]\d*)"') { throw 'SteamCMD did not return a Workshop item ID; upload is not confirmed.' }
$itemId = $Matches[1]
if ($previousId -ne '0' -and $itemId -ne $previousId) { throw 'SteamCMD returned an unexpected Workshop item ID.' }
Copy-Item -LiteralPath $vdfPath -Destination $receiptPath
if ($previousId -eq '0') { Write-Output "Assigned item ID: $itemId. Verify the upload on its page and keep upload.vdf for future updates." }
else { Write-Output "Item ID: $itemId. Check the page and SteamCMD log to confirm this update succeeded." }
Write-Output "Item page: https://steamcommunity.com/sharedfiles/filedetails/?id=$itemId"
