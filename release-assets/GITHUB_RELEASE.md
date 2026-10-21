## Eternia Co-op 1.1.1

Local 2–4 player co-op for **Tales of Eternia Remastered** - each controller drives its own party member in battle, like the PS1 original's multitap mode.

**Download `EterniaCoop-1.1.1.zip`** below and extract it into the game folder (next to `Tales of Eternia Remastered.exe`). BepInEx is included. Updating from 1.1.0: extract over the old files.

### What's new
- **Works alongside other mods.** 1.1.0 switched itself off completely if anything had changed the game's code file. Now each feature (co-op players, battle input, rumble, Skills menu tab, keeping modes after battle) checks every bit of game code it uses and only stays off if that code was changed - everything else keeps working. No feature can end up half-applied.
- The main-menu label tells you if some features are off; `BepInEx\LogOutput.log` says exactly why.
- New `StrictVersionCheck` setting for the old all-or-nothing behavior (replaces `AllowUnknownGameVersion`).

Thanks to the Reddit commenter who asked for this!

### Features
- Up to 4 players in battle; player 1 controls the leader as usual.
- Controllers are assigned automatically; join or drop out mid-battle (a dropped player's character goes back to AI).
- Controller manager (F1, or hold L3 + R3) to choose which controller is which player.
- Semi-Auto/Auto tab in the Skills menu for every party member; modes stay set after battles.
- Per-player rumble.
- Works over Parsec (guests' controllers become players 2–4).

### Safety
No internet access, telemetry or tracking. No game files are modified (all changes are in memory). Full source in this repository.

See the [README](https://github.com/thewhitejesus/EterniaCoop#readme) for settings and troubleshooting, and the [CHANGELOG](https://github.com/thewhitejesus/EterniaCoop/blob/main/CHANGELOG.md) for all changes.

### License and credits
MIT - free to share, modify, fork and include in modpacks; keep the license and credits.
Created by thewhitejesus1 with Claude (Anthropic) as co-developer. Built on BepInEx, HarmonyX, UnityDoorstop and MonoMod - see [CREDITS](https://github.com/thewhitejesus/EterniaCoop/blob/main/CREDITS.md).
