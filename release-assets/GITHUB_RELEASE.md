## Eternia Co-op 1.3.0

Local 2–4 player co-op for **Tales of Eternia Remastered** - each controller drives its own party member in battle, like the PS1 original's multitap mode.

**Download `EterniaCoop-1.3.0.zip`** below and extract it into the game folder (next to `Tales of Eternia Remastered.exe`). BepInEx and the BepInEx 6 Bridge are included. Updating: extract over the old files.

### What's new
- **Survives game updates.** Every spot the mod uses in the game's code now has a code signature. When a game update moves code around, the mod finds each spot again by itself and the main menu says "adapted to a game update". It only accepts a spot when it's certain (found exactly, pointing at the right data); anything else stays off, so nothing is ever half-applied. The search takes about 0.2 seconds and only happens once per game version.
  - Tested by searching every spot from scratch in the running game (all found, with other mods' hooks present) and against simulated updates that move code and data around.
- **Plays nicer with other mods** that hook the game's code (like Guests from Phantasia and Uncropped Backgrounds): the signatures stay clear of the places mods usually hook.
- **Lighter:** no memory allocations in the battle input code, and shortcut/mod checks run once a second instead of every frame.

Includes everything from 1.2.x: the battle menu works for whoever opens it, per-player rumble for players 2-4, Steam Deck-friendly zips, and the BepInEx 6 Bridge (`BepInEx6Bridge-1.0.1.zip` on its own, unchanged).

### Safety
No internet access, telemetry or tracking. No game files are modified (all changes are in memory). Full source in this repository.

See the [README](https://github.com/thewhitejesus/EterniaCoop#readme) and the [CHANGELOG](https://github.com/thewhitejesus/EterniaCoop/blob/main/CHANGELOG.md).

### License and credits
MIT - free to share, modify, fork and include in modpacks; keep the license and credits.
Created by thewhitejesus1 with Claude (Anthropic) as co-developer. Built on BepInEx, HarmonyX, UnityDoorstop and MonoMod - see [CREDITS](https://github.com/thewhitejesus/EterniaCoop/blob/main/CREDITS.md).
