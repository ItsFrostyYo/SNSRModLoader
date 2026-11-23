using System;
using System.IO;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace SNSRModLoader
{
    public enum GameVersion { Unknown = 0, September2018 = 1, December2021 = 2, March2023 = 3, October2025 = 4 }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SNSRModLoaderAttribute : Attribute
    {
        public string PluginId { get; private set; }
        public string PluginName { get; private set; }
        public string PluginVersion { get; private set; }
        public bool AutomaticUpdate { get; set; }
        public string UpdateRepository { get; set; }
        public string UpdateManifestUrl { get; set; }
        public SNSRModLoaderAttribute(string pluginId, string pluginName, string pluginVersion)
        { PluginId = pluginId; PluginName = pluginName; PluginVersion = pluginVersion; }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class TargetVersionAttribute : Attribute
    {
        public GameVersion Version { get; private set; }
        public TargetVersionAttribute(GameVersion version) { Version = version; }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class TargetVersionsAttribute : Attribute
    {
        public GameVersion[] Versions { get; private set; }
        public TargetVersionsAttribute(params GameVersion[] versions) { Versions = versions; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class PluginDependencyAttribute : Attribute
    {
        public string PluginId { get; private set; }
        public string MinimumVersion { get; private set; }
        public PluginDependencyAttribute(string pluginId, string minimumVersion = "0.0.0")
        { PluginId = pluginId; MinimumVersion = minimumVersion; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class ConflictsAttribute : Attribute
    {
        public string PluginId { get; private set; }
        public string PluginName { get; private set; }
        public ConflictsAttribute(string pluginId, string pluginName = null)
        { PluginId = pluginId; PluginName = pluginName; }
    }

    // Unity-independent contract: one assembly can target several game releases.
    // Plugins may create MonoBehaviours from Load for Unity callbacks/coroutines.
    public abstract class Plugin
    {
        public abstract void Load(PluginContext context);
        public virtual void Unload() { }
    }

    public sealed class PluginContext
    {
        public string PluginId { get; private set; }
        public string PluginDirectory { get; private set; }
        public string SettingsDirectory { get; private set; }
        public GameVersion GameVersion { get; private set; }
        public PluginLogger Logger { get; private set; }
        public SettingsFile Settings { get; private set; }
        public Harmony Harmony { get; private set; }
        public PluginContext(string id, string modsDirectory, GameVersion version)
        {
            if (!IsValidId(id)) throw new ArgumentException("Plugin IDs must use letters, digits, dots, underscores or hyphens.");
            PluginId = id; PluginDirectory = modsDirectory; GameVersion = version;
            SettingsDirectory = Path.Combine(Path.Combine(modsDirectory, "Settings"), id);
            Directory.CreateDirectory(SettingsDirectory);
            Logger = new PluginLogger(id);
            Settings = new SettingsFile(Path.Combine(SettingsDirectory, "settings.ini"));
            Harmony = new Harmony("SNSRModLoader.plugin." + id);
        }
        public static bool IsValidId(string id) { return id != null && Regex.IsMatch(id, @"^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$"); }
        public string GetSettingsPath(string relativePath)
        {
            var root = Path.GetFullPath(SettingsDirectory) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Path must stay in the plugin settings directory.");
            return path;
        }
    }
}
