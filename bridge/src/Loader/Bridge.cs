// BepInEx 6 Bridge: runs BepInEx 6 (Unity Mono) plugins on BepInEx 5, so Tales of Eternia Remastered mods built for
// either version work in one install.
//
// The plugin DLLs are loaded unmodified. BepInEx 6's own assemblies (BepInEx.Core, BepInEx.Unity.Mono) are replaced by
// small stand-ins (shim\ next to this DLL) that provide what those plugins use, on top of BepInEx 5. Plugins that need
// anything the stand-ins lack fail to start with a clear log line instead of taking the game down.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Mono.Cecil;
using UnityEngine;

namespace BepInEx6Bridge
{
    [BepInPlugin(Guid, Name, Version)]
    public class Bridge : BaseUnityPlugin
    {
        public const string Guid = "eterniacoop.bepinex6bridge", Name = "BepInEx 6 Bridge", Version = "1.0.0";

        static Assembly shimCore, shimMono;
        static readonly Dictionary<string, Assembly> loadedPlugins = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        GameObject host;

        void Awake()
        {
            string shimDir = Path.Combine(Path.GetDirectoryName(Info.Location), "shim");
            try
            {
                shimCore = Assembly.LoadFrom(Path.Combine(shimDir, "BepInEx.Core.dll"));
                shimMono = Assembly.LoadFrom(Path.Combine(shimDir, "BepInEx.Unity.Mono.dll"));
            }
            catch (Exception e)
            {
                Logger.LogError("Bridge files missing from " + shimDir + " - reinstall the BepInEx 6 Bridge. (" + e.Message + ")");
                return;
            }
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;

            var found = FindPlugins();
            if (found.Count == 0) { Logger.LogInfo("No BepInEx 6 plugins found."); return; }

            host = new GameObject("BepInEx6Bridge");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            foreach (string path in found) Launch(path);
        }

        // DLLs under BepInEx\plugins built against BepInEx 6 (they reference BepInEx.Unity.Mono).
        List<string> FindPlugins()
        {
            var result = new List<string>();
            string own = Path.GetDirectoryName(Info.Location);
            foreach (string path in Directory.GetFiles(Paths.PluginPath, "*.dll", SearchOption.AllDirectories))
            {
                if (path.StartsWith(own, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using (var asm = AssemblyDefinition.ReadAssembly(path))
                        if (asm.MainModule.AssemblyReferences.Any(r => r.Name == "BepInEx.Unity.Mono")) result.Add(path);
                }
                catch { }   // not a .NET assembly
            }
            return result;
        }

        void Launch(string path)   // (not "Start": Unity would call that itself)
        {
            string file = Path.GetFileName(path);
            try
            {
                var asm = Assembly.LoadFrom(path);
                loadedPlugins[asm.GetName().Name] = asm;
                Type baseType = shimMono.GetType("BepInEx.Unity.Mono.BaseUnityPlugin");
                Type attrType = shimCore.GetType("BepInEx.BepInPlugin");
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

                bool any = false;
                foreach (var t in types)
                {
                    if (t.IsAbstract || !baseType.IsAssignableFrom(t)) continue;
                    var attrs = t.GetCustomAttributes(attrType, false);
                    if (attrs.Length == 0) continue;
                    string guid = (string)attrType.GetProperty("GUID").GetValue(attrs[0], null);
                    string name = (string)attrType.GetProperty("Name").GetValue(attrs[0], null);
                    string version = (string)attrType.GetProperty("Version").GetValue(attrs[0], null);
                    if (Chainloader.PluginInfos.ContainsKey(guid)) { Logger.LogWarning(name + " (" + file + ") is already loaded, skipped."); continue; }

                    var instance = (MonoBehaviour)host.AddComponent(t);
                    Register(guid, name, version, path, instance);
                    Logger.LogInfo("Started BepInEx 6 plugin " + name + " " + version + " (" + file + ")");
                    any = true;
                }
                if (!any) Logger.LogWarning(file + " references BepInEx 6 but has no plugin class the bridge can start.");
            }
            catch (Exception e)
            {
                Logger.LogError("Couldn't start BepInEx 6 plugin " + file + ": " + e.GetType().Name + ": " + e.Message +
                    " - it may use a BepInEx 6 feature the bridge doesn't provide yet.");
            }
        }

        // Make bridged plugins visible like any other plugin (Chainloader.PluginInfos), e.g. for mod lists.
        void Register(string guid, string name, string version, string path, MonoBehaviour instance)
        {
            try
            {
                var info = (PluginInfo)Activator.CreateInstance(typeof(PluginInfo), true);
                Set(info, "Metadata", new BepInPlugin(guid, name, CleanVersion(version)));
                Set(info, "Location", path);
                Set(info, "Instance", instance);
                Chainloader.PluginInfos[guid] = info;
            }
            catch (Exception e) { Logger.LogDebug("Couldn't register " + name + ": " + e.Message); }
        }

        static void Set(object o, string property, object value)
        {
            var p = o.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null) p.SetValue(o, value, null);
        }

        static string CleanVersion(string v)
        {
            if (string.IsNullOrEmpty(v)) return "0.0.0";
            int cut = v.IndexOfAny(new[] { '-', '+' });
            return cut > 0 ? v.Substring(0, cut) : v;
        }

        // BepInEx 6 plugins ask for BepInEx.Core / BepInEx.Unity.Mono 6.0 and newer library versions (0Harmony 2.10):
        // answer with the stand-ins and whatever version is already loaded. Other lookups are left alone unless a
        // bridged plugin made them, so other mods' assembly resolution behaves exactly as without the bridge.
        static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (name == "BepInEx.Core") return shimCore;
            if (name == "BepInEx.Unity.Mono") return shimMono;
            Assembly a;
            if (loadedPlugins.TryGetValue(name, out a)) return a;
            bool fromBridged = args.RequestingAssembly != null && loadedPlugins.ContainsValue(args.RequestingAssembly);
            if (name != "0Harmony" && !fromBridged) return null;
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
                if (loaded.GetName().Name == name) return loaded;
            return null;
        }
    }
}
