# BepInEx 6 Bridge (for Tales of Eternia Remastered)

Runs **BepInEx 6 plugins on BepInEx 5**, so mods built for either version work in one install.

Tales of Eternia Remastered mods are split between the two BepInEx versions, and a game can only run one of them:

| BepInEx 5 | BepInEx 6 |
|---|---|
| Eternia Co-op, 100% Drop and Steal Rate, Thai translation | Uncropped Backgrounds, Fast Shutdown, Font Replacer |

With the bridge you stay on BepInEx 5 and the BepInEx 6 plugins run too. **Tested** together with Eternia Co-op,
100% Drop and Steal Rate and Guests from Phantasia: Uncropped Backgrounds 1.1.3, Fast Shutdown 0.1.0 and Font
Replacer 0.1.0 (with Font of Eternia).

Unofficial - not made by or affiliated with the BepInEx team or the authors of those mods.

## Install

1. Install BepInEx 5 (the Eternia Co-op download includes it and the bridge).
   **Coming from BepInEx 6?** Extracting the zip over it switches the game back to BepInEx 5's loader
   (`winhttp.dll`, `doorstop_config.ini`); your plugins and their settings stay where they are.
2. Put the bridge in `BepInEx\plugins\BepInEx6Bridge\` (the zip does this).
3. Put BepInEx 6 plugins in `BepInEx\plugins\` as their own instructions say - **unchanged**, from their own pages.
4. Start the game. `BepInEx\LogOutput.log` shows `[BepInEx 6 Bridge] Started BepInEx 6 plugin ...` for each one.

To remove it, delete the `BepInEx6Bridge` folder. Nothing else is changed.

## How it works

A BepInEx 6 plugin references two BepInEx 6 libraries, `BepInEx.Core` and `BepInEx.Unity.Mono`. The bridge ships
small stand-ins for them (`shim\`) that provide what this game's BepInEx 6 plugins use, built on BepInEx 5:

- the plugin base class and `[BepInPlugin]` / `[BepInDependency]` / `[BepInProcess]` attributes,
- configuration (`Config.Bind` etc.) - the same API in both versions, handed straight to BepInEx 5, so settings
  land in `BepInEx\config\<plugin id>.cfg` as usual,
- logging, including BepInEx 6's `$"..."` log messages.

At startup the bridge (a normal BepInEx 5 plugin) finds DLLs in `BepInEx\plugins` that were built for BepInEx 6,
loads them as they are and starts their plugin classes the way BepInEx 6 does. It also lists them as loaded plugins,
so other mods (e.g. Eternia Co-op's main-menu "Also running" line) can see them. Plugin files are never modified.

## Limits

- It covers what this game's BepInEx 6 plugins use today. A plugin that needs more of BepInEx 6 fails to start with
  a log line naming the plugin ("Couldn't start BepInEx 6 plugin ...") instead of crashing the game - please report it.
- BepInEx 6 preloader patchers (`BepInEx\patchers`) and IL2CPP plugins are not supported (this game uses Mono).
- Plugins are started in file order; BepInEx 6 dependency ordering is not applied.

## Credits

Part of [Eternia Co-op](https://github.com/thewhitejesus/EterniaCoop) by thewhitejesus1, made with Claude
(Anthropic). MIT licensed. BepInEx is © the BepInEx team (LGPL-2.1); this bridge contains none of BepInEx 6's code.
