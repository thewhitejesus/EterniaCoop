## Eternia Co-op 1.2.0

Local 2–4 player co-op for **Tales of Eternia Remastered** - each controller drives its own party member in battle, like the PS1 original's multitap mode.

**Download `EterniaCoop-1.2.0.zip`** below and extract it into the game folder (next to `Tales of Eternia Remastered.exe`). BepInEx is included. Updating: extract over the old files.

### What's new: BepInEx 6 mods work alongside co-op
Some Tales of Eternia mods are built for BepInEx 6 while co-op uses BepInEx 5, and a game can only run one. The download now includes the **BepInEx 6 Bridge**, which runs BepInEx 6 mods on BepInEx 5 - no more choosing.

**Tested together in one install:** Eternia Co-op, 100% Drop and Steal Rate, Guests from Phantasia, **Uncropped Backgrounds 1.1.3**, **Fast Shutdown** and **Font Replacer** (with Font of Eternia).

- Install the BepInEx 6 mods into `BepInEx\plugins\` as their pages say - just the mod, not BepInEx 6 itself.
- **Coming from BepInEx 6?** Extract this zip over it: the game switches to BepInEx 5's loader and your mods and settings stay.
- The bridge is its own plugin folder (`BepInEx\plugins\BepInEx6Bridge`), does nothing when no BepInEx 6 mods are installed, and never modifies other mods' files. Details: [bridge/README.md](https://github.com/thewhitejesus/EterniaCoop/blob/main/bridge/README.md).

**Don't want co-op?** `BepInEx6Bridge-1.0.0.zip` is the bridge on its own (with BepInEx 5).

Also: the main menu's "Also running" line shows friendly names for those mods.

### Features
- Up to 4 players in battle; controllers assigned automatically; join or drop out mid-battle.
- Controller manager to choose which controller is which player.
- Semi-Auto/Auto tab for every party member; modes stay set after battles; per-player rumble.
- Whoever opens the battle menu can use and close it. Works over Parsec. Steam Deck notes in the README.

### Safety
No internet access, telemetry or tracking. No game files are modified (all changes are in memory). Full source in this repository.

See the [README](https://github.com/thewhitejesus/EterniaCoop#readme) and the [CHANGELOG](https://github.com/thewhitejesus/EterniaCoop/blob/main/CHANGELOG.md).

### License and credits
MIT - free to share, modify, fork and include in modpacks; keep the license and credits.
Created by thewhitejesus1 with Claude (Anthropic) as co-developer. Built on BepInEx, HarmonyX, UnityDoorstop and MonoMod - see [CREDITS](https://github.com/thewhitejesus/EterniaCoop/blob/main/CREDITS.md). The bridge contains no BepInEx 6 code and is not affiliated with the BepInEx team or the authors of the mods it runs.
