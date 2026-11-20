param(
    [string]$GameRoot = (Join-Path $PSScriptRoot '../../..'),
    [string]$Binary = (Join-Path $PSScriptRoot '../dist/DumpingGroundMap.dll'),
    [switch]$Scan
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$managed = Join-Path (Resolve-Path -LiteralPath $GameRoot).Path 'Probably Stolen_Data/Managed'
$binaryPath = (Resolve-Path -LiteralPath $Binary).Path
if ($Scan) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $managed 'DseCecil.dll'))
    [void][Reflection.Assembly]::LoadFrom((Join-Path $managed 'Assembly-CSharp.dll'))
    # Outside Unity the game's assembly catalog is not populated: supply its actual managed DLLs.
    $allowed = [string[]]@(Get-ChildItem -LiteralPath $managed -Filter '*.dll' | ForEach-Object BaseName)
    $package = Join-Path $repo 'dist'
    $result = [ModScanner]::Scan([string[]]@($binaryPath),$allowed,$package,$true)
    # 发布版不声明任何能力：扫描结果必须为空，清单也不许答应任何能力。
    $manifest = [xml](Get-Content -LiteralPath (Join-Path $repo 'manifest.xml') -Raw)
    $declared = @($manifest.Manifest.Capabilities.Capability |
        ForEach-Object { ([string]$_).Trim().ToLowerInvariant() } |
        Where-Object { $_ })
    $undeclared = @([ModCapability]::Undeclared([string[]]@($result.Capabilities),[string[]]$declared))
    if ($result.Refused -or $result.Capabilities.Count -gt 0 -or $declared.Count -gt 0 -or $undeclared.Count -gt 0) {
        throw "ModScanner: $($result.Error) $($result.FormatFindings(30))"
    }
    Write-Output "PASS: game ModScanner ($($result.MethodsScanned) methods, $($result.HardFlags.Count) hard flags, $($result.Capabilities.Count) capabilities; $($result.Pending.Count) guarded game-API calls)"
    return
}
$out = Join-Path $repo 'obj/tests'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$framework = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.7.2'
$compiler = Join-Path $env:windir 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$test = Join-Path $out 'DumpingGroundMap.Tests.exe'
& $compiler /noconfig /nologo /t:exe /platform:x64 /codepage:65001 /nostdlib+ "/out:$test" "/r:$binaryPath" "/r:$managed/Assembly-CSharp.dll" "/r:$managed/0Harmony.dll" "/r:$managed/UnityEngine.CoreModule.dll" "/r:$framework/mscorlib.dll" "/r:$framework/System.dll" "/r:$framework/System.Core.dll" "/r:$framework/System.Web.Extensions.dll" "/r:$framework/Facades/netstandard.dll" (Join-Path $repo 'tests/RulesTests.cs') (Join-Path $repo 'tests/ScavengingCompatibilityTests.cs') (Join-Path $repo 'tests/LocalizationTests.cs') (Join-Path $repo 'tests/LoggingTests.cs') (Join-Path $repo 'tests/AssetDiagnosticsTests.cs') (Join-Path $repo 'tests/DebugToolsTests.cs') (Join-Path $repo 'tests/LootJsonTests.cs') (Join-Path $repo 'tests/SoundFeedbackTests.cs') (Join-Path $repo 'tests/GroundOptionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Rule test compilation failed.' }
& $test $binaryPath $managed
if ($LASTEXITCODE -ne 0) { throw 'Rule tests failed.' }
