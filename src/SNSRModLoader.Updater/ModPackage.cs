using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Mono.Cecil;

namespace SNSRModLoader.Updater
{
    internal static class UpdateXml
    {
        internal static XmlDocument Read(byte[] data)
        {
            if (data.Length > 1024 * 1024) throw new InvalidDataException("Update XML exceeds 1 MiB.");
            var xml = new XmlDocument { XmlResolver = null };
            using (var stream = new MemoryStream(data))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) xml.Load(reader);
            return xml;
        }
        internal static bool Hash(string value) { return Regex.IsMatch(value ?? "", "^[a-fA-F0-9]{64}$"); }
        internal static bool Id(string value) { return Regex.IsMatch(value ?? "", @"^[A-Za-z0-9][A-Za-z0-9_.-]{0,127}$"); }
        internal static readonly string[] Games = { "September2018", "December2021", "March2023", "October2025" };
    }
    internal sealed class ModRequest
    {
        internal string Id, Name, FileName, Hash, Repository, Manifest;
        internal Version Version;
        internal bool Enabled;
        internal bool Selected;
    }
    internal sealed class BatchRequest
    {
        internal string Game, LoaderManifest;
        internal bool LoaderSelected;
        internal readonly List<ModRequest> Mods = new List<ModRequest>();
        internal static BatchRequest Read(string file, string gameRoot)
        {
            if (new FileInfo(file).Length > 1024 * 1024) throw new InvalidDataException("Update request too large.");
            var xml = UpdateXml.Read(File.ReadAllBytes(file));
            var root = xml.DocumentElement;
            if (root == null || root.Name != "updateRequest" || root.GetAttribute("schemaVersion") != "1" || !UpdateXml.Games.Contains(root.GetAttribute("gameVersion"))) throw new InvalidDataException("Invalid batch update request.");
            var result = new BatchRequest { Game = root.GetAttribute("gameVersion"), LoaderManifest = root.GetAttribute("loaderManifestUrl"), LoaderSelected = root.GetAttribute("loaderSelected") == "true" };
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlElement mod in root.SelectNodes("mod"))
            {
                var item = new ModRequest { Id = mod.GetAttribute("id"), Name = mod.GetAttribute("name"), FileName = mod.GetAttribute("file"),
                    Version = new Version(mod.GetAttribute("version")), Hash = mod.GetAttribute("sha256"), Enabled = mod.GetAttribute("automaticUpdate") == "true",
                    Repository = mod.GetAttribute("repository"), Manifest = mod.GetAttribute("manifestUrl"), Selected = mod.GetAttribute("updateSelected") == "true" };
                if (!UpdateXml.Id(item.Id) || !UpdateXml.Hash(item.Hash) || !ids.Add(item.Id) || !paths.Add(item.FileName)) throw new InvalidDataException("Invalid/duplicate batch mod identity.");
                UpdatePackage.ModPath(gameRoot, item.FileName);
                result.Mods.Add(item);
            }
            return result;
        }
    }
    internal sealed class ModPackage
    {
        internal string Id, Hash;
        internal Version Version, MinimumLoader;
        internal Uri Download;
        internal ModRequest Installed;
        internal readonly Dictionary<string, Version> Dependencies = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);
        internal readonly HashSet<string> Conflicts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal static ModPackage Parse(byte[] data, ModRequest installed, string game)
        {
            SNSRModLoader.UpdateSource.Validate(installed.Repository, installed.Manifest);
            var root = UpdateXml.Read(data).DocumentElement;
            if (root == null || root.Name != "modUpdate" || root.GetAttribute("schemaVersion") != "1" || root.GetAttribute("id") != installed.Id || !UpdateXml.Hash(root.GetAttribute("sha256"))) throw new InvalidDataException("Invalid mod update identity/schema/hash.");
            var result = new ModPackage { Id = installed.Id, Version = new Version(root.GetAttribute("version")), Download = new Uri(root.GetAttribute("url")), Hash = root.GetAttribute("sha256"), Installed = installed,
                MinimumLoader = new Version(root.HasAttribute("minimumLoaderVersion") ? root.GetAttribute("minimumLoaderVersion") : "1.0.2") };
            var targets = root.SelectNodes("target").Cast<XmlElement>().Select(t => t.GetAttribute("version")).ToArray();
            if (targets.Length == 0 || targets.Any(t => !UpdateXml.Games.Contains(t)) || !targets.Contains(game)) throw new InvalidDataException("Mod update does not support this game version.");
            foreach (XmlNode child in root.ChildNodes) if (child is XmlElement && child.Name != "target") throw new InvalidDataException("Unexpected mod manifest element.");
            if (result.Download.Scheme != "https" || result.Download.Host != "github.com" || !result.Download.IsDefaultPort || result.Download.UserInfo != "" || result.Download.Query != "" || result.Download.Fragment != "" ||
                !result.Download.AbsolutePath.StartsWith("/" + installed.Repository + "/releases/download/", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Mod DLL must be a release asset in the declared repository.");
            return result;
        }
        internal void ValidateDll(string file, string game, string loader)
        {
            if (!string.Equals(UpdatePackage.Hash(file), Hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Mod DLL SHA-256 mismatch: " + Id);
            using (var resolver = new DefaultAssemblyResolver())
            {
                resolver.AddSearchDirectory(loader);
                resolver.AddSearchDirectory(Path.Combine(Path.GetDirectoryName(loader), "Settings", Id));
                using (var assembly = AssemblyDefinition.ReadAssembly(file, new ReaderParameters { AssemblyResolver = resolver }))
                {
                    if (assembly.MainModule.Kind != ModuleKind.Dll || (game == "September2018" && assembly.MainModule.RuntimeVersion.StartsWith("v4"))) throw new InvalidDataException("Updated DLL uses an unsupported runtime.");
                    var plugins = assembly.MainModule.Types.Where(t => t.CustomAttributes.Any(a => a.AttributeType.FullName == "SNSRModLoader.SNSRModLoaderAttribute")).ToArray();
                    if (plugins.Length != 1 || !plugins[0].IsPublic || plugins[0].IsAbstract || plugins[0].HasGenericParameters) throw new InvalidDataException("Updated DLL has no unique public plugin.");
                    bool derivesPlugin = false;
                    var parent = plugins[0].BaseType;
                    for (int depth = 0; parent != null && depth < 64; depth++) {
                        if (parent.FullName == "SNSRModLoader.Plugin") { derivesPlugin = true; break; }
                        parent = parent.Resolve().BaseType;
                    }
                    if (!derivesPlugin) throw new InvalidDataException("Updated plugin must derive from SNSRModLoader.Plugin.");
                    var registration = plugins[0].CustomAttributes.Single(a => a.AttributeType.FullName == "SNSRModLoader.SNSRModLoaderAttribute");
                    if ((string)registration.ConstructorArguments[0].Value != Id || new Version((string)registration.ConstructorArguments[2].Value) != Version) throw new InvalidDataException("Updated DLL identity/version differs from the manifest.");
                    var name = (string)registration.ConstructorArguments[1].Value;
                    if (string.IsNullOrEmpty(name) || name.Length > 128 || name.Any(char.IsControl)) throw new InvalidDataException("Invalid updated plugin display name.");
                    Dependencies.Clear(); Conflicts.Clear();
                    bool supportsGame = false;
                    foreach (var attribute in plugins[0].CustomAttributes)
                    {
                        if (attribute.AttributeType.FullName == "SNSRModLoader.TargetVersionAttribute") supportsGame |= Supports(Convert.ToInt32(attribute.ConstructorArguments[0].Value), game);
                        if (attribute.AttributeType.FullName == "SNSRModLoader.TargetVersionsAttribute")
                            foreach (var target in (CustomAttributeArgument[])attribute.ConstructorArguments[0].Value) supportsGame |= Supports(Convert.ToInt32(target.Value), game);
                        if (attribute.AttributeType.FullName == "SNSRModLoader.PluginDependencyAttribute") {
                            var id = (string)attribute.ConstructorArguments[0].Value;
                            if (!UpdateXml.Id(id) || id == Id || Dependencies.ContainsKey(id)) throw new InvalidDataException("Invalid updated dependency metadata.");
                            Dependencies.Add(id, new Version((string)attribute.ConstructorArguments[1].Value));
                        }
                        if (attribute.AttributeType.FullName == "SNSRModLoader.ConflictsAttribute") {
                            var id = (string)attribute.ConstructorArguments[0].Value;
                            if (!UpdateXml.Id(id) || string.Equals(id, Id, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid updated conflict metadata.");
                            Conflicts.Add(id);
                        }
                    }
                    if (!supportsGame) throw new InvalidDataException("Updated DLL does not declare support for this game.");
                }
            }
        }
        private static bool Supports(int target, string game)
        {
            if (target < 1 || target > UpdateXml.Games.Length) throw new InvalidDataException("Invalid DLL game target.");
            return target == Array.IndexOf(UpdateXml.Games, game) + 1;
        }
    }
}
