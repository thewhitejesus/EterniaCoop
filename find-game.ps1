# Finds the Tales of Eternia Remastered install folder: $env:TOE_GAME_DIR if set, otherwise every Steam library
# listed in Steam's libraryfolders.vdf. Dot-source it and call Find-EterniaGame.
function Find-EterniaGame {
    $folder = "Tales of Eternia Remastered"
    if ($env:TOE_GAME_DIR -and (Test-Path "$env:TOE_GAME_DIR\$folder.exe")) { return $env:TOE_GAME_DIR }

    $steam = (Get-ItemProperty "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
    if ($steam) {
        $libraries = @($steam)
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            $libraries += Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object { $_.Matches[0].Groups[1].Value -replace '\\\\', '\' }
        }
        foreach ($lib in $libraries) {
            $dir = Join-Path $lib "steamapps\common\$folder"
            if (Test-Path "$dir\$folder.exe") { return $dir }
        }
    }
    throw "Couldn't find Tales of Eternia Remastered. Set TOE_GAME_DIR to the game folder or pass -Game."
}
