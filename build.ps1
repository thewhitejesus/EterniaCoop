# Builds EterniaCoop.dll with the C# compiler built into Windows (.NET Framework csc, C# 5) and installs it.
#   powershell -ExecutionPolicy Bypass -File build.ps1               release build, installed into the game
#   powershell -ExecutionPolicy Bypass -File build.ps1 -DebugBuild   adds the research tools (src\Debug), defines COOP_DEBUG
#   powershell -ExecutionPolicy Bypass -File build.ps1 -NoInstall    build only (bin\release or bin\debug)
# The game is found through Steam (see find-game.ps1); override with -Game or $env:TOE_GAME_DIR.
# It has to have BepInEx installed, since the build references BepInEx\core.
param(
    [switch]$DebugBuild,
    [switch]$NoInstall,
    [string]$Game
)
$ErrorActionPreference = "Stop"
if (-not $Game) { . "$PSScriptRoot\find-game.ps1"; $Game = Find-EterniaGame }
$managed = "$Game\Tales of Eternia Remastered_Data\Managed"
$core    = "$Game\BepInEx\core"
$csc     = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$outDir  = if ($DebugBuild) { "$PSScriptRoot\bin\debug" } else { "$PSScriptRoot\bin\release" }
$out     = "$outDir\EterniaCoop.dll"

New-Item -ItemType Directory -Force $outDir | Out-Null
$refs = @(
    "$managed\mscorlib.dll", "$managed\System.dll", "$managed\System.Core.dll", "$managed\netstandard.dll",
    "$managed\UnityEngine.dll", "$managed\UnityEngine.CoreModule.dll", "$managed\UnityEngine.IMGUIModule.dll",
    "$managed\UnityEngine.TextRenderingModule.dll", "$managed\Unity.InputSystem.dll", "$managed\Assembly-CSharp.dll",
    "$core\BepInEx.dll", "$core\0Harmony.dll", "$core\MonoMod.RuntimeDetour.dll", "$core\MonoMod.Utils.dll"
) | ForEach-Object { "/r:$_" }

$sources = @(Get-ChildItem "$PSScriptRoot\src\*.cs" | ForEach-Object { $_.FullName })
$flags = @("/nologo", "/nostdlib", "/noconfig", "/target:library", "/optimize", "/unsafe")
if ($DebugBuild) {
    $sources += Get-ChildItem "$PSScriptRoot\src\Debug\*.cs" | ForEach-Object { $_.FullName }
    $flags += "/define:COOP_DEBUG"
}

& $csc @flags "/out:$out" @refs @sources
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
Write-Host ("Built " + $(if ($DebugBuild) { "debug" } else { "release" }) + " -> $out")

if (-not $NoInstall) {
    New-Item -ItemType Directory -Force "$Game\BepInEx\plugins" | Out-Null
    Copy-Item $out "$Game\BepInEx\plugins\EterniaCoop.dll" -Force
    Write-Host "Installed -> $Game\BepInEx\plugins\EterniaCoop.dll"
}
