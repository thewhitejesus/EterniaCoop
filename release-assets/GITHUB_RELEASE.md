## Eternia Co-op 1.2.1

Local 2–4 player co-op for **Tales of Eternia Remastered** - each controller drives its own party member in battle, like the PS1 original's multitap mode.

**Download `EterniaCoop-1.2.1.zip`** below and extract it into the game folder (next to `Tales of Eternia Remastered.exe`). BepInEx and the BepInEx 6 Bridge are included. Updating: extract over the old files.

### What's fixed
- **The battle menu now works properly for players 2-4** ([#1](https://github.com/thewhitejesus/EterniaCoop/issues/1), thanks Oofatable for the detailed follow-up!). Like on the PS1, the battle menu belongs to whoever opens it - but players 2-4's D-pad, confirm and cancel never registered there, and 1.1.3 also cut off their stick and camera pan. Now the opener's own controller drives the whole battle menu: D-pad and stick, confirm/cancel, camera pan (hold Square) and the submenus, and they get their character back when it closes. Tested with a controller as player 2.

### Also in 1.2.x
- **BepInEx 6 mods work alongside co-op** through the included BepInEx 6 Bridge (Uncropped Backgrounds, Fast Shutdown, Font Replacer - tested together with 100% Drop and Steal Rate and Guests from Phantasia). `BepInEx6Bridge-1.0.0.zip` is the bridge on its own, unchanged, from the [1.2.0 release](https://github.com/thewhitejesus/EterniaCoop/releases/tag/v1.2.0).

### Safety
No internet access, telemetry or tracking. No game files are modified (all changes are in memory). Full source in this repository.

See the [README](https://github.com/thewhitejesus/EterniaCoop#readme) and the [CHANGELOG](https://github.com/thewhitejesus/EterniaCoop/blob/main/CHANGELOG.md).

### License and credits
MIT - free to share, modify, fork and include in modpacks; keep the license and credits.
Created by thewhitejesus1 with Claude (Anthropic) as co-developer. Built on BepInEx, HarmonyX, UnityDoorstop and MonoMod - see [CREDITS](https://github.com/thewhitejesus/EterniaCoop/blob/main/CREDITS.md).
