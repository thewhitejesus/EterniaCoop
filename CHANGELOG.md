# Changelog

## Unreleased
- Works with **Guests from Phantasia** without a shortcut clash: it toggles its guest with L3+R3, so while it's
  installed the controller manager's controller shortcut is **LB+RB+Back** instead. The label and manager say so.
- Controller manager: LB / RB now act when released, so closing with LB+RB+Back no longer moves the controller.
- Main-menu label redesigned to match other mods' status lines (small, white, name in bold) and now lists the other
  mods running alongside ("Also running: ..."). Tested with 100% Drop and Steal Rate and Guests from Phantasia.
- Works with **100% Drop and Steal Rate** (tested together): that mod opens its menu with F1, so while it's installed
  the controller manager opens with **F8** instead. The main-menu label and the manager say so.
- Co-op stays off unless the game code check has actually confirmed the input code (it could be skipped before
  in theory).
- The main-menu label uses the full screen width and no longer wraps, so a long "partly on (off: ...)" status fits.
- The label only reads the game's area id once that address is confirmed, so an unrecognized game version can't
  make it read the wrong memory.

## 1.1.1 - 2026-10-21
- Works with other mods that change the game's code, and with small game updates. Instead of switching off whenever
  `GameNative.dll` differs from the supported build, each feature (co-op players, battle input, rumble, Skills menu
  tab, keeping modes after battle) checks every piece of game code it uses and turns on only if all of it is
  unchanged. Features are all-or-nothing, so none can end up half-applied. The main-menu label says which are off.
- `StrictVersionCheck` setting brings back the old behavior (stay off unless the game file is exactly the supported
  build). It replaces `AllowUnknownGameVersion`.

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
