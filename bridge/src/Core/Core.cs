// Stand-in for BepInEx 6's BepInEx.Core.dll, running on BepInEx 5. Only what BepInEx 6 plugins for this game use:
// plugin attributes, configuration (forwarded to BepInEx 5's identical types) and logging (wrapping BepInEx 5's,
// plus BepInEx 6's interpolated-string log handlers). Built as "BepInEx.Core" version 6.0.0.0 so the plugins bind to it.
extern alias BE5;
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

[assembly: AssemblyVersion("6.0.0.0")]
[assembly: AssemblyTitle("BepInEx 6 Bridge - BepInEx.Core stand-in")]

// Configuration is the same API in BepInEx 5 and 6: hand those types straight to BepInEx 5.
[assembly: TypeForwardedTo(typeof(BE5::BepInEx.Configuration.ConfigFile))]
[assembly: TypeForwardedTo(typeof(BE5::BepInEx.Configuration.ConfigEntry<>))]
[assembly: TypeForwardedTo(typeof(BE5::BepInEx.Configuration.ConfigEntryBase))]
[assembly: TypeForwardedTo(typeof(BE5::BepInEx.Configuration.ConfigDefinition))]
[assembly: TypeForwardedTo(typeof(BE5::BepInEx.Configuration.ConfigDescription))]
[assembly: TypeForwardedTo(typeof(BE5::BepInEx.Configuration.AcceptableValueBase))]
[assembly: TypeForwardedTo(typeof(BE5::BepInEx.Configuration.AcceptableValueRange<>))]
[assembly: TypeForwardedTo(typeof(BE5::BepInEx.Configuration.AcceptableValueList<>))]
[assembly: TypeForwardedTo(typeof(BE5::BepInEx.Configuration.SettingChangedEventArgs))]
[assembly: TypeForwardedTo(typeof(BE5::BepInEx.Configuration.TomlTypeConverter))]
[assembly: TypeForwardedTo(typeof(BE5::BepInEx.Configuration.TypeConverter))]
[assembly: TypeForwardedTo(typeof(BE5::BepInEx.Logging.LogLevel))]
[assembly: TypeForwardedTo(typeof(BE5::BepInEx.Paths))]

namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class BepInPlugin : Attribute
    {
        public string GUID { get; protected set; }
        public string Name { get; protected set; }
        public string Version { get; protected set; }   // BepInEx 6 uses SemanticVersioning.Version; plugins here only pass a string

        public BepInPlugin(string GUID, string Name, string Version)
        {
            this.GUID = GUID;
            this.Name = Name;
            this.Version = Version;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class BepInDependency : Attribute
    {
        [Flags]
        public enum DependencyFlags { HardDependency = 1, SoftDependency = 2 }

        public string DependencyGUID { get; protected set; }
        public DependencyFlags Flags { get; protected set; }

        public BepInDependency(string DependencyGUID, DependencyFlags Flags = DependencyFlags.HardDependency)
        {
            this.DependencyGUID = DependencyGUID;
            this.Flags = Flags;
        }

        public BepInDependency(string guid, string version) : this(guid) { }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class BepInIncompatibility : Attribute
    {
        public string IncompatibilityGUID { get; protected set; }
        public BepInIncompatibility(string IncompatibilityGUID) { this.IncompatibilityGUID = IncompatibilityGUID; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class BepInProcess : Attribute
    {
        public string ProcessName { get; protected set; }
        public BepInProcess(string ProcessName) { this.ProcessName = ProcessName; }
    }
}

namespace BepInEx.Logging
{
    using BepInEx.Core.Logging.Interpolation;
    using LogLevel = BE5::BepInEx.Logging.LogLevel;

    // BepInEx 6's ManualLogSource: the same calls as BepInEx 5's, plus overloads taking interpolated-string handlers.
    public class ManualLogSource : IDisposable
    {
        readonly BE5::BepInEx.Logging.ManualLogSource inner;

        public ManualLogSource(string sourceName) { inner = BE5::BepInEx.Logging.Logger.CreateLogSource(sourceName); }

        public string SourceName { get { return inner.SourceName; } }

        public void Log(LogLevel level, object data) { inner.Log(level, data); }
        public void LogFatal(object data) { inner.LogFatal(data); }
        public void LogError(object data) { inner.LogError(data); }
        public void LogWarning(object data) { inner.LogWarning(data); }
        public void LogMessage(object data) { inner.LogMessage(data); }
        public void LogInfo(object data) { inner.LogInfo(data); }
        public void LogDebug(object data) { inner.LogDebug(data); }

        public void Log(LogLevel level, BepInExLogInterpolatedStringHandler logHandler) { inner.Log(level, logHandler.ToString()); }
        public void LogFatal(BepInExFatalLogInterpolatedStringHandler logHandler) { inner.LogFatal(logHandler.ToString()); }
        public void LogError(BepInExErrorLogInterpolatedStringHandler logHandler) { inner.LogError(logHandler.ToString()); }
        public void LogWarning(BepInExWarningLogInterpolatedStringHandler logHandler) { inner.LogWarning(logHandler.ToString()); }
        public void LogMessage(BepInExMessageLogInterpolatedStringHandler logHandler) { inner.LogMessage(logHandler.ToString()); }
        public void LogInfo(BepInExInfoLogInterpolatedStringHandler logHandler) { inner.LogInfo(logHandler.ToString()); }
        public void LogDebug(BepInExDebugLogInterpolatedStringHandler logHandler) { inner.LogDebug(logHandler.ToString()); }

        public void Dispose() { BE5::BepInEx.Logging.Logger.Sources.Remove(inner); }
    }
}

namespace BepInEx.Core.Logging.Interpolation
{
    using LogLevel = BE5::BepInEx.Logging.LogLevel;

    // What C# compiles $"..." into when it's passed to a BepInEx 6 log call: a builder the compiler fills piece by
    // piece. A class in BepInEx 6 too (plugins create it with newobj and call it with callvirt).
    public class BepInExLogInterpolatedStringHandler
    {
        readonly StringBuilder sb;

        public BepInExLogInterpolatedStringHandler(int literalLength, int formattedCount, LogLevel logLevel, out bool isEnabled)
        {
            isEnabled = true;
            sb = new StringBuilder(literalLength + formattedCount * 8);
        }

        public bool Enabled { get { return true; } }

        public void AppendLiteral(string s) { sb.Append(s); }
        public void AppendFormatted<T>(T t) { sb.Append(Format(t, null)); }
        public void AppendFormatted<T>(T t, string format) { sb.Append(Format(t, format)); }
        public void AppendFormatted<T>(T t, int alignment) { Pad(Format(t, null), alignment); }
        public void AppendFormatted<T>(T t, int alignment, string format) { Pad(Format(t, format), alignment); }

        static string Format<T>(T t, string format)
        {
            if (t == null) return "";
            var f = t as IFormattable;
            return f != null ? f.ToString(format, null) : t.ToString();
        }

        void Pad(string s, int alignment)
        {
            sb.Append(alignment < 0 ? s.PadRight(-alignment) : s.PadLeft(alignment));
        }

        public override string ToString() { return sb.ToString(); }
    }

    public class BepInExFatalLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
    {
        public BepInExFatalLogInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled)
            : base(literalLength, formattedCount, LogLevel.Fatal, out isEnabled) { }
    }

    public class BepInExErrorLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
    {
        public BepInExErrorLogInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled)
            : base(literalLength, formattedCount, LogLevel.Error, out isEnabled) { }
    }

    public class BepInExWarningLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
    {
        public BepInExWarningLogInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled)
            : base(literalLength, formattedCount, LogLevel.Warning, out isEnabled) { }
    }

    public class BepInExMessageLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
    {
        public BepInExMessageLogInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled)
            : base(literalLength, formattedCount, LogLevel.Message, out isEnabled) { }
    }

    public class BepInExInfoLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
    {
        public BepInExInfoLogInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled)
            : base(literalLength, formattedCount, LogLevel.Info, out isEnabled) { }
    }

    public class BepInExDebugLogInterpolatedStringHandler : BepInExLogInterpolatedStringHandler
    {
        public BepInExDebugLogInterpolatedStringHandler(int literalLength, int formattedCount, out bool isEnabled)
            : base(literalLength, formattedCount, LogLevel.Debug, out isEnabled) { }
    }
}
