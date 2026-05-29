$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $projectRoot 'src'
$outDir = Join-Path $projectRoot 'dist\MultiKeyboardProbeClean'
$outDll = Join-Path $outDir 'MultiKeyboardProbeClean.dll'
$gameManaged = 'C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice\A Dance of Fire and Ice_Data\Managed'
$ummDir = Join-Path $gameManaged 'UnityModManager'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe not found' }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$refs = @(
    (Join-Path $ummDir 'UnityModManager.dll'),
    (Join-Path $ummDir '0Harmony.dll'),
    (Join-Path $gameManaged 'netstandard.dll'),
    (Join-Path $gameManaged 'Assembly-CSharp.dll'),
    (Join-Path $gameManaged 'RDTools.dll'),
    (Join-Path $gameManaged 'Rewired_Core.dll'),
    (Join-Path $gameManaged 'UnityEngine.dll'),
    (Join-Path $gameManaged 'UnityEngine.CoreModule.dll'),
    (Join-Path $gameManaged 'UnityEngine.InputLegacyModule.dll'),
    (Join-Path $gameManaged 'UnityEngine.IMGUIModule.dll')
)
foreach ($ref in $refs) { if (-not (Test-Path $ref)) { throw "Missing reference: $ref" } }
$sources = Get-ChildItem -LiteralPath $src -Filter *.cs | ForEach-Object { $_.FullName }
$args = @('/nologo','/target:library','/langversion:default','/unsafe-','/optimize+',"/out:$outDll","/reference:$($refs -join ';')") + $sources
& $csc @args
if ($LASTEXITCODE -ne 0) { throw "csc failed with exit code $LASTEXITCODE" }
Copy-Item (Join-Path $projectRoot 'Info.json') (Join-Path $outDir 'Info.json') -Force
Write-Host "Build complete: $outDir"
