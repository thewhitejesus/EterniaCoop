// Other mods running alongside this one, for the main-menu label: BepInEx plugins, plus native mods loaded by
// version.dll / ASI loaders (Guests from Phantasia, EZ Craymel Ball) and ReShade, found among the game's modules.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace EterniaCoop
{
    static class OtherMods
    {
        // Short names for mods whose own names are long or unhelpful (plugin file / module name -> label).
        static readonly Dictionary<string, string> ShortNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "TalesOfEternia_DropStealMod", "100% Drop & Steal" },
            { "ToE_Guest", "Guests from Phantasia" },
            { "CraymelBall", "EZ Craymel Ball" },
            { "UncropPlugin", "Uncropped Backgrounds" },
            { "FontOfEterniaPlugin", "Font Replacer" },
            { "FastShutdownPlugin", "Fast Shutdown" },
            { "AnalogFixPlugin", "Analog Stick Fix" },
            { "ArteShortcutsPlugin", "Arte Shortcuts" },
            { "ArtesCharacterRenamer", "Artes Renamer" },
            { "EterniaSixPersonBattle", "Six Person Battle" },
        };

        // Shipped with this mod (BepInEx 6 Bridge): not worth listing as "another mod".
        const string BridgeGuid = "eterniacoop.bepinex6bridge";

        static List<string> names;
        static float nextScan;

        // Rescanned every few seconds while the main menu shows it: mods can finish loading after us.
        public static List<string> Names(float now)
        {
            if (names != null && now < nextScan) return names;
            nextScan = now + 5f;
            var found = new List<string>();
            try
            {
                foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
                {
                    if (info.Metadata.GUID == Plugin.Guid || info.Metadata.GUID == BridgeGuid) continue;
                    string file = info.Location == null ? "" : Path.GetFileNameWithoutExtension(info.Location);
                    string name;
                    Add(found, ShortNames.TryGetValue(file, out name) ? name : info.Metadata.Name);
                }

                string game = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
                using (var self = Process.GetCurrentProcess())
                foreach (ProcessModule m in self.Modules)
                {
                    string path = m.FileName ?? "";
                    if (!path.StartsWith(game, StringComparison.OrdinalIgnoreCase)) continue;
                    string file = Path.GetFileNameWithoutExtension(path), name;
                    string dir = Path.GetFileName(Path.GetDirectoryName(path)) ?? "";
                    if (ShortNames.TryGetValue(file, out name)) Add(found, name);
                    else if (path.EndsWith(".asi", StringComparison.OrdinalIgnoreCase)) Add(found, file);
                    else if (dir.Equals("scripts", StringComparison.OrdinalIgnoreCase) || ParentName(Path.GetDirectoryName(path)) == "mods") Add(found, file);
                    else if ((m.FileVersionInfo.ProductName ?? "").IndexOf("ReShade", StringComparison.OrdinalIgnoreCase) >= 0) Add(found, "ReShade");
                }
            }
            catch (Exception e)
            {
                if (names == null) Plugin.Log.LogWarning("Couldn't list other mods: " + e.Message);
            }
            names = found;
            return names;
        }

        // BepInEx 6 plugins in BepInEx\plugins that nothing will load: BepInEx 5 skips them, and the BepInEx 6 Bridge
        // (shipped with this mod, also an optional file on Nexus) isn't installed. Such a DLL references BepInEx 6's
        // "BepInEx.Unity.Mono", which BepInEx 5 plugins never do; the file is only read, not loaded. Checked once.
        static List<string> unbridged;

        public static List<string> UnbridgedBepInEx6()
        {
            if (unbridged != null) return unbridged;
            unbridged = new List<string>();
            try
            {
                if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(BridgeGuid)) return unbridged;
                byte[] marker = System.Text.Encoding.ASCII.GetBytes("BepInEx.Unity.Mono");
                foreach (string dll in Directory.GetFiles(BepInEx.Paths.PluginPath, "*.dll", SearchOption.AllDirectories))
                {
                    string file = Path.GetFileNameWithoutExtension(dll);
                    if (file.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)) continue;   // the bridge and its stand-ins
                    if (!Contains(File.ReadAllBytes(dll), marker)) continue;
                    string name;
                    Add(unbridged, ShortNames.TryGetValue(file, out name) ? name : file);
                }
                if (unbridged.Count > 0)
                    Plugin.Log.LogWarning("BepInEx 6 mods found but not loaded (" + string.Join(", ", unbridged.ToArray()) +
                        "): they need the BepInEx 6 Bridge, which comes with Eternia Co-op (BepInEx\\plugins\\BepInEx6Bridge) " +
                        "and is an optional file on its Nexus page.");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't check for BepInEx 6 mods: " + e.Message); }
            return unbridged;
        }

        static bool Contains(byte[] data, byte[] marker)
        {
            for (int i = 0; i + marker.Length <= data.Length; i++)
            {
                int k = 0;
                while (k < marker.Length && data[i + k] == marker[k]) k++;
                if (k == marker.Length) return true;
            }
            return false;
        }

        // "mods" for game\mods\ToE_Guest (lower case), "" if there is no parent.
        static string ParentName(string dir)
        {
            string parent = dir == null ? null : Path.GetDirectoryName(dir);
            return parent == null ? "" : Path.GetFileName(parent).ToLowerInvariant();
        }

        static void Add(List<string> list, string name)
        {
            if (!string.IsNullOrEmpty(name) && !list.Contains(name)) list.Add(name);
        }
    }
}
