// Game version check. The mod was made for one GameNative.dll build; on any other (a game update, or another
// mod that changed the file) Features decides per feature what is still safe, unless StrictVersionCheck is on.
using System;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;

namespace EterniaCoop
{
    static class Compatibility
    {
        // GameNative.dll of Tales of Eternia Remastered (Steam, October 2026 build).
        public const string KnownSha256 = "6488BE2CE5ECC1DC9FC0216AA2462EC9AEBA7A464468D5FD61AD3C2A8356849B";

        public static bool Ok { get; private set; }          // false: leave the game alone entirely
        public static bool Exact { get; private set; }       // GameNative.dll is the exact supported build
        public static string Problem { get; private set; }   // short text for the watermark, null when all is well

        public static void Check(bool strict)
        {
            string path = Path.Combine(Application.dataPath, "Plugins/x86_64/GameNative.dll");
            string hash = null;
            try
            {
                using (var sha = SHA256.Create())
                using (var file = File.OpenRead(path))
                    hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Can't read GameNative.dll (" + e.Message + ")"); }

            Exact = hash == KnownSha256;
            Ok = Exact || !strict;
            if (Exact) Plugin.Log.LogInfo("GameNative.dll matches the supported game version.");
            else if (Ok) Plugin.Log.LogWarning("GameNative.dll is not the build this mod was made for (" + Short(hash) +
                "): it was updated or another mod changed it. Each feature checks its game code before it is used.");
            else
            {
                Problem = "off: game version not supported";
                Plugin.Log.LogError("Co-op disabled: GameNative.dll " + Short(hash) + " is not the supported build and " +
                    "StrictVersionCheck is on. The game runs unmodified.");
            }
        }

        // From Features.Check, once the per-feature code checks are done.
        public static void Report(bool core, System.Collections.Generic.List<string> missing)
        {
            if (!core) { Ok = false; Problem = "off: game code not recognized"; return; }
            if (missing.Count > 0) Problem = "partly on (off: " + string.Join(", ", missing.ToArray()) + ")";
            else if (!Exact) Problem = "modified game, all features on";
        }

        static string Short(string hash) { return hash == null ? "unreadable" : hash.Substring(0, 12) + "..."; }
    }
}
