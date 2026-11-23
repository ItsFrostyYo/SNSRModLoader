using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;

namespace SNSRModLoader.Updater
{
    internal sealed class UpdatePackage
    {
        internal Version Version;
        internal Uri Download;
        internal string Sha256;
        internal readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal const string Inventory = "Mods/Loader/installed-files.txt";
        internal static Uri ManifestUrl(string url)
        {
            var uri = new Uri(url, UriKind.Absolute);
            if (uri.Scheme != "https" || uri.Host != "raw.githubusercontent.com" || !uri.IsDefaultPort || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" || uri.Segments.Length < 5)
                throw new InvalidDataException("Updates require a raw HTTPS GitHub manifest URL.");
            return uri;
        }
        internal static bool OwnedPath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Contains("\\") || path.Contains(":") || path.Split('/').Any(p => p == "" || p == "." || p == ".." || p.EndsWith(".") || p.EndsWith(" "))) return false;
            if (path == "winhttp.dll" || path == Inventory || path == "Mods/Loader/installed-proxy.sha256" || path == "Mods/Loader/Config/loader.ini") return true;
            if (path.StartsWith("Mods/Loader/Licenses/", StringComparison.Ordinal) && path.Substring(21).IndexOf('/') < 0)
                return Regex.IsMatch(path.Substring(21), @"^[A-Za-z0-9_.-]+\.txt$");
            return Regex.IsMatch(path, @"^Mods/Loader/[A-Za-z0-9_.-]+\.(dll|exe)$");
        }
        internal static UpdatePackage Parse(byte[] data, Uri manifest)
        {
            if (data.Length > 1024 * 1024) throw new InvalidDataException("Update manifest too large.");
            var xml = new XmlDocument { XmlResolver = null };
            using (var stream = new MemoryStream(data))
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) xml.Load(reader);
            var root = xml.DocumentElement;
            if (root == null || root.Name != "loaderUpdate" || root.GetAttribute("schemaVersion") != "1") throw new InvalidDataException("Unsupported update manifest.");
            var result = new UpdatePackage { Version = new Version(root.GetAttribute("version")), Download = new Uri(root.GetAttribute("url")), Sha256 = root.GetAttribute("sha256") };
            var repository = manifest.AbsolutePath.Split('/');
            if (result.Download.Scheme != "https" || result.Download.Host != "github.com" || !result.Download.IsDefaultPort || result.Download.UserInfo != "" || result.Download.Query != "" || result.Download.Fragment != "" ||
                !result.Download.AbsolutePath.StartsWith("/" + repository[1] + "/" + repository[2] + "/releases/download/", StringComparison.Ordinal))
                throw new InvalidDataException("Package must be a release in the manifest's GitHub repository.");
            if (!HashValid(result.Sha256)) throw new InvalidDataException("Invalid package SHA-256.");
            foreach (XmlNode node in root.ChildNodes)
            {
                var file = node as XmlElement;
                if (file == null) continue;
                string path = file.GetAttribute("path"), hash = file.GetAttribute("sha256");
                if (file.Name != "file" || !OwnedPath(path) || !HashValid(hash) || result.Files.ContainsKey(path)) throw new InvalidDataException("Invalid or duplicate package file: " + path);
                result.Files.Add(path, hash);
            }
            foreach (var required in new[] { "winhttp.dll", Inventory, "Mods/Loader/SNSRModLoader.dll", "Mods/Loader/SNSRModLoader.Runtime.dll", "Mods/Loader/SNSRModLoader.Bootstrap.dll", "Mods/Loader/SNSRModLoader.Updater.exe" })
                if (!result.Files.ContainsKey(required)) throw new InvalidDataException("Incomplete loader release: " + required);
            if (result.Files.Count > 128) throw new InvalidDataException("Too many package files.");
            return result;
        }
        private static bool HashValid(string hash) { return Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$"); }
        internal static string Hash(string path)
        { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant(); }
        internal static string SafePath(string root, string path)
        {
            if (!OwnedPath(path)) throw new InvalidDataException("Invalid loader path: " + path);
            return ResolvePath(root, path);
        }
        internal static string ModPath(string root, string filename)
        {
            if (string.IsNullOrEmpty(filename) || !Regex.IsMatch(filename, @"^[A-Za-z0-9_.-]+\.dll$")) throw new InvalidDataException("Invalid mod DLL filename.");
            return ResolvePath(root, "Mods/" + filename);
        }
        private static string ResolvePath(string root, string path)
        {
            root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
            var full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path escaped loader root.");
            for (var current = full; current != null; current = Path.GetDirectoryName(current))
                if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw new IOException("Updater does not write through links: " + current);
            return full;
        }
        internal void Extract(string zipPath, string stage)
        {
            if (!string.Equals(Hash(zipPath), Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Package SHA-256 mismatch.");
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            using (var zip = ZipFile.OpenRead(zipPath))
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith("/") && entry.Length == 0) continue;
                string hash;
                if (!Files.TryGetValue(entry.FullName, out hash) || !found.Add(entry.FullName) || entry.Length > 32 * 1024 * 1024 || (total += entry.Length) > 64 * 1024 * 1024)
                    throw new InvalidDataException("Unexpected/duplicate/oversized archive entry: " + entry.FullName);
                var target = SafePath(stage, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var input = entry.Open()) using (var output = File.Create(target))
                {
                    var buffer = new byte[16384]; int count;
                    while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                    { if (output.Length + count > entry.Length) throw new InvalidDataException("Archive entry exceeded its declared size."); output.Write(buffer, 0, count); }
                    if (output.Length != entry.Length) throw new InvalidDataException("Truncated archive entry.");
                }
                if (!string.Equals(Hash(target), hash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("File SHA-256 mismatch: " + entry.FullName);
            }
            if (found.Count != Files.Count) throw new InvalidDataException("Release archive is incomplete.");
            var inventory = File.ReadAllLines(SafePath(stage, Inventory));
            if (inventory.Length != Files.Count || !new HashSet<string>(inventory, StringComparer.OrdinalIgnoreCase).SetEquals(Files.Keys)) throw new InvalidDataException("Release inventory does not match manifest.");
        }
        internal void Install(string stage, string game, Action<int> afterWrite = null)
        {
            var transaction = new FileTransaction();
            PrepareInstall(stage, game, transaction);
            transaction.Apply(afterWrite);
        }
        internal void PrepareInstall(string stage, string game, FileTransaction transaction)
        {
            // Preflight everything before changing any installed file.
            foreach (var file in Files)
            {
                if (!string.Equals(Hash(SafePath(stage, file.Key)), file.Value, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Staged file changed: " + file.Key);
                SafePath(game, file.Key);
            }
            var previous = new string[0];
            var inventoryPath = SafePath(game, Inventory);
            if (File.Exists(inventoryPath)) previous = File.ReadAllLines(inventoryPath);
            if (previous.Length > 128 || previous.Any(p => !OwnedPath(p))) throw new InvalidDataException("Invalid installed loader inventory.");
            var paths = previous.Concat(Files.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Where(p => !p.StartsWith("Mods/Loader/Config/", StringComparison.Ordinal) || !File.Exists(SafePath(game, p)));
            foreach (var path in paths)
            {
                var target = SafePath(game, path);
                if (path == "Mods/Loader/installed-proxy.sha256") continue;
                if (Files.ContainsKey(path)) transaction.Copy(SafePath(stage, path), target);
                else transaction.Delete(target);
            }
            var marker = Path.Combine(game, "Mods", "Loader", "installed-proxy.sha256");
            SafePath(game, "Mods/Loader/SNSRModLoader.dll");
            if (File.Exists(marker) && (File.GetAttributes(marker) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked ownership marker.");
            if (!File.Exists(marker) || !File.Exists(Path.Combine(game, "winhttp.dll")) || !string.Equals(File.ReadAllText(marker).Trim(), Hash(Path.Combine(game, "winhttp.dll")), StringComparison.OrdinalIgnoreCase))
                throw new IOException("winhttp.dll ownership changed; reinstall the loader manually.");
            transaction.Bytes(marker, System.Text.Encoding.UTF8.GetBytes(Hash(SafePath(stage, "winhttp.dll"))));
        }
    }
}
