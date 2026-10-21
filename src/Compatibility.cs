// Startup check: the mod only patches the GameNative.dll build it was made for.
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

        public static bool Ok { get; private set; }
        public static string Problem { get; private set; }   // short text for the watermark, null when Ok

        public static void Check(bool allowUnknown)
        {
            string path = Path.Combine(Application.dataPath, "Plugins/x86_64/GameNative.dll");
            string hash;
            try
            {
                using (var sha = SHA256.Create())
                using (var file = File.OpenRead(path))
                    hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
            }
            catch (Exception e)
            {
                Fail("can't read GameNative.dll (" + e.Message + ")", allowUnknown);
                return;
            }

            if (hash == KnownSha256)
            {
                Ok = true;
                Plugin.Log.LogInfo("GameNative.dll matches the supported game version.");
                return;
            }
            Fail("game version not supported (GameNative.dll " + hash.Substring(0, 12) + "...)", allowUnknown);
        }

        static void Fail(string why, bool allowUnknown)
        {
            if (allowUnknown)
            {
                Ok = true;
                Problem = "untested game version";
                Plugin.Log.LogWarning("Co-op: " + why + ". AllowUnknownGameVersion is on, patching anyway (each patch checks its bytes).");
                return;
            }
            Ok = false;
            Problem = "off: game version not supported";
            Plugin.Log.LogError("Co-op disabled: " + why + ". The game was probably updated; the game runs unmodified until the mod is updated.");
        }
    }
}
