## Eternia Co-op 1.2.2

Local 2–4 player co-op for **Tales of Eternia Remastered** - each controller drives its own party member in battle, like the PS1 original's multitap mode.

**Download `EterniaCoop-1.2.2.zip`** below and extract it into the game folder (next to `Tales of Eternia Remastered.exe`). BepInEx and the BepInEx 6 Bridge are included. Updating: extract over the old files.

### What's new
- **The battle menu works properly for players 2-4** (from 1.2.1, [#1](https://github.com/thewhitejesus/EterniaCoop/issues/1)): whoever opens it drives the whole menu with their own controller - D-pad, stick, confirm/cancel, camera pan, submenus - like on the PS1.
- **Per-player rumble now actually works for players 2-4.** The game only ever asks for rumble for player 1's character, so players 2-4 never got any. The mod now applies the game's own rumble rule to their characters, on their own controllers. (Not yet tested on a second physical controller - reports welcome!)
- **Steam Deck / Linux:** the zips now use standard paths. Earlier ones unpacked on Linux as files literally named `BepInEx\core\...`.
- **BepInEx 6 Bridge 1.0.1:** it only steps in for the BepInEx 6 mods it runs; other mods behave exactly as without it.

**Don't want co-op?** `BepInEx6Bridge-1.0.1.zip` is the bridge on its own (with BepInEx 5), for running BepInEx 6 mods (Uncropped Backgrounds, Fast Shutdown, Font Replacer) on BepInEx 5.

### Safety
No internet access, telemetry or tracking. No game files are modified (all changes are in memory). Full source in this repository.

See the [README](https://github.com/thewhitejesus/EterniaCoop#readme) and the [CHANGELOG](https://github.com/thewhitejesus/EterniaCoop/blob/main/CHANGELOG.md).

### License and credits
MIT - free to share, modify, fork and include in modpacks; keep the license and credits.
Created by thewhitejesus1 with Claude (Anthropic) as co-developer. Built on BepInEx, HarmonyX, UnityDoorstop and MonoMod - see [CREDITS](https://github.com/thewhitejesus/EterniaCoop/blob/main/CREDITS.md). The bridge contains no BepInEx 6 code and is not affiliated with the BepInEx team or the authors of the mods it runs.
