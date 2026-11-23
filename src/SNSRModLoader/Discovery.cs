using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace SNSRModLoader
{
    public sealed class Dependency
    {
        public string Id;
        public Version MinimumVersion;
    }
    public sealed class PluginConflict
    {
        public string Id, Name;
    }
    public sealed class PluginInfo
    {
        public string Id, Name, Version, Path, TypeName, Sha256;
        public GameVersion[] Targets;
        public bool Verified;
        public bool AutomaticUpdate;
        public string UpdateRepository, UpdateManifestUrl;
        public readonly List<Dependency> Dependencies = new List<Dependency>();
        public readonly List<PluginConflict> Conflicts = new List<PluginConflict>();
    }

    public static class Discovery
    {
        private const string Prefix = "SNSRModLoader.";
        public static List<PluginInfo> Scan(string mods, GameVersion game, Action<string> warning)
        { return Scan(mods, game, warning, null); }
        public static List<PluginInfo> Scan(string mods, GameVersion game, Action<string> warning, Action<PluginInfo> updateCandidate)
        {
            var candidates = new List<PluginInfo>();
            var files = Directory.GetFiles(mods, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(p => string.Equals(Path.GetExtension(p), ".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            using (var resolver = new DefaultAssemblyResolver())
            {
            resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(Plugin).Assembly.Location));
            foreach (var path in files)
            {
                try
                {
                    using (var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver }))
                    {
                        var api = assembly.MainModule.AssemblyReferences.FirstOrDefault(r => r.Name == "SNSRModLoader");
                        var installedApi = typeof(Plugin).Assembly.GetName().Version;
                        if (api != null && api.Version > installedApi)
                            throw new InvalidOperationException("This plugin requires SNSRModLoader " + api.Version + "; installed loader is " + installedApi + ". Update the loader before using this plugin.");
                        var types = assembly.MainModule.Types.Where(t => t.CustomAttributes.Any(a => a.AttributeType.FullName == Prefix + "SNSRModLoaderAttribute")).ToArray();
                        if (types.Length != 1) throw new InvalidOperationException("Expected exactly one SNSRModLoader plugin declaration; found " + types.Length + ". This DLL is not a valid plugin for SNSRModLoader.");
                        var type = types[0];
                        if (!type.IsPublic || type.IsAbstract || type.HasGenericParameters) throw new InvalidOperationException("Plugin class must be public, concrete and non-generic.");
                        var attribute = type.CustomAttributes.Single(a => a.AttributeType.FullName == Prefix + "SNSRModLoaderAttribute");
                        var info = new PluginInfo { Path = path, TypeName = type.FullName.Replace('/', '+'), Id = (string)attribute.ConstructorArguments[0].Value,
                            Name = (string)attribute.ConstructorArguments[1].Value, Version = (string)attribute.ConstructorArguments[2].Value, Sha256 = Registry.Hash(path) };
                        if (!PluginContext.IsValidId(info.Id) || string.IsNullOrEmpty(info.Name) || info.Name.Length > 128 || info.Name.Any(char.IsControl)) throw new InvalidOperationException("Invalid plugin ID or display name.");
                        new Version(info.Version);
                        foreach (var property in attribute.Properties)
                        {
                            if (property.Name == "AutomaticUpdate") info.AutomaticUpdate = (bool)property.Argument.Value;
                            if (property.Name == "UpdateRepository") info.UpdateRepository = (string)property.Argument.Value;
                            if (property.Name == "UpdateManifestUrl") info.UpdateManifestUrl = (string)property.Argument.Value;
                        }
                        var targets = new List<GameVersion>();
                        foreach (var a in type.CustomAttributes)
                        {
                            if (a.AttributeType.FullName == Prefix + "TargetVersionAttribute") targets.Add((GameVersion)Convert.ToInt32(a.ConstructorArguments[0].Value));
                            if (a.AttributeType.FullName == Prefix + "TargetVersionsAttribute")
                                foreach (var value in (CustomAttributeArgument[])a.ConstructorArguments[0].Value) targets.Add((GameVersion)Convert.ToInt32(value.Value));
                            if (a.AttributeType.FullName == Prefix + "PluginDependencyAttribute")
                            {
                                var id = (string)a.ConstructorArguments[0].Value;
                                if (!PluginContext.IsValidId(id) || id == info.Id) throw new InvalidOperationException("Invalid dependency ID.");
                                info.Dependencies.Add(new Dependency { Id = id, MinimumVersion = new Version((string)a.ConstructorArguments[1].Value) });
                            }
                            if (a.AttributeType.FullName == Prefix + "ConflictsAttribute")
                            {
                                var id = (string)a.ConstructorArguments[0].Value;
                                if (!PluginContext.IsValidId(id) || string.Equals(id, info.Id, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid conflict plugin ID.");
                                info.Conflicts.Add(new PluginConflict { Id = id, Name = (string)a.ConstructorArguments[1].Value });
                            }
                        }
                        info.Targets = targets.Distinct().ToArray();
                        if (targets.Count == 0 || targets.Any(v => v == GameVersion.Unknown || !Enum.IsDefined(typeof(GameVersion), v))) throw new InvalidOperationException("Declare TargetVersion or TargetVersions using supported game versions.");
                        if (updateCandidate != null) updateCandidate(info);
                        if (!targets.Contains(game)) { warning(Path.GetFileName(path) + " (" + info.Name + ") does not target " + Registry.DisplayName(game) + "; skipped."); continue; }
                        if (game == GameVersion.September2018 && assembly.MainModule.RuntimeVersion.StartsWith("v4")) throw new InvalidOperationException("2018 plugins must target .NET 3.5, not the newer CLR.");
                        candidates.Add(info);
                    }
                }
                catch (Exception ex) { warning(Path.GetFileName(path) + ": " + ex.Message + " Skipped."); }
            }
            var duplicates = new HashSet<string>(candidates.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key), StringComparer.OrdinalIgnoreCase);
            foreach (var info in candidates.Where(p => duplicates.Contains(p.Id))) warning(Path.GetFileName(info.Path) + ": duplicate plugin ID " + info.Id + "; all copies skipped.");
            return Order(candidates.Where(p => !duplicates.Contains(p.Id)).ToList(), warning);
            }
        }

        public static List<PluginInfo> Order(List<PluginInfo> candidates, Action<string> warning)
        {
            var ordered = new List<PluginInfo>();
            var blocked = new HashSet<PluginInfo>();
            var pairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in candidates)
                foreach (var conflict in info.Conflicts)
                    foreach (var other in candidates.Where(p => string.Equals(p.Id, conflict.Id, StringComparison.OrdinalIgnoreCase)))
                    {
                        blocked.Add(info); blocked.Add(other);
                        var key = string.Compare(info.Id, other.Id, StringComparison.OrdinalIgnoreCase) < 0 ? info.Id + ":" + other.Id : other.Id + ":" + info.Id;
                        if (pairs.Add(key)) warning("Plugin conflict: " + info.Name + " [" + info.Id + "] (" + Path.GetFileName(info.Path) + ") conflicts with " + other.Name + " [" + other.Id + "] (" + Path.GetFileName(other.Path) + "). Both plugins skipped.");
                    }
            var remaining = candidates.Where(p => !blocked.Contains(p)).ToList();
            bool progress;
            do
            {
                progress = false;
                foreach (var info in remaining.ToArray())
                {
                    if (!info.Dependencies.All(d => ordered.Any(p => string.Equals(p.Id, d.Id, StringComparison.OrdinalIgnoreCase) && new Version(p.Version) >= d.MinimumVersion))) continue;
                    ordered.Add(info); remaining.Remove(info); progress = true;
                }
            } while (progress);
            foreach (var info in remaining) warning(Path.GetFileName(info.Path) + " (" + info.Name + "): missing, incompatible, or cyclic dependency; skipped.");
            return ordered;
        }
    }
}

