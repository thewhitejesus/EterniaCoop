## Eternia Co-op 1.1.2

Local 2–4 player co-op for **Tales of Eternia Remastered** - each controller drives its own party member in battle, like the PS1 original's multitap mode.

**Download `EterniaCoop-1.1.2.zip`** below and extract it into the game folder (next to `Tales of Eternia Remastered.exe`). BepInEx is included. Updating: extract over the old files.

### What's new
- **Tested with other mods.** Works together with **100% Drop and Steal Rate** and **Guests from Phantasia**. Their shortcuts no longer clash: while they're installed, the controller manager opens with **F8** (instead of F1) and **LB + RB + Back** (instead of L3 + R3). The main menu tells you when this happens.
- **New main-menu label** in the same small style as other mods' status lines, with an "Also running: ..." line listing the other mods you have installed.
- Controller manager: LB / RB act when released, so closing it with LB + RB + Back doesn't move your controller.
- Safety fixes: co-op only turns on once the game-code check has confirmed it, and the label never reads game memory the check hasn't confirmed.

### Compatibility
- ✅ 100% Drop and Steal Rate, Guests from Phantasia (with 4 players, set `ReplaceFourth=0` in `ToE_Guest.ini` so the guest doesn't take player 4's character).
- ❌ Uncropped Backgrounds and Fast Shutdown need BepInEx 6; this mod uses BepInEx 5, and a game can only run one.

### Features
- Up to 4 players in battle; player 1 controls the leader as usual.
- Controllers are assigned automatically; join or drop out mid-battle.
- Controller manager to choose which controller is which player.
- Semi-Auto/Auto tab in the Skills menu for every party member; modes stay set after battles.
- Per-player rumble. Works over Parsec.
- Checks the game code before changing anything and skips only what a game update or another mod changed.

### Safety
No internet access, telemetry or tracking. No game files are modified (all changes are in memory). Full source in this repository.

See the [README](https://github.com/thewhitejesus/EterniaCoop#readme) for settings and troubleshooting, and the [CHANGELOG](https://github.com/thewhitejesus/EterniaCoop/blob/main/CHANGELOG.md) for all changes.

### License and credits
MIT - free to share, modify, fork and include in modpacks; keep the license and credits.
Created by thewhitejesus1 with Claude (Anthropic) as co-developer. Built on BepInEx, HarmonyX, UnityDoorstop and MonoMod - see [CREDITS](https://github.com/thewhitejesus/EterniaCoop/blob/main/CREDITS.md).
