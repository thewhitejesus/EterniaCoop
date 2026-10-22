# Builds the BepInEx 6 Bridge with the C# compiler built into Windows (.NET Framework csc, C# 5) and installs it.
#   powershell -ExecutionPolicy Bypass -File bridge\build.ps1             build and install into the game
#   powershell -ExecutionPolicy Bypass -File bridge\build.ps1 -NoInstall  build only (bridge\bin)
# Output: BepInEx6Bridge.dll (a BepInEx 5 plugin) and shim\BepInEx.Core.dll + shim\BepInEx.Unity.Mono.dll (stand-ins
# for BepInEx 6's assemblies). Installed to BepInEx\plugins\BepInEx6Bridge\.
param(
    [switch]$NoInstall,
    [string]$Game
)
$ErrorActionPreference = "Stop"
if (-not $Game) { . "$PSScriptRoot\..\find-game.ps1"; $Game = Find-EterniaGame }
$managed = "$Game\Tales of Eternia Remastered_Data\Managed"
$core    = "$Game\BepInEx\core"
$csc     = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$out     = "$PSScriptRoot\bin"
$flags   = @("/nologo", "/nostdlib", "/noconfig", "/target:library", "/optimize")
$sys     = @("$managed\mscorlib.dll", "$managed\System.dll", "$managed\System.Core.dll", "$managed\netstandard.dll") | ForEach-Object { "/r:$_" }

New-Item -ItemType Directory -Force "$out\shim" | Out-Null
function Build($dll, $refs, $src) {
    & $csc @flags "/out:$dll" @sys @refs (Get-ChildItem $src -Filter *.cs | ForEach-Object { $_.FullName })
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $dll" }
}

# BepInEx 5's BepInEx.dll is referenced under the alias BE5: the stand-ins reuse BepInEx's own namespace and type names.
Build "$out\shim\BepInEx.Core.dll" @("/r:BE5=$core\BepInEx.dll") "$PSScriptRoot\src\Core"
Build "$out\shim\BepInEx.Unity.Mono.dll" @("/r:BE5=$core\BepInEx.dll", "/r:$out\shim\BepInEx.Core.dll",
    "/r:$managed\UnityEngine.CoreModule.dll") "$PSScriptRoot\src\UnityMono"
Build "$out\BepInEx6Bridge.dll" @("/r:$core\BepInEx.dll", "/r:$core\Mono.Cecil.dll", "/r:$managed\UnityEngine.CoreModule.dll",
    "/r:$managed\UnityEngine.dll") "$PSScriptRoot\src\Loader"
Write-Host "Built -> $out"

if (-not $NoInstall) {
    $dest = "$Game\BepInEx\plugins\BepInEx6Bridge"
    New-Item -ItemType Directory -Force "$dest\shim" | Out-Null
    Copy-Item "$out\BepInEx6Bridge.dll" $dest -Force
    Copy-Item "$out\shim\*.dll" "$dest\shim" -Force
    Write-Host "Installed -> $dest"
}
