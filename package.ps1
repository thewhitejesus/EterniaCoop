# Builds the release and makes three zips in dist\:
#   EterniaCoop-<ver>.zip               extract into the game folder (BepInEx + mod + BepInEx 6 Bridge + docs + licenses)
#   BepInEx6Bridge-<bridgever>.zip      the bridge on its own (with BepInEx 5), for players who don't want co-op
#   EterniaCoop-<ver>-source.zip        source, build scripts, research scripts, docs (no decompiled game code)
# BepInEx comes from the original, checksum-verified BepInEx download, never from the game folder.
param([string]$BepInExZip = "$PSScriptRoot\BepInEx_win_x64_5.4.23.5.zip")
$ErrorActionPreference = "Stop"
$version = ([regex]::Match((Get-Content "$PSScriptRoot\src\Plugin.cs" -Raw), 'Version = "([^"]+)"')).Groups[1].Value
$bepinexSha = "82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4"
if (-not (Test-Path $BepInExZip)) {
    throw "Download BepInEx_win_x64_5.4.23.5.zip from https://github.com/BepInEx/BepInEx/releases/tag/v5.4.23.5 next to package.ps1"
}
if ((Get-FileHash $BepInExZip).Hash -ne $bepinexSha) { throw "BepInEx zip checksum mismatch" }

# Zips a folder with "/" in entry names. (Windows PowerShell's Compress-Archive writes "\", which Linux tools - and so
# Steam Deck users - unpack as files literally named "BepInEx\core\...".)
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
function New-Zip($folder, $zip) {
    Remove-Item $zip -ErrorAction SilentlyContinue
    $root = (Resolve-Path $folder).Path.TrimEnd('\') + '\'
    $archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in Get-ChildItem $folder -Recurse -File -Force) {
            $name = $f.FullName.Substring($root.Length).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $name, [IO.Compression.CompressionLevel]::Optimal)
        }
    } finally { $archive.Dispose() }
}

& "$PSScriptRoot\build.ps1" -NoInstall
& "$PSScriptRoot\bridge\build.ps1" -NoInstall
$bridgeVersion = ([regex]::Match((Get-Content "$PSScriptRoot\bridge\src\Loader\Bridge.cs" -Raw), 'Version = "([^"]+)"')).Groups[1].Value
$dist = "$PSScriptRoot\dist"; $stage = "$dist\stage"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null

# The bridge as installed: BepInEx\plugins\BepInEx6Bridge\ (plugin, shim\, readme, license)
function Add-Bridge($root) {
    $b = "$root\BepInEx\plugins\BepInEx6Bridge"
    New-Item -ItemType Directory -Force "$b\shim" | Out-Null
    Copy-Item "$PSScriptRoot\bridge\bin\BepInEx6Bridge.dll" $b
    Copy-Item "$PSScriptRoot\bridge\bin\shim\*.dll" "$b\shim"
    Copy-Item "$PSScriptRoot\bridge\README.md" "$b\README.md"
    Copy-Item "$PSScriptRoot\LICENSE" "$b\LICENSE"
}

# Bridge-only package
$bstage = "$dist\bstage"
Remove-Item $bstage -Recurse -Force -ErrorAction SilentlyContinue
Expand-Archive $BepInExZip -DestinationPath $bstage
Add-Bridge $bstage
Copy-Item "$PSScriptRoot\bridge\README.md" "$bstage\BepInEx6Bridge-README.md"
$bzip = "$dist\BepInEx6Bridge-$bridgeVersion.zip"
New-Zip "$bstage" $bzip
Remove-Item $bstage -Recurse -Force

# Player package
Expand-Archive $BepInExZip -DestinationPath $stage
Add-Bridge $stage
$plug = "$stage\BepInEx\plugins\EterniaCoop"
New-Item -ItemType Directory -Force "$plug\licenses" | Out-Null
Copy-Item "$PSScriptRoot\bin\release\EterniaCoop.dll" $plug
Copy-Item "$PSScriptRoot\README.md","$PSScriptRoot\CREDITS.md","$PSScriptRoot\CHANGELOG.md","$PSScriptRoot\AGENTS.md","$PSScriptRoot\LICENSE" $plug
Copy-Item "$PSScriptRoot\release-assets\licenses\*" "$plug\licenses"
Copy-Item "$PSScriptRoot\README.md" "$stage\EterniaCoop-README.md"
$zip = "$dist\EterniaCoop-$version.zip"
New-Zip "$stage" $zip

# Source package
$src = "$dist\source"
Remove-Item $src -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force "$src\research\ghidra_scripts" | Out-Null
Copy-Item "$PSScriptRoot\src" "$src\src" -Recurse
Copy-Item "$PSScriptRoot\build.ps1","$PSScriptRoot\find-game.ps1","$PSScriptRoot\package.ps1","$PSScriptRoot\README.md","$PSScriptRoot\CREDITS.md","$PSScriptRoot\CHANGELOG.md","$PSScriptRoot\AGENTS.md","$PSScriptRoot\LICENSE" $src
Copy-Item "$PSScriptRoot\research\*.py","$PSScriptRoot\research\*.ps1" "$src\research"
Copy-Item "$PSScriptRoot\research\ghidra_scripts\*.java" "$src\research\ghidra_scripts"
Copy-Item "$PSScriptRoot\release-assets" "$src\release-assets" -Recurse
New-Item -ItemType Directory -Force "$src\bridge" | Out-Null
Copy-Item "$PSScriptRoot\bridge\src" "$src\bridge\src" -Recurse
Copy-Item "$PSScriptRoot\bridge\build.ps1","$PSScriptRoot\bridge\README.md" "$src\bridge"
$srcZip = "$dist\EterniaCoop-$version-source.zip"
New-Zip "$src" $srcZip

Remove-Item $stage, $src -Recurse -Force
Get-ChildItem $dist -Filter *.zip | ForEach-Object { "{0}  {1:N0} KB  SHA256 {2}" -f $_.Name, ($_.Length / 1KB), (Get-FileHash $_.FullName).Hash }
