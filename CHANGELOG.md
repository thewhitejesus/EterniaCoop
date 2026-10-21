# Changelog

## 1.1.0 - 2026-10-20
- Controller manager: press F1 (or hold L3 + R3 on any controller) to choose which device controls which player.
  Controllers use LB/RB or the D-pad, the keyboard uses the arrow keys; choices are saved per device.
- The game ignores input while the manager is open.
- `ManagerKey` and `ManagerControllerShortcut` settings to change the manager's shortcuts (e.g. LB+RB+Back for use
  alongside Guests from Phantasia, which also uses L3+R3).
- Main-menu watermark mentions the F1 shortcut.

## 1.0.0 - 2026-10-20
First release.
- Up to 4 players: controllers 2–4 control party slots 2–4 in battle (Semi-Auto), using the battle engine's
  per-character input slots left over from the PS1 version.
- Automatic controller assignment with hot-plug; join or drop out mid-battle (dropped players' characters go Auto).
- Semi-Auto/Auto tab in the Skills menu for every party member.
- Control modes no longer reset to Auto after each battle.
- Per-player rumble for each character's hits.
- Startup compatibility check: the mod only patches the game build it was made for and otherwise stays off.
- Main-menu watermark showing the mod is running (or why it's off); hidden in game.
- Separate debug build with the research tools used to reverse-engineer the game.
