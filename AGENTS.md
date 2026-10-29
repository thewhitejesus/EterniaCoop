# Eternia Co-op - technical guide (for AI assistants and developers)

Read this before changing the mod. It explains how Tales of Eternia Remastered handles input, what the mod
patches, every address it uses, and the lessons that cost the most time to learn.

**Design rule:** restore the PS1 version's behavior. Reuse the game's own native systems (pad builder, command
builder, rumble decision) instead of reimplementing them, and keep everything else player 1-only, as the PS1 did.
On-screen player markers were tried and removed: the PS1 had none and they didn't fit. Manual control mode is
deliberately left locked.

## The game

- Unity 6 (6000.0.66f2), **Mono** scripting backend → BepInEx 5 (x64) + HarmonyX work.
- `Assembly-CSharp.dll` is a thin C# shell ("Ugx" framework: input, graphics, sound). The game itself (battle,
  menus, AI) is native C++ in `Tales of Eternia Remastered_Data\Plugins\x86_64\GameNative.dll`, a port of the
  PSP version, which is a port of the 2000 PS1 game.
- C# calls into GameNative via P/Invoke (`UgxGameNative_Initialize/Update`). GameNative calls back into C# through
  function pointers registered by name with `UgxGeneratedUnityFuncExporter_RegisterUnityFunc(name, fn)`, which
  stores each pointer in a global slot (e.g. `UnityInputFunctions_GetInputInfo` → slot `0x85FF78`).
- Battle code can run on the game's own thread: **no Unity API inside native hooks** (Time, GUI, etc.).
- The game binds **F4** itself (toggles the leader's Auto mode). Don't use F4 for hotkeys.

## How input flows (and where the PSP version cut co-op)

1. **C#:** `UgxInputManager.Initialize(playerNum)` creates one `UgxInputPlayer` per player. The game always passes
   **1** (`UnityInputFunctions.InitializeInput`, IL `ldc.i4.1`).
2. **Native input object** (global pointer `0x6449F8`): `+0x10` player count, `+0x18` remap table (0x1C × 24-byte
   entries per player), `+0x30` buttons (0xAE × 20-byte `UgxInputParam` per player), `+0x48` sticks (2 × 20 per
   player). Setup `FUN_18940` **hardcodes count = 1** (`mov [rdi+0x10], 1` at `0x18A41`) and sizes the stick and
   remap buffers for 1 player.
3. **Poll loop** `FUN_57768` calls `GetInputInfo` / `GetStickInfo` for every player < count, then per-player
   helpers `FUN_57AA0` (button latches) and `FUN_57F20` (Switch A/B remap).
4. **PSP pad:** `FUN_19DBC0(pad, rate, flag)` → `FUN_5D2D0` builds an emulated PSP pad at `0x8C9180` (size 0x144:
   held/pressed/released words, repeat timers, per-button bools) from **player 1's slice** of the input object.
   About 90 functions read this one pad by absolute address. Everything outside battle is player 1-only by design.
5. **Battle commands:** each battle frame `FUN_D2C80` (the battle) calls `FUN_1121E0(&padCopy)`, which turns the pad
   into command bits (attack, artes, guard, left/right...) plus a 15-entry combo history in a 0x20-byte block at
   `0x12CEBA0`. **Each party actor reads its own block through `actor+0xF0`**: slot 0 → `0x12CEBA0`, slot 1 →
   `0x12CEBC0`, slot 2 → `0x12CEBE0`... That is the PS1's 4-player design, intact. The PSP port simply never fills
   blocks 1-3, so a Semi-Auto party member other than the leader stands still.
6. **Control modes:** menu/save byte per party slot at `0x11CAD38 + slot` (0 Auto, 1 Semi-Auto). Battle setup
   copies them into each actor's `+0x57`; the battle dispatches each actor through a handler table at `0x4E8C60`
   indexed by `+0x57`. The battle-results code `FUN_84210` resets slots 1-3 to 0 after every battle.

## What the mod does

| Feature | Where | How |
|---|---|---|
| 4 C# input players | `InputPatches.cs` | Harmony prefix on `UgxInputManager.Initialize` sets `playerNum = 4`. GameNative's setup is the caller, so the native patches are applied here, before it writes its own count. |
| Native player count | `NativePatches.cs` | Immediate operands in `FUN_18940`: count 1 → 4, stick buffer 2 → 8, remap table 0x1C → 0x70 (and its 0x2A0-byte size). |
| Spinning-camera fix | `NativePatches.cs` | `FUN_57AA0`/`FUN_57F20` guards `cmp edx,[rcx+0x10]` → `cmp edx,1` (`0x57AAE`, `0x57F2F`): they share global latch flags and extra players were clearing player 1's. |
| Keep modes after battle | `NativePatches.cs` | NOP the three `mov byte [0x11CAD39/3A/3B], 0` stores at `0x84C91`, `0x84D31`, `0x84DD1`. |
| Menu tab for everyone | `NativePatches.cs` | Skills menu input `FUN_228AD0` and draw `FUN_224920` gate the Control Mode tab on "selected character == 0" (`r10b`/`ecx`): je→jmp ×5, `cmp r10b,1`→4 ×2, one `jae`→NOP, draw `cmp ecx,1`→4. The toggle already uses `mode[selected]`. |
| Co-op battle input | `CoopBattleInput.cs` | MonoMod `NativeDetour` on `FUN_1121E0`. After the original runs for slot 0: for each extra player whose party actor isn't Auto, point the input object's three slice pointers at that player, call `FUN_19DBC0` into a private pad, swap it into `0x8C9180` with that player's previous command state, call the original again, copy the result to the player's block, restore slot 0's pad and block. |
| Target switching for everyone | `CoopBattleInput.cs`, `Battle.cs` | A fresh target-button press (block +4 bit `0x200`) in any party actor's block makes it the targeting actor (`0x12CEB8D` = index + 1); while its block holds the bit, `FUN_D2C80` sets `0x12CEB7A` bit 0 (pause, like the menu) and calls `FUN_113EA0(actor)` (`0xD448D`), which reads left/right from the PSP pad. `Battle.TargetingBlock` tells targeting from the menu (`MenuOpen` excludes it). For a player 2-4, the paused branch keeps running that player (`RunPlayer`), and the call at `0xD448D` goes through a native stub that swaps their pad into `0x8C9180` for the call (`SetTargetPad`). |
| Drop-in/out mid-battle | `CoopBattleInput.cs` | Slots 2-4's actor `+0x57` follows the menu byte, forced to 0 (Auto) while the player has no device. |
| Controller manager | `ControllerManager.cs`, `ManagerView.cs` | Panel (F1 / hold L3+R3; `OpenKey` turns F1 into F8 while 100% Drop & Steal Rate, which uses F1, is loaded). Any controller navigates (D-pad/stick with repeat; A own row, LB/RB move own device, Y automatic, B closes on release). Per-device choice (player 0-3 or `Players.Off` = -1) saved as `path=player;...` in `DeviceAssignments`; `KeyboardPlayer = -1` turns keyboard+mouse off. `Players.PlayerOf` = saved choice else the automatic order. `Players.BlockAll` zeroes every player's input while open. Player 1 may never be left without a device. `ManagerView` draws it on a uGUI canvas with the game's TMP font (`BorderFont_en`, or Font Replacer's `FontOfEternia_Runtime` when loaded) and the game's character art (`face_pic1-4`, copied to RenderTextures while the title screen has them loaded); without the font it falls back to an IMGUI window. Nothing is shipped. |
| Co-op camera | `CoopCamera.cs` | `CamTargetX` (`0xD3BA3`, the battle camera's load of the followed actor's x) becomes a call to a stub returning our centre while `active`. Spring-smoothed centre + zoom (`CamZoom`, capped by `CoopCameraMaxZoom`), standing human characters only (kind 1). `GuardSnap` undoes the menu's snap to actor 0 (closing the battle menu / leaving submenus resets `CamScrollNow` `0x12CEA98` and `CamScroll`): a jump > 32 px landing within 16 px of `FUN_D6420(actor0.x)` is reverted; other jumps (spell targets) pass. |
| Battle menu for everyone | `Players.cs`, `Plugin.cs`, `CoopBattleInput.cs` | The menu button (command bit `0x10`) in any slot's block opens the battle menu and records the owner in `0x12CEB8C` (block + 1); `0x12CEB7A` bit 0 = menu open / paused (Start pauses with no owner). The icon row (`FUN_10F770`) reads the owner's block (+8 pressed buttons/D-pad, +0 held dirs), the submenus read player 1's pad. Players 2-4's pads lack menu latches (helpers are player 1-only), so while the menu is open `Players.ShareWithPlayer1` gives player 1's input the owner's devices only (`Players.MenuOwner`; every device when paused with Start, no owner) and the hook copies block 0 into the owner's block (others cleared, no pad swapping). Another player holding a direction mid-combo can't move the owner's cursor. |
| Field control | `Players.cs`, `Plugin.cs`, `ControllerManager.cs` | `Settings.FieldControl` (manager's last row). While no battle runs (`Battle.Actors()` is zero), `Plugin.Update` sets `Players.FieldPlayer` and `Desired` gives player 1's input that player's devices (only if they have any); reassigned the moment a battle starts or ends. |
| Device assignment | `Players.cs` | Once a second: `SetInputDevices` on each `UgxInputPlayer` (keyboard+mouse to `KeyboardPlayer`, controllers in deviceId order). Players without a device get zeroed input (`InputPatches.cs`) - otherwise an empty device list listens to every device and mirrors player 1. |
| Slot modes follow controllers | `SlotModes.cs` | Writes `JoinMode`/0 to `0x11CAD38+slot` when a player gains/loses a device or the party changes (save load). A human slot already on Semi-Auto/Manual is never overwritten; a slot set to Auto because its controller dropped remembers its mode and gets it back on reconnect (forgotten when the slot's character changes). Manual (2) is offered by the game's Skills tab only when the global flag `0x11CAE9A` is set (max choice = flag + 1); the mod doesn't change that. |
| Per-player rumble | `RumbleRouter.cs` | Detour on `FUN_8E320(type, flags, hit, attacker, victim)`. At its end the game requests rumble (strength 0.6, 6 frames, played on player 1) only when the option `[0x6449E0]+0x21` is on and the attacker - else the victim - is the party actor in command block 0 (`+0x56 == 0`): players 2-4's characters never rumble. The hook leaves that alone and applies the same rule to blocks 1-3 (skipped while paused, like the game): `UgxInputManager.SetVibration(0.6, player)` / `StopVibration`, queued for `Update`. |
| Compatibility check | `Compatibility.cs`, `Sites.cs`, `Features.cs` | SHA-256 of `GameNative.dll` vs `KnownSha256` decides the label (and, with `StrictVersionCheck`, whether to stay off). The gate is `Features.Check`, run in the `Initialize` prefix before anything is patched: `Sites.Resolve` finds every site by signature (known address, else cache, else search; on the known build a site whose neighbours another mod hooked is accepted if its own instruction matches), `Rva` takes the found addresses, and each feature (Core, KeepModes, MenuTab, CoopBattle, Rumble, Camera) is on only if all its sites resolved (plus cross-checks: Pad/PadHeld, BattleFlags/HitPaused). Everything needs Core. Patches re-check their instruction bytes. |
| Watermark | `Plugin.cs` | IMGUI label, top-right, main menu only: hidden while the area id at `0x11CAFC4` is non-zero (it is 0 on the title screen; the party list is *not* empty there - it holds Reid). |

## Source layout

```
src/
  Plugin.cs           BepInEx entry, per-frame loop, watermark
  Settings.cs         all config entries
  Native.cs           Rva (every address), Actor (field offsets), Native (module base, safe patching)
  Compatibility.cs    GameNative.dll fingerprint, watermark status text
  OtherMods.cs        other loaded mods for the watermark (BepInEx plugins + native modules in the game folder)
  Sites.cs            finds every GameNative spot by code signature (survives game updates), caches per build
  Sites.Generated.cs  the signatures - generated by research/gen_sigs.py, don't edit by hand
  Features.cs         which sites each feature needs: which features are safe to turn on
  Players.cs          device -> player assignment (automatic order + saved manual choices)
  ControllerManager.cs  in-game controller manager (input, assignment, IMGUI fallback window)
  ManagerView.cs      the manager's game-styled window (uGUI + the game's fonts and character art)
  CoopCamera.cs       co-op battle camera
  InputPatches.cs     Harmony patches on the C# input layer
  NativePatches.cs    code patches in GameNative
  Battle.cs           actor helpers (party slot from command block pointer)
  CoopBattleInput.cs  battle input hook
  SlotModes.cs        menu-side control modes
  RumbleRouter.cs     rumble hook
  Debug/ResearchTools.cs   debug build only (COOP_DEBUG): memory scan, peek/poke, dumps, verbose logs
bridge/               BepInEx 6 Bridge (separate plugin, shipped in the same zip): runs BepInEx 6 plugins on BepInEx 5.
  src/Core/           stand-in for BepInEx.Core 6.0 (attributes, config forwarded to BepInEx 5, logging + interpolated handlers)
  src/UnityMono/      stand-in for BepInEx.Unity.Mono 6.0 (BaseUnityPlugin: Config + Logger from [BepInPlugin])
  src/Loader/         the BepInEx 5 plugin: resolves those assemblies to the stand-ins, finds and starts BepInEx 6 plugins
  build.ps1           builds all three (BepInEx 5's BepInEx.dll is referenced under the extern alias BE5)
build.ps1             csc build (release or -DebugBuild), installs into the game
research/             analysis scripts (Capstone/pefile, Ghidra headless); decomp output is not shipped
```

The build uses the .NET Framework `csc.exe` that ships with Windows, so the code is **C# 5**: no `$""`, `?.`,
`nameof`, expression-bodied members or auto-property initializers. References: the game's `Managed\` DLLs plus
`BepInEx\core` (BepInEx, 0Harmony, MonoMod.RuntimeDetour, MonoMod.Utils).

## Address reference (GameNative.dll, SHA-256 6488BE2C...849B)

| RVA | Meaning |
|---|---|
| `0x18940` | `FUN_18940` input setup (count, buffer sizes) |
| `0x57768` / `0x57AA0` / `0x57F20` | poll loop / button-latch helper / Switch remap helper |
| `0x5D2D0` / `0x19DBC0` | input → raw PSP pad / pad builder (`pad, rate, flag`) |
| `0x1121E0` | battle command builder (pad → slot 0 block) |
| `0xD2C80` | battle main; actor loop over 13 actors, handler table `0x4E8C60[actor+0x57]` |
| `0x84210` | battle results (writes modes back, resets slots 1-3) |
| `0x8E320` | apply hit (rumble decision at its end, `0x8E895`) |
| `0x228AD0` / `0x224920` | Skills menu input / draw |
| `0xD6420` | battle camera scroll from x: `-(x >> 4)`, clamped to the field; stored at `0x12CEAB8` |
| `0x12CEB78` / `0x12CEB7F` | battle camera zoom setting (0x200 closest .. 0x29C game max) / camera mode (0 normal, >= 2 scripted) |
| `0x12CEAB8` / `0x12CEA98` | camera scroll target / scroll shown now (eases toward the target) |
| `0x11CB80` / `0x1A07D0` | PS1 GTE-style object transform / perspective projection |
| `0x6449F8` | input object pointer |
| `0x8C9180` | PSP pad (0x144 bytes) |
| `0x11CAD10` | party member ids (4 × short) |
| `0x11CAFC4` | current area (2 × short); 0 on the title screen |
| `0x11CAD38` | control mode per party slot (byte) |
| `0x111A674` | Skills menu: selected character |
| `0x12CEBA0` | battle command blocks (0x20 per slot) |
| `0x12CECD8` | pointer to battle actors (13 × 0x248) |
| `0x862E72` / `0x862E78` | rumble request frames / strength (player 1) |
| `0x6449E0` | game options object (pointer); `+0x21` rumble on |
| `0x12CEB7A` / `0x12CEB8C` | battle flags (bit 0 menu open / paused) / battle menu owner (block + 1) |
| `0x12CEB8D` / `0x113EA0` | targeting actor (index + 1) / target switch (`FUN_113EA0(actor)`, called at `0xD448D`) |
| `0x85FF78` / `0x85FF70` / `0x85FF68` | callback slots: GetInputInfo / GetStickInfo / InitializeInput |

Actor fields: `+0x10` x, `+0x12` y (world units = 1/16 PS1 pixel; battle view is 320 px wide), `+0x3C` kind
(1 = character), `+0x51` side (0 party), `+0x56` command block index (0 = player 1's), `+0x57` control mode, `+0xF0` command block.

## Lessons learned (don't repeat these)

- **Don't leave the remap table at 1-player size** with count > 1: the load-save menu reads it per player and crashes.
- **Raising the count without the helper fix spins the battle/field camera**: the per-player helpers share global
  latches. Blanking players 2-4's input does *not* fix it.
- **`0x576F20` (battle mode copy) is not live during battle.** Read control modes from the actors (`+0x57`).
- **The debug-info exports (`DebugInfoFunctions_*`) are all stubs** returning 0 in the release build.
- **`FUN_11CB80(rot, pos)` places an object in front of the camera; it isn't the camera.** Battle drawing is 2D:
  screen x = x/16 + scroll(`0x12CEAB8`) + 160.
- **Don't use the Input System's `wasPressedThisFrame` in plugin code here** - controller presses were missed (keyboard
  presses mostly worked) even with updateMode = ProcessEventsInDynamicUpdate. Compare `isPressed` with the previous
  Update's value instead (`ControllerManager.Pressed`).
- **Native hooks may run off the Unity main thread.** Queue work (as `RumbleRouter` does) for `Update`.
- **Adding a patch, hook or data address? Add a site to `SITES` in `research/gen_sigs.py`** (for an address, any
  instruction that reaches it RIP-relative - `gn.py refs`), regenerate, and list it in its feature in `Features.cs`.
  Never hardcode an RVA in code: take it from `Sites` / `Rva`, or it won't follow a game update.
- **Signatures stay out of a function's first 16 bytes** (`HOOK_ZONE`): other mods hook functions there (something
  hooks `FUN_110120`), and a signature covering a hook stops matching.
- **PowerShell `Set-Content` writes a BOM; BepInEx config tolerates it, other tools might not.**
- **Zooming the battle camera past ~730 shows a band below the picture.** The zoom is a real camera distance; each
  stage's background routine (table `0x4E8E90`, dispatched by `FUN_10D500`) draws its ground as a perspective grid of
  quads (`FUN_11E6F0`) only as far as the PS1 screen needed. A ground extension (homography fit of the grid, rows
  continued downward, the stages' black under-ground fill `FUN_120F70` skipped) works, but the remaster renders the
  battle into a fixed viewport (e.g. 2560x1011 from y 174 on 1440p): anything below it never shows, scissor or not.
  Parked in `research/wip/` until the viewport (and its projection) can be extended like Uncropped Backgrounds does
  sideways.

## Updating after a game patch

The mod adapts to most updates by itself: every spot it uses has a code signature (`src/Sites.Generated.cs`), and on
an unknown `GameNative.dll` `Sites.Resolve` searches for each one (unique match / exact copy count / constraint,
data addresses decoded from the instruction found). The watermark then says "adapted to a game update"; results are
cached per build in `BepInEx\cache\EterniaCoop-sites-<hash>.txt`. To make the new build the supported one:

1. `LogOutput.log` lists every moved site as `Name: 0xOLD -> 0xNEW` (and sites not found). Sites not found need
   research: re-find them by their old signature / code pattern with `research/gn.py` (`refs` finds RIP-relative
   anchors for a data address) or Ghidra (`research/decomp.ps1`).
2. Put the new addresses (and data targets) into `SITES` in `research/gen_sigs.py`, run `python -I gen_sigs.py`
   (regenerates `src/Sites.Generated.cs`; it fails if any signature isn't unique), then update the defaults in `Rva`
   and `Compatibility.KnownSha256`.
3. Check with `python -I sim_update.py 1` (simulated update) and a debug build (its site self-test searches every
   site from scratch and must report N/N). Retest: menu load (remap crash), field camera (spin), battle with 2+
   players, battle menu as player 2, battle end (modes kept), Skills menu tab for slot 2, save + reload.

## Research workflow

- `build.ps1 -DebugBuild` enables the tools in `src/Debug/ResearchTools.cs` (hotkeys listed at the top of that file),
  e.g. F5/F6/F7 memory scan to find a byte that flips with an in-game toggle, Numpad 1 to dump battle actors.
- `research/gn.py` (Python + Capstone + pefile): `dis`, `func`, `refs` (xref index over .pdata functions), `export`.
- `research/decomp.ps1 <out.c> <rva...>`: Ghidra headless decompile using `research/ghidra_scripts/DecompileAt.java`
  (expects a tools folder - `$env:EC_TOOLS`, default `..\tools` beside the repo - holding Ghidra in `ghidra\`,
  JDK 21 in `jdk\` and the analyzed project in `ghidra_projects\`).
- Scripts find the game through Steam; set `TOE_GAME_DIR` to override (`find-game.ps1`, `research/gn.py`).
- Don't redistribute decompiled game code; `research/decomp/` stays local.

## Testing checklist

One controller + keyboard is enough for most tests (`KeyboardPlayer = 1` makes the keyboard player 2):
title → load save (no crash), field (camera still), battle (player 2 controls slot 2), battle end (Semi kept),
Skills menu (tab visible for slot 2), unplug player 2 mid-battle (character goes Auto), rumble on player 1's hits
only. With real extra controllers or Parsec: players 3-4, rumble on their own controllers.
