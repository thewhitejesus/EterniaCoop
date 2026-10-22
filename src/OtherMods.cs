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
        };

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
                    if (info.Metadata.GUID == Plugin.Guid) continue;
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
