using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using SNSRModLoader;
using Mono.Cecil;

internal static class Program
{
    private static int assertions;
    private static void Check(bool condition, string label)
    { if (!condition) throw new Exception(label); assertions++; Console.WriteLine("PASS " + label); }
    private static void Throws(Action action, string label)
    { try { action(); } catch { Check(true, label); return; } throw new Exception(label); }
    private static System.Xml.XmlDocument Xml(string xml)
    { using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml))) return Registry.Read(stream); }
    private static PluginInfo Info(string id, params string[] dependencies)
    {
        var p = new PluginInfo { Id = id, Name = id, Path = id + ".dll", Version = "1.0.0" };
        foreach (var d in dependencies) p.Dependencies.Add(new Dependency { Id = d, MinimumVersion = new Version("1.0.0") });
        return p;
    }
    private static void Main(string[] args)
    {
        var repo = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        var temporary = Path.Combine(repo, "artifacts", "tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        var versions = new[] { GameVersion.September2018, GameVersion.December2021, GameVersion.March2023, GameVersion.October2025 };
        var installs = new[] { "Subnautica2018", "Subnautica2021", "Subnautica2023", "SubnauticaCurrent" };
        for (int i = 0; i < versions.Length; i++)
            Check(Registry.Detect(Path.Combine(@"C:\Program Files (x86)\Steam\steamapps\common", installs[i], "Subnautica_Data", "Managed", "Assembly-CSharp.dll")) == versions[i], "exact Assembly-CSharp detection " + versions[i]);
        Check(Registry.DetectHash(Registry.ReadEmbedded("game-builds.xml"), new string('0', 64)) == GameVersion.Unknown, "unknown build rejected");
        var whitelist = Xml("<verifiedMods><mod id='test' version='1.0.0' sha256='abc'><target version='September2018'/></mod></verifiedMods>");
        Check(Registry.IsVerified(whitelist, "test", "1.0.0", "ABC", GameVersion.September2018), "verified exact identity and hash");
        Check(!Registry.IsVerified(whitelist, "test", "1.0.0", "different", GameVersion.September2018), "changed binary unverified");
        Check(!Registry.IsVerified(whitelist, "test", "1.0.0", "abc", GameVersion.October2025), "wrong game unverified");
        Check(!Registry.IsVerified(whitelist, "test", "2.0.0", "abc", GameVersion.September2018), "wrong plugin version unverified");
        Throws(() => Xml("<!DOCTYPE x [<!ENTITY y SYSTEM 'file:///C:/Windows/win.ini'>]><verifiedMods>&y;</verifiedMods>"), "registry external entities rejected");
        var context = new PluginContext("test.plugin", temporary, GameVersion.September2018);
        Throws(() => context.GetSettingsPath("../escape.txt"), "settings traversal rejected");
        Throws(() => new PluginContext("../escape", temporary, GameVersion.September2018), "unsafe plugin ID rejected");
        context.Settings.Set("number", 17); context.Settings.Set("enabled", true); context.Settings.Save();
        var settings = new SettingsFile(context.GetSettingsPath("settings.ini"));
        Check(settings.Get("number", 0) == 17 && settings.Get("enabled", false), "settings roundtrip");
        settings.Set("number", "invalid"); Check(settings.Get("number", 42) == 42, "malformed setting fallback");
        Throws(() => settings.Set("x\ny", "injected"), "settings key injection rejected");
        var warnings = new List<string>();
        var order = Discovery.Order(new List<PluginInfo> { Info("child", "parent"), Info("parent"), Info("missing", "unknown"), Info("cycle1", "cycle2"), Info("cycle2", "cycle1") }, warnings.Add);
        Check(string.Join(",", order.Select(p => p.Id)) == "parent,child" && warnings.Count == 3, "dependency order, missing dependency and cycles");
        var conflictA = Info("a"); var conflictB = Info("b");
        conflictA.Conflicts.Add(new PluginConflict { Id = "b", Name = "Friendly B" });
        warnings.Clear(); var conflicts = Discovery.Order(new List<PluginInfo> { conflictA, conflictB, Info("child", "b"), Info("unrelated") }, warnings.Add);
        Check(conflicts.Count == 1 && conflicts[0].Id == "unrelated" && warnings.Any(w => w.Contains("a.dll") && w.Contains("b.dll") && w.Contains("Both plugins skipped")), "one-sided conflict blocks both and their dependents");
        warnings.Clear(); Check(Discovery.Order(new List<PluginInfo> { conflictA }, warnings.Add).Count == 1 && warnings.Count == 0, "absent conflicting plugin does not block loading");
        var mods = Path.Combine(temporary, "Mods"); Directory.CreateDirectory(mods);
        File.Copy(Path.Combine(repo, "artifacts/examples/HelloMultiVersion.dll"), Path.Combine(mods, "Hello.dll"));
        var secondary = Path.Combine(temporary, "Secondary.dll");
        using (var a = AssemblyDefinition.ReadAssembly(Path.Combine(repo, "artifacts/examples/HelloMultiVersion.dll")))
        {
            a.MainModule.Types.Single(t => t.Name == "HelloMultiVersion").CustomAttributes[0].ConstructorArguments[0] = new CustomAttributeArgument(a.MainModule.TypeSystem.String, "test.secondary");
            a.Write(secondary);
        }
        File.Copy(secondary, Path.Combine(mods, "Secondary.dll"));
        using (var resolver = new DefaultAssemblyResolver())
        {
            resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(Plugin).Assembly.Location));
            using (var a = AssemblyDefinition.ReadAssembly(Path.Combine(repo, "artifacts/examples/HelloMultiVersion.dll"), new ReaderParameters { AssemblyResolver = resolver }))
            {
            var type = a.MainModule.Types.Single(t => t.Name == "HelloMultiVersion");
            type.CustomAttributes[0].ConstructorArguments[0] = new CustomAttributeArgument(a.MainModule.TypeSystem.String, "test.single-target");
            type.CustomAttributes.RemoveAt(1);
            var target = new CustomAttribute(a.MainModule.ImportReference(typeof(TargetVersionAttribute).GetConstructor(new[] { typeof(GameVersion) })));
            target.ConstructorArguments.Add(new CustomAttributeArgument(a.MainModule.ImportReference(typeof(GameVersion)), (int)GameVersion.September2018));
            type.CustomAttributes.Add(target); a.Write(Path.Combine(mods, "LegacyOnly.dll"));
            }
        }
        File.Copy(Path.Combine(repo, "src/SNSRModLoader/bin/Release/net35/SNSRModLoader.dll"), Path.Combine(mods, "Foreign.dll"));
        File.WriteAllText(Path.Combine(mods, "Corrupt.dll"), "not a DLL");
        foreach (var version in versions)
        {
            warnings.Clear(); var plugins = Discovery.Scan(mods, version, warnings.Add);
            Check(plugins.Count == (version == GameVersion.September2018 ? 3 : 2), "version-filtered discovery " + version);
            Check(plugins.Any(p => p.Id == "test.secondary" && p.Targets.Length == 4 && p.Targets.Contains(version)), "multi-version plugin targets " + version);
            Check(warnings.Any(w => w.Contains("Foreign.dll") && w.Contains("not a valid plugin")) && warnings.Any(w => w.Contains("Corrupt.dll")), "foreign and corrupt DLL filename warnings " + version);
            Check(plugins.All(p => !p.Verified), "discovery does not trust a local registry " + version);
        }
        File.Copy(Path.Combine(mods, "Hello.dll"), Path.Combine(mods, "Duplicate.dll"));
        warnings.Clear(); Check(Discovery.Scan(mods, GameVersion.October2025, warnings.Add).Count == 1 && warnings.Any(w => w.Contains("duplicate plugin ID")), "duplicate IDs reject every copy");
        var dll = Path.Combine(mods, "Duplicate.dll");
        using (var a = AssemblyDefinition.ReadAssembly(dll)) { a.MainModule.Types.Single(t => t.Name == "HelloMultiVersion").CustomAttributes.RemoveAt(1); a.Write(Path.Combine(mods, "MissingTargets.dll")); }
        warnings.Clear(); Discovery.Scan(mods, GameVersion.September2018, warnings.Add);
        Check(warnings.Any(w => w.Contains("MissingTargets.dll") && w.Contains("Declare TargetVersion")), "missing targets rejected");
        var conflictMods = Path.Combine(temporary, "ConflictMods"); Directory.CreateDirectory(conflictMods);
        File.Copy(secondary, Path.Combine(conflictMods, "Secondary.dll"));
        using (var a = AssemblyDefinition.ReadAssembly(Path.Combine(repo, "artifacts/examples/HelloMultiVersion.dll")))
        {
            var constructor = typeof(ConflictsAttribute).GetConstructor(new[] { typeof(string), typeof(string) });
            var attribute = new CustomAttribute(a.MainModule.ImportReference(constructor));
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(a.MainModule.TypeSystem.String, "test.secondary"));
            attribute.ConstructorArguments.Add(new CustomAttributeArgument(a.MainModule.TypeSystem.String, "Secondary Plugin"));
            a.MainModule.Types.Single(t => t.Name == "HelloMultiVersion").CustomAttributes.Add(attribute);
            a.Write(Path.Combine(conflictMods, "Hello.dll"));
        }
        warnings.Clear(); Check(Discovery.Scan(conflictMods, GameVersion.September2018, warnings.Add).Count == 0 && warnings.Any(w => w.Contains("Plugin conflict") && w.Contains("Hello.dll") && w.Contains("Secondary.dll")), "Conflicts attribute read from DLL metadata before execution");
        Check(Verification.Fetch("").State == VerificationState.Unavailable, "unpublished GitHub endpoint is unavailable");
        Throws(() => Verification.ValidateUrl("http://raw.githubusercontent.com/owner/repo/main/list.xml"), "HTTP verification endpoint rejected");
        Throws(() => Verification.ValidateUrl("https://example.com/list.xml"), "non-GitHub endpoint rejected");
        Throws(() => Verification.Parse(new MemoryStream(Encoding.UTF8.GetBytes("<wrong/>"))), "invalid remote schema rejected");
        var valid = "<verifiedMods schemaVersion='1'><mod id='test' version='1.0.0' sha256='" + new string('a', 64) + "'><target version='September2018'/></mod></verifiedMods>";
        Check(Verification.Parse(new MemoryStream(Encoding.UTF8.GetBytes(valid))).DocumentElement.Name == "verifiedMods", "remote registry parsed in memory");
        var url = "https://raw.githubusercontent.com/owner/SNSRModLoader/main/verified-mods.xml";
        var online = Verification.FetchCore(url, u => Encoding.UTF8.GetBytes(valid));
        Check(online.State == VerificationState.Available && online.Registry != null, "successful remote response enables verification");
        var offline = Verification.FetchCore(url, u => { throw new System.ComponentModel.Win32Exception(12029); });
        Check(offline.State == VerificationState.Unavailable && offline.Registry == null && offline.Message.Contains("No connection"), "offline response warns and does not fall back to local verification");
        var malformed = Verification.FetchCore(url, u => Encoding.UTF8.GetBytes("<wrong/>"));
        Check(malformed.State == VerificationState.Unavailable && malformed.Registry == null, "malformed remote response fails closed");
        using (var runtime = AssemblyDefinition.ReadAssembly(Path.Combine(repo, "src/SNSRModLoader.Runtime/bin/Release/net35/SNSRModLoader.Runtime.dll")))
        {
            Check(!runtime.MainModule.GetTypeReferences().Any(t => t.Name == "uGUI_MainMenu"), "watermark runtime has no main-menu dependency");
            foreach (var install in installs)
            using (var game = AssemblyDefinition.ReadAssembly(Path.Combine(@"C:\Program Files (x86)\Steam\steamapps\common", install, "Subnautica_Data", "Managed", "Assembly-CSharp.dll")))
            {
                var native = game.MainModule.GetType("uGUI_BuildWatermark");
                Check(native.Methods.Any(m => m.Name == "Start" && m.ReturnType.FullName == "System.Collections.IEnumerator") && native.Methods.Any(m => m.Name == "UpdateText"), "native watermark lifecycle hooks exist " + install);
            }
        }
        Console.WriteLine(assertions + " assertions passed. Fixtures: " + temporary);
    }
}
