# Credits

## Eternia Co-op
- **[thewhitejesus1](https://www.nexusmods.com/profile/thewhitejesus1)** - creator: design, testing, and the push to bring back the PS1's co-op.
- **Claude** (Anthropic's AI assistant) - co-developer: reverse engineering of GameNative.dll, the mod's code and
  the documentation, working with thewhitejesus1.

## Bundled in the release (thank you!)
| Component | Authors | License | Used for |
|---|---|---|---|
| [BepInEx](https://github.com/BepInEx/BepInEx) | BepInEx team | LGPL-2.1 | Mod loader |
| [UnityDoorstop](https://github.com/NeighTools/UnityDoorstop) | NeighTools | LGPL-2.1 | `winhttp.dll` that starts BepInEx |
| [HarmonyX](https://github.com/BepInEx/HarmonyX) | BepInEx team (based on [Harmony](https://github.com/pardeike/Harmony) by Andreas Pardeike) | MIT | Patching the game's C# input code |
| [MonoMod](https://github.com/MonoMod/MonoMod) | 0x0ade and contributors | MIT | Hooking the game's native battle and rumble functions |

Their license texts are in `licenses\`.

## Inspiration
- [arise-multiplayer](https://github.com/EusthEnoptEron/arise-multiplayer) by **EusthEnoptEron** - local
  multiplayer for Tales of Arise; showed that bringing co-op to a single-player Tales port is possible.

## Research tools (not bundled)
Used to understand the game: [Ghidra](https://github.com/NationalSecurityAgency/ghidra) (NSA, Apache-2.0),
[Capstone](https://www.capstone-engine.org/) (BSD), [pefile](https://github.com/erocarrera/pefile) (MIT),
Eclipse Temurin JDK (GPL-2.0 with Classpath Exception).

## The game
*Tales of Eternia* © Bandai Namco Entertainment Inc. Originally developed by Wolfteam / Namco Tales Studio
(PlayStation, 2000); *Tales of Eternia Remastered* developed by TOSE. This is an unofficial fan mod, not
affiliated with or endorsed by Bandai Namco Entertainment. It contains no game code or assets.
