// Stand-in for BepInEx 6's BepInEx.Unity.Mono.dll: the plugin base class. Like BepInEx 6, a plugin gets its config
// file (BepInEx\config\<GUID>.cfg) and its log source in the constructor, from its [BepInPlugin] attribute.
extern alias BE5;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;

[assembly: AssemblyVersion("6.0.0.0")]
[assembly: AssemblyTitle("BepInEx 6 Bridge - BepInEx.Unity.Mono stand-in")]

namespace BepInEx.Unity.Mono
{
    public abstract class BaseUnityPlugin : MonoBehaviour
    {
        public BepInPlugin Metadata { get; private set; }
        public BepInEx.Logging.ManualLogSource Logger { get; private set; }
        public BE5::BepInEx.Configuration.ConfigFile Config { get; private set; }

        protected BaseUnityPlugin()
        {
            var attrs = GetType().GetCustomAttributes(typeof(BepInPlugin), false);
            Metadata = attrs.Length > 0 ? (BepInPlugin)attrs[0] : new BepInPlugin(GetType().FullName, GetType().Name, "0.0.0");
            Logger = new BepInEx.Logging.ManualLogSource(Metadata.Name);
            Config = new BE5::BepInEx.Configuration.ConfigFile(
                Path.Combine(BE5::BepInEx.Paths.ConfigPath, Metadata.GUID + ".cfg"), true,
                new BE5::BepInEx.BepInPlugin(Metadata.GUID, Metadata.Name, CleanVersion(Metadata.Version)));
        }

        // BepInEx 5 wants System.Version ("1.2.3"); BepInEx 6 versions can be semantic ("1.2.3-beta").
        static string CleanVersion(string v)
        {
            if (string.IsNullOrEmpty(v)) return "0.0.0";
            int cut = v.IndexOfAny(new[] { '-', '+' });
            return cut > 0 ? v.Substring(0, cut) : v;
        }
    }
}
