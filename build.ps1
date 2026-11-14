param([switch]$Install)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$managed = Join-Path $root 'Probably Stolen_Data/Managed'
$framework = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2'
$compiler = Join-Path $env:windir 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$dist = Join-Path $PSScriptRoot 'dist'
$binary = Join-Path $dist 'DumpingGroundMap.dll'
$refs = 'Assembly-CSharp','0Harmony','UnityEngine','UnityEngine.CoreModule','UnityEngine.UI',
    'Unity.TextMeshPro','UnityEngine.UIModule','UnityEngine.TextRenderingModule','UnityEngine.SharedInternalsModule'
$arguments = '/noconfig','/nologo','/t:library','/platform:x64','/optimize+','/codepage:65001','/nostdlib+',
    "/out:$binary","/r:$framework/mscorlib.dll","/r:$framework/System.dll",
    "/r:$framework/System.Core.dll","/r:$framework/Facades/netstandard.dll"
$arguments += @($refs | ForEach-Object { "/r:$managed/$_.dll" })

New-Item -ItemType Directory -Force -Path $dist | Out-Null
& $compiler @arguments (Join-Path $PSScriptRoot 'Main.cs') (Join-Path $PSScriptRoot 'Tuning.cs')
if ($LASTEXITCODE -ne 0) { throw "Compilation failed: $LASTEXITCODE" }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'manifest.xml') -Destination $dist -Force
$paper = Join-Path $PSScriptRoot 'Paper'
$distPaper = Join-Path $dist 'Paper'
New-Item -ItemType Directory -Force -Path $distPaper | Out-Null
Copy-Item -LiteralPath (Join-Path $paper 'dumping_ground_map.png') -Destination $distPaper -Force

if ($Install) {
    $destination = Join-Path $root 'Mods/DumpingGroundMap'
    $destinationPaper = Join-Path $destination 'Paper'
    New-Item -ItemType Directory -Force -Path $destinationPaper | Out-Null
    Copy-Item -LiteralPath $binary,(Join-Path $dist 'manifest.xml') -Destination $destination -Force
    Copy-Item -LiteralPath (Join-Path $distPaper 'dumping_ground_map.png') -Destination $destinationPaper -Force
    Write-Output "Installed to $destination"
}
Write-Output "Built $binary"
