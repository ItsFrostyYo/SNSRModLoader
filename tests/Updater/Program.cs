using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using SNSRModLoader.Updater;

internal static class Program
{
    private static int count;
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); count++; Console.WriteLine("PASS " + label); }
    private static void Reject(Action action, string label) { try { action(); } catch { Check(true, label); return; } throw new Exception(label); }
    private static byte[] Bytes(XmlDocument doc) { return Encoding.UTF8.GetBytes(doc.OuterXml); }
    private static void Main(string[] args)
    {
        var root = Path.Combine(Path.GetFullPath("artifacts"), "updater-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var manifestUrl = UpdatePackage.ManifestUrl("https://raw.githubusercontent.com/owner/SNSRModLoader/main/loader-update.xml");
        Reject(() => UpdatePackage.ManifestUrl("http://raw.githubusercontent.com/owner/repo/main/a.xml"), "HTTP manifest rejected");
        Reject(() => UpdatePackage.ManifestUrl("https://example.com/a.xml"), "foreign manifest host rejected");
        foreach (var bad in new[] { "../winhttp.dll", "Mods/Loader/../Save.dll", "Mods/Loader/a.dll:ads", "Mods\\Loader\\a.dll", "Mods/OtherMod.dll", "Subnautica.exe", "Mods/Settings/a.txt", "Mods/Loader/Config/custom.ini" })
            Check(!UpdatePackage.OwnedPath(bad), "unsafe/unowned path rejected: " + bad);
        var packageDir = Path.Combine(root, "package");
        var paths = new[] { "winhttp.dll", UpdatePackage.Inventory, "Mods/Loader/SNSRModLoader.dll", "Mods/Loader/SNSRModLoader.Runtime.dll", "Mods/Loader/SNSRModLoader.Bootstrap.dll", "Mods/Loader/SNSRModLoader.Updater.exe", "Mods/Loader/NewDependency.dll", "Mods/Loader/Config/loader.ini", "Mods/Loader/Licenses/license.txt" };
        foreach (var path in paths) { var full = UpdatePackage.SafePath(packageDir, path); Directory.CreateDirectory(Path.GetDirectoryName(full)); File.WriteAllText(full, "new " + path); }
        File.WriteAllLines(UpdatePackage.SafePath(packageDir, UpdatePackage.Inventory), paths);
        var zip = Path.Combine(root, "release.zip");
        ZipFile.CreateFromDirectory(packageDir, zip);
        var xml = new XmlDocument();
        xml.LoadXml("<loaderUpdate schemaVersion='1' version='0.2.0' url='https://github.com/owner/SNSRModLoader/releases/download/v0.2.0/release.zip' sha256='" + UpdatePackage.Hash(zip) + "'/>");
        foreach (var path in paths) { var file = xml.CreateElement("file"); file.SetAttribute("path", path); file.SetAttribute("sha256", UpdatePackage.Hash(UpdatePackage.SafePath(packageDir, path))); xml.DocumentElement.AppendChild(file); }
        var update = UpdatePackage.Parse(Bytes(xml), manifestUrl);
        Check(update.Version == new Version("0.2.0") && update.Files.Count == paths.Length, "complete GitHub update manifest parsed");
        var foreign = (XmlDocument)xml.Clone(); foreign.DocumentElement.SetAttribute("url", "https://github.com/attacker/repo/releases/download/v1/a.zip");
        Reject(() => UpdatePackage.Parse(Bytes(foreign), manifestUrl), "different repository package rejected");
        var duplicate = (XmlDocument)xml.Clone(); duplicate.DocumentElement.AppendChild(duplicate.DocumentElement.FirstChild.Clone());
        Reject(() => UpdatePackage.Parse(Bytes(duplicate), manifestUrl), "duplicate manifest path rejected");
        var missing = (XmlDocument)xml.Clone(); missing.DocumentElement.RemoveChild(missing.DocumentElement.FirstChild);
        Reject(() => UpdatePackage.Parse(Bytes(missing), manifestUrl), "incomplete release rejected");
        Reject(() => UpdatePackage.Parse(Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY y SYSTEM 'file:///C:/Windows/win.ini'>]><loaderUpdate>&y;</loaderUpdate>"), manifestUrl), "XML external entities rejected");
        var stage = Path.Combine(root, "stage"); update.Extract(zip, stage);
        Check(File.ReadAllText(UpdatePackage.SafePath(stage, "winhttp.dll")) == "new winhttp.dll", "archive and per-file hashes validated during extraction");
        var badZip = Path.Combine(root, "extra.zip"); File.Copy(zip, badZip);
        using (var extra = ZipFile.Open(badZip, ZipArchiveMode.Update)) using (var writer = new StreamWriter(extra.CreateEntry("Mods/OtherMod.dll").Open())) writer.Write("bad");
        var originalHash = update.Sha256; update.Sha256 = UpdatePackage.Hash(badZip);
        Reject(() => update.Extract(badZip, Path.Combine(root, "bad-stage")), "unexpected archive file rejected even with valid archive hash");
        update.Sha256 = originalHash;
        Reject(() => update.Extract(badZip, Path.Combine(root, "bad-hash")), "archive SHA-256 mismatch rejected");
        var game = Path.Combine(root, "game");
        Directory.CreateDirectory(Path.Combine(game, "Mods", "Loader", "Config"));
        File.WriteAllText(Path.Combine(game, "winhttp.dll"), "old proxy");
        File.WriteAllText(Path.Combine(game, "Mods", "Loader", "installed-proxy.sha256"), UpdatePackage.Hash(Path.Combine(game, "winhttp.dll")));
        File.WriteAllText(UpdatePackage.SafePath(game, "Mods/Loader/Config/loader.ini"), "user config");
        File.WriteAllText(UpdatePackage.SafePath(game, "Mods/Loader/OldDependency.dll"), "old dependency");
        File.WriteAllText(UpdatePackage.SafePath(game, "Mods/Loader/SNSRModLoader.dll"), "old loader");
        File.WriteAllLines(UpdatePackage.SafePath(game, UpdatePackage.Inventory), new[] { "winhttp.dll", "Mods/Loader/SNSRModLoader.dll", "Mods/Loader/OldDependency.dll", UpdatePackage.Inventory });
        Directory.CreateDirectory(Path.Combine(game, "Mods", "Settings"));
        File.WriteAllText(Path.Combine(game, "Mods", "UserMod.dll"), "user mod");
        File.WriteAllText(Path.Combine(game, "Mods", "Settings", "custom.txt"), "user data");
        var snapshot = Directory.GetFiles(game, "*", SearchOption.AllDirectories).ToDictionary(p => p, File.ReadAllText);
        Reject(() => update.Install(stage, game, i => { if (i == 7) { Check(!File.Exists(UpdatePackage.SafePath(game, "Mods/Loader/OldDependency.dll")) && File.Exists(UpdatePackage.SafePath(game, "Mods/Loader/SNSRModLoader.Updater.exe")), "fault injected after dependency removal and new file creation"); throw new IOException("injected write failure"); } }), "partial installation failure reported");
        Check(snapshot.All(p => File.Exists(p.Key) && File.ReadAllText(p.Key) == p.Value) && Directory.GetFiles(game, "*", SearchOption.AllDirectories).Length == snapshot.Count, "failed update rolls back original files and removes new files");
        File.WriteAllText(UpdatePackage.SafePath(stage, "Mods/Loader/NewDependency.dll"), "tampered");
        Reject(() => update.Install(stage, game), "changed staging file rejected before install");
        update.Extract(zip, stage);
        update.Install(stage, game);
        Check(File.ReadAllText(UpdatePackage.SafePath(game, "Mods/Loader/SNSRModLoader.dll")) == "new Mods/Loader/SNSRModLoader.dll", "loader replaced");
        Check(File.ReadAllText(UpdatePackage.SafePath(game, "Mods/Loader/SNSRModLoader.Updater.exe")) == "new Mods/Loader/SNSRModLoader.Updater.exe", "updater replaces itself from separate staging helper");
        Check(!File.Exists(UpdatePackage.SafePath(game, "Mods/Loader/OldDependency.dll")) && File.Exists(UpdatePackage.SafePath(game, "Mods/Loader/NewDependency.dll")), "obsolete dependency removed and new dependency installed");
        Check(File.ReadAllText(UpdatePackage.SafePath(game, "Mods/Loader/Config/loader.ini")) == "user config" && File.ReadAllText(Path.Combine(game, "Mods", "UserMod.dll")) == "user mod" && File.ReadAllText(Path.Combine(game, "Mods", "Settings", "custom.txt")) == "user data", "mods, settings and existing config preserved");
        Check(File.ReadAllText(Path.Combine(game, "Mods", "Loader", "installed-proxy.sha256")) == UpdatePackage.Hash(Path.Combine(game, "winhttp.dll")), "native proxy ownership marker updated");
        File.Delete(UpdatePackage.SafePath(game, "Mods/Loader/Config/loader.ini"));
        update.Install(stage, game);
        Check(File.ReadAllText(UpdatePackage.SafePath(game, "Mods/Loader/Config/loader.ini")) == "new Mods/Loader/Config/loader.ini", "missing default config restored");
        File.WriteAllText(Path.Combine(game, "winhttp.dll"), "another loader");
        Reject(() => update.Install(stage, game), "another loader's changed proxy protected");
        if (args.Length == 2)
        {
            var release = UpdatePackage.Parse(File.ReadAllBytes(args[0]), manifestUrl);
            release.Extract(args[1], Path.Combine(root, "real-release"));
            Check(true, "actual generated release ZIP and manifest validated");
        }
        Console.WriteLine(count + " updater assertions passed.");
    }
}
