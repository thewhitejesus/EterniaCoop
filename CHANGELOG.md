# Changelog

## 1.4.1 - 2026-10-24
- **Players 2-4 can switch targets** (hold R1 and press left/right), like on the PS1. Holding the target button
  pauses the battle the same way the battle menu does, and the mod mistook it for the menu and cleared that player's
  buttons, ending it at once; and the game reads the direction from player 1's controller only. The targeting
  player's input now keeps running, and the target switch reads their own controller. Thanks to Jenon08 for the report.

## 1.4.0 - 2026-10-22
- **Co-op camera:** with two or more human players, the battle camera centres between their characters and zooms
  out as they spread apart (back in, after a moment, as they come together), with smooth spring easing and a little
  look-ahead. Player 1's character is kept in view first if everyone can't fit. Knocked-out characters aren't
  framed, so with player 1 down the camera follows the others. Spell cameras and the battle menu's manual pan are
  the game's own. Settings: `CoopCamera`, `CoopCameraMaxZoom` (default 720 - beyond about 730 the view reaches past
  the bottom of the battle picture).
- The camera no longer swings back to player 1 when the battle menu or its submenus close, and still moves to show a
  spell's target.
- **Controller manager, redone:** any controller can now move through the whole list (D-pad or left stick, held
  directions repeat) instead of only moving itself; A jumps to your own controller, Y = automatic, B closes. Drawn
  in Tales of Eternia's own style (the arte-name window, formation arrows, yellow highlights) with the game's font -
  or Font of Eternia (by Jyrakos) when Font Replacer (by vitruvianesse) is installed - and Reid, Farah, Meredy and
  Keele from the game's own character art behind it. Neither the font nor any art is included: both come from your
  installed game and mods while it runs.
- **Devices can be turned Off** (in the manager, or `KeyboardPlayer = -1` for keyboard and mouse): for Parsec, where
  guests' keyboards act as the host's.

## 1.3.0 - 2026-10-22
- **Survives game updates.** Every spot the mod uses in the game's code (46 code spots, 11 data addresses) now has a
  code signature. When a game update moves code, the mod searches for each one and accepts it only if it's found
  exactly (the right number of times, pointing at the right data) - then the main menu says "adapted to a game
  update". Anything it can't find with certainty stays off, as before. Results are remembered per game version, so
  the search happens once (about 0.2 s). Tested by searching every spot from scratch in the running game and against
  simulated updates that move code and data.
- Signatures avoid the first bytes of functions, where other mods hook the game, so mods like Guests from Phantasia
  and Uncropped Backgrounds don't get in the way.
- Faster: no memory allocations in the battle input code, controller-manager shortcuts and mod checks worked out
  once a second instead of every frame, cached lookups for rumble and controller assignment.

## 1.2.2 - 2026-10-22
- **Per-player rumble now works for players 2-4.** The game only ever asks for rumble for player 1's character, so
  the old version had nothing to pass on to players 2-4. The mod now applies the game's own rumble rule (rumble on in
  the options; the character that hit, else the one that got hit; same strength) to players 2-4's characters, on
  their own controllers. Player 1's rumble is left entirely to the game.
- **Steam Deck / Linux:** the release zips use standard "/" paths. The old ones (made with Windows PowerShell)
  unpacked on Linux as files literally named `BepInEx\core\...`.
- BepInEx 6 Bridge 1.0.1: its library-lookup fallback only answers the BepInEx 6 mods it runs, so other mods behave
  exactly as without it.
- Controller manager: no crash path when no input device is present at all.

## 1.2.1 - 2026-10-22
- **Battle menu fixed properly for players 2-4** (GitHub issue #1). Like the PS1, the battle menu belongs to whoever
  opens it, and its icon row reads that player's input - but players 2-4's menu presses never registered (D-pad,
  confirm, cancel), and 1.1.3 also cut off their stick and camera pan. Now the opener's own controller drives the whole
  battle menu: D-pad and stick, confirm/cancel, camera pan (hold Square) and the submenus. Tested with a controller as
  player 2.

## 1.2.0 - 2026-10-22
- **BepInEx 6 mods work alongside co-op.** New, included **BepInEx 6 Bridge**: runs mods built for BepInEx 6 on
  BepInEx 5, so co-op, 100% Drop and Steal Rate, Uncropped Backgrounds, Fast Shutdown and Font Replacer all run
  in one install (all tested together, with Guests from Phantasia). It's a separate plugin folder and does nothing
  when no BepInEx 6 mods are installed. Also released on its own.
- The main menu's "Also running" line shows friendly names for those mods.

## 1.1.3 - 2026-10-22
- **Fixed a soft-lock:** players 2-4 could open the battle menu (Start, or Tab on the keyboard), but the remaster's
  battle menu only takes player 1's input, so they couldn't confirm, cancel or close it (GitHub issue #1). Now every
  controller and the keyboard/mouse count as player 1 while the battle menu is open, so whoever opened it can use it
  and close it; everyone goes back to their own player when it closes.
- Steam Deck / Linux (Proton) instructions: the `WINEDLLOVERRIDES="winhttp=n,b" %command%` launch option BepInEx
  needs there, plus Deck controller notes. The log says when the mod runs under Wine/Proton. Not tested on a Deck yet.

## 1.1.2 - 2026-10-21
- Works with **Guests from Phantasia** without a shortcut clash: it toggles its guest with L3+R3, so while it's
  installed the controller manager's controller shortcut is **LB+RB+Back** instead. The label and manager say so.
- Controller manager: LB / RB now act when released, so closing with LB+RB+Back no longer moves the controller.
- Main-menu label redesigned to match other mods' status lines (small, white, name in bold) and now lists the other
  mods running alongside ("Also running: ..."). Tested with 100% Drop and Steal Rate and Guests from Phantasia.
- Works with **100% Drop and Steal Rate** (tested together): that mod opens its menu with F1, so while it's installed
  the controller manager opens with **F8** instead. The main-menu label and the manager say so.
- Co-op stays off unless the game code check has actually confirmed the input code (it could be skipped before
  in theory).
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
