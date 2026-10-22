## Eternia Co-op 1.1.3

Local 2–4 player co-op for **Tales of Eternia Remastered** - each controller drives its own party member in battle, like the PS1 original's multitap mode.

**Download `EterniaCoop-1.1.3.zip`** below and extract it into the game folder (next to `Tales of Eternia Remastered.exe`). BepInEx is included. Updating: extract over the old files.

### What's new
- **Fixed a battle-menu soft-lock** ([#1](https://github.com/thewhitejesus/EterniaCoop/issues/1), thanks Oofatable!). Players 2-4 could open the battle menu (Start, or Tab on the keyboard), but the remaster's battle menu only listens to player 1, so they couldn't confirm, cancel or close it. Now every controller and the keyboard/mouse count as player 1 while the battle menu is open, so **whoever opens it can use it and close it**. Everyone goes back to their own character when it closes. Tested with the keyboard as player 2 and player 3.
- **Steam Deck / Linux (Proton):** setup instructions in the README - BepInEx needs the launch option `WINEDLLOVERRIDES="winhttp=n,b" %command%` there. Not tested on a Deck yet, reports welcome.

### Compatibility
- ✅ 100% Drop and Steal Rate, Guests from Phantasia (with 4 players, set `ReplaceFourth=0` in `ToE_Guest.ini`).
- ✅ ReShade presets should work (ReShade hooks the graphics, not the game code) - not tested yet.
- ❌ Uncropped Backgrounds, Font Replacer and Fast Shutdown need BepInEx 6; this mod uses BepInEx 5, and a game can only run one.

### Safety
No internet access, telemetry or tracking. No game files are modified (all changes are in memory). Full source in this repository.

See the [README](https://github.com/thewhitejesus/EterniaCoop#readme) for settings and troubleshooting, and the [CHANGELOG](https://github.com/thewhitejesus/EterniaCoop/blob/main/CHANGELOG.md) for all changes.

### License and credits
MIT - free to share, modify, fork and include in modpacks; keep the license and credits.
Created by thewhitejesus1 with Claude (Anthropic) as co-developer. Built on BepInEx, HarmonyX, UnityDoorstop and MonoMod - see [CREDITS](https://github.com/thewhitejesus/EterniaCoop/blob/main/CREDITS.md).
