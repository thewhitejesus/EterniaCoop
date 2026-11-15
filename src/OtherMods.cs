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
        static int scans;

        // Scanned a few times while the main menu first shows it (mods can finish loading after us - Guests from
        // Phantasia's DLL a few seconds later), then kept: walking every loaded module and reading version information
        // from disk takes long enough to show as a hitch.
        public static List<string> Names(float now)
        {
            if (names != null && (now < nextScan || scans >= 4)) return names;
            scans++;
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

        // 100% Drop & Steal Rate 1.0.0 patches fixed GameNative addresses from game version 2.0.0. On any other build
        // those bytes are other code: in 2.1.0 its patch at 0x72382 cuts an instruction out of the Auto characters' AI
        // handler, and the first battle crashes. Null unless that version runs on a different build.
        const string DropStealFile = "TalesOfEternia_DropStealMod", DropStealVersion = "1.0.0";
        const string DropStealBuild = "6488BE2CE5ECC1DC9FC0216AA2462EC9AEBA7A464468D5FD61AD3C2A8356849B";   // game 2.0.0
        static bool dropStealChecked;
        static string dropStealWarning;

        // Version 1.0.0 was made for game 2.0.0 and patches its fixed addresses; on 2.1.0 one of them (0x72382) is inside
        // the Auto handler, and battles crash. A later build kept the version number but finds its spots by code pattern.
        // So the warning goes by what's in memory: does game code differ from the file at the old 2.0.0 spots?
        static readonly int[] DropStealOldSpots = { 0x72382, 0x9122F, 0x91323, 0xD0E3E };

        public static string DropStealWarning()
        {
            if (dropStealChecked) return dropStealWarning;
            dropStealChecked = true;
            try
            {
                if (Compatibility.Hash == null || Compatibility.Hash == DropStealBuild || Native.Base == 0) return null;
                bool loaded = false;
                foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
                {
                    string file = info.Location == null ? "" : Path.GetFileNameWithoutExtension(info.Location);
                    if (file.Equals(DropStealFile, StringComparison.OrdinalIgnoreCase) && info.Metadata.Version.ToString() == DropStealVersion) loaded = true;
                }
                if (!loaded) return null;
                int changed = -1;
                foreach (int rva in DropStealOldSpots) if (DiffersFromFile(rva, 8)) { changed = rva; break; }
                if (changed < 0)
                {
                    Plugin.Log.LogInfo("100% Drop & Steal: it left game 2.0.0's addresses alone (a build that supports this game version).");
                    return null;
                }
                dropStealWarning = "100% Drop & Steal 1.0.0 is made for game version 2.0.0: on this version it crashes battles. " +
                    "Remove it until it's updated.";
                Plugin.Log.LogWarning(dropStealWarning + " (It changed game 2.0.0's code at GameNative+0x" + changed.ToString("X") + ".)");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Couldn't check 100% Drop & Steal: " + e.Message); }
            return dropStealWarning;
        }

        static byte[] gameFile;

        // Whether GameNative's code in memory at rva differs from GameNative.dll on disk.
        static bool DiffersFromFile(int rva, int length)
        {
            if (gameFile == null) gameFile = File.ReadAllBytes(Path.Combine(UnityEngine.Application.dataPath, "Plugins/x86_64/GameNative.dll"));
            long b = Native.Base;
            int pe = System.Runtime.InteropServices.Marshal.ReadInt32(new IntPtr(b + 0x3C));
            int sections = System.Runtime.InteropServices.Marshal.ReadInt16(new IntPtr(b + pe + 6));
            int optSize = System.Runtime.InteropServices.Marshal.ReadInt16(new IntPtr(b + pe + 20));
            long table = b + pe + 24 + optSize;
            for (int s = 0; s < sections; s++)
            {
                long h = table + s * 40;
                int va = System.Runtime.InteropServices.Marshal.ReadInt32(new IntPtr(h + 12));
                int rawSize = System.Runtime.InteropServices.Marshal.ReadInt32(new IntPtr(h + 16));
                int raw = System.Runtime.InteropServices.Marshal.ReadInt32(new IntPtr(h + 20));
                if (rva < va || rva + length > va + rawSize) continue;
                int at = rva - va + raw;
                if (at + length > gameFile.Length) return false;
                var now = new byte[length];
                System.Runtime.InteropServices.Marshal.Copy(Native.At(rva), now, 0, length);
                for (int i = 0; i < length; i++) if (now[i] != gameFile[at + i]) return true;
                return false;
            }
            return false;
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
