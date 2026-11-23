using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;

namespace SNSRModLoader.Updater
{
    internal sealed class ModCandidate
    {
        internal ModPackage Package;
        internal byte[] Manifest;
    }
    internal sealed class UpdateBatch
    {
        internal BatchRequest Request;
        internal UpdatePackage Loader;
        internal byte[] LoaderManifest;
        internal readonly List<ModCandidate> Mods = new List<ModCandidate>();
        internal bool HasUpdates { get { return Loader != null || Mods.Count != 0; } }
        internal static UpdateBatch Check(BatchRequest request, Version current, string game,
            Func<Uri, int, bool, byte[]> download, Action<string> log)
        {
            var batch = new UpdateBatch { Request = request };
            if (!string.IsNullOrEmpty(request.LoaderManifest))
                try
                {
                    var uri = UpdatePackage.ManifestUrl(request.LoaderManifest);
                    var bytes = download(uri, 1024 * 1024, false);
                    var package = UpdatePackage.Parse(bytes, uri);
                    if (package.Version > current) { batch.Loader = package; batch.LoaderManifest = bytes; }
                    else log("Loader is current: " + current);
                }
                catch (Exception ex) { log("Loader update check unavailable: " + ex.Message); }
            var effectiveLoader = batch.Loader == null ? current : batch.Loader.Version;
            // Every opted-in mod is checked before any prompt/download/install.
            Parallel.ForEach(request.Mods.Where(m => m.Enabled), new ParallelOptions { MaxDegreeOfParallelism = 4 }, installed =>
            {
                try
                {
                    var uri = SNSRModLoader.UpdateSource.Validate(installed.Repository, installed.Manifest);
                    if (!string.Equals(UpdatePackage.Hash(UpdatePackage.ModPath(game, installed.FileName)), installed.Hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Installed DLL changed since discovery.");
                    var bytes = download(uri, 64 * 1024, false);
                    var package = ModPackage.Parse(bytes, installed, request.Game);
                    if (package.Version <= installed.Version) { log("Mod is current: " + installed.Id + " " + installed.Version); return; }
                    if (package.MinimumLoader > effectiveLoader) throw new InvalidDataException("Requires loader " + package.MinimumLoader + "; no compatible loader update was found.");
                    lock (batch.Mods) batch.Mods.Add(new ModCandidate { Package = package, Manifest = bytes });
                    log("Mod update available: " + installed.Name + " " + installed.Version + " -> " + package.Version);
                }
                catch (Exception ex) { log("Mod update check skipped for " + installed.Id + ": " + ex.Message); }
            });
            batch.Mods.Sort((a, b) => string.Compare(a.Package.Id, b.Package.Id, StringComparison.OrdinalIgnoreCase));
            return batch;
        }
        internal string Summary()
        {
            var lines = new List<string>();
            if (Loader != null) lines.Add("SNSRModLoader -> " + Loader.Version);
            lines.AddRange(Mods.Select(m => m.Package.Installed.Name + ": " + m.Package.Installed.Version + " -> " + m.Package.Version));
            return string.Join("\n", lines.Take(15).ToArray()) + (lines.Count > 15 ? "\n...and " + (lines.Count - 15) + " more (see updater.log)" : "");
        }
        internal void Stage(string work, string game, Func<Uri, int, bool, byte[]> download)
        {
            SaveRequest(Path.Combine(work, "request.xml"));
            long total = 0;
            if (Loader != null)
            {
                File.WriteAllBytes(Path.Combine(work, "loader.xml"), LoaderManifest);
                var zip = download(Loader.Download, 64 * 1024 * 1024, true); total += zip.Length;
                File.WriteAllBytes(Path.Combine(work, "loader.zip"), zip);
                Loader.Extract(Path.Combine(work, "loader.zip"), Path.Combine(work, "loader-stage"));
            }
            Parallel.ForEach(Mods, new ParallelOptions { MaxDegreeOfParallelism = 4 }, candidate =>
            {
                var mod = candidate.Package;
                var bytes = download(mod.Download, 32 * 1024 * 1024, true);
                if (System.Threading.Interlocked.Add(ref total, bytes.Length) > 256L * 1024 * 1024) throw new IOException("Update batch exceeds 256 MiB.");
                var target = UpdatePackage.ModPath(Path.Combine(work, "mod-stage"), mod.Installed.FileName);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.WriteAllBytes(target, bytes);
                mod.ValidateDll(target, Request.Game, Path.Combine(game, "Mods", "Loader"));
                File.WriteAllBytes(Path.Combine(work, "mod-" + mod.Id + ".xml"), candidate.Manifest);
            });
            ValidateDependencies();
        }
        private void SaveRequest(string path)
        {
            var xml = new XmlDocument(); var root = xml.CreateElement("updateRequest"); xml.AppendChild(root);
            root.SetAttribute("schemaVersion", "1"); root.SetAttribute("gameVersion", Request.Game); root.SetAttribute("loaderManifestUrl", Request.LoaderManifest ?? "");
            root.SetAttribute("loaderSelected", Loader != null ? "true" : "false");
            foreach (var request in Request.Mods)
            {
                var mod = xml.CreateElement("mod"); root.AppendChild(mod);
                mod.SetAttribute("id", request.Id); mod.SetAttribute("name", request.Name); mod.SetAttribute("file", request.FileName);
                mod.SetAttribute("version", request.Version.ToString()); mod.SetAttribute("sha256", request.Hash);
                mod.SetAttribute("automaticUpdate", request.Enabled ? "true" : "false");
                mod.SetAttribute("updateSelected", Mods.Any(m => m.Package.Id == request.Id) ? "true" : "false");
                mod.SetAttribute("repository", request.Repository ?? ""); mod.SetAttribute("manifestUrl", request.Manifest ?? "");
            }
            xml.Save(path);
        }
        internal static UpdateBatch ReadStaged(string work, string game, Version current)
        {
            var batch = new UpdateBatch { Request = BatchRequest.Read(Path.Combine(work, "request.xml"), game) };
            if (batch.Request.LoaderSelected)
            {
                batch.Loader = UpdatePackage.Parse(File.ReadAllBytes(Path.Combine(work, "loader.xml")), UpdatePackage.ManifestUrl(batch.Request.LoaderManifest));
                if (batch.Loader.Version <= current) throw new InvalidDataException("Staged loader update is not newer.");
                batch.Loader.Extract(Path.Combine(work, "loader.zip"), Path.Combine(work, "loader-stage"));
            }
            foreach (var installed in batch.Request.Mods.Where(m => m.Enabled && m.Selected))
            {
                var manifest = Path.Combine(work, "mod-" + installed.Id + ".xml");
                if (!File.Exists(manifest)) throw new InvalidDataException("A selected mod update is missing: " + installed.Id);
                var mod = ModPackage.Parse(File.ReadAllBytes(manifest), installed, batch.Request.Game);
                if (mod.Version <= installed.Version || mod.MinimumLoader > (batch.Loader == null ? current : batch.Loader.Version)) throw new InvalidDataException("Staged mod version is incompatible.");
                mod.ValidateDll(UpdatePackage.ModPath(Path.Combine(work, "mod-stage"), installed.FileName), batch.Request.Game, Path.Combine(game, "Mods", "Loader"));
                batch.Mods.Add(new ModCandidate { Package = mod });
            }
            if (!batch.HasUpdates) throw new InvalidDataException("Empty staged update batch.");
            batch.ValidateDependencies();
            return batch;
        }
        private void ValidateDependencies()
        {
            var versions = Request.Mods.ToDictionary(m => m.Id, m => m.Version, StringComparer.OrdinalIgnoreCase);
            foreach (var mod in Mods) versions[mod.Package.Id] = mod.Package.Version;
            foreach (var candidate in Mods)
            {
                foreach (var dependency in candidate.Package.Dependencies) {
                    Version version;
                    if (!versions.TryGetValue(dependency.Key, out version) || version < dependency.Value) throw new InvalidDataException(candidate.Package.Id + " requires " + dependency.Key + " " + dependency.Value + "; this batch cannot satisfy it.");
                }
                foreach (var conflict in candidate.Package.Conflicts)
                    if (versions.ContainsKey(conflict)) throw new InvalidDataException(candidate.Package.Id + " update conflicts with installed mod " + conflict);
            }
            var complete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in Mods) CheckCycle(candidate.Package.Id, complete, visiting);
        }
        private void CheckCycle(string id, HashSet<string> complete, HashSet<string> visiting)
        {
            if (complete.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidDataException("Updated mod dependency cycle: " + id);
            var candidate = Mods.FirstOrDefault(m => string.Equals(m.Package.Id, id, StringComparison.OrdinalIgnoreCase));
            if (candidate != null) foreach (var dependency in candidate.Package.Dependencies.Keys) CheckCycle(dependency, complete, visiting);
            visiting.Remove(id); complete.Add(id);
        }
        internal void Install(string work, string game, Action<int> afterWrite = null)
        {
            foreach (var installed in Request.Mods) {
                var path = UpdatePackage.ModPath(game, installed.FileName);
                if (!File.Exists(path) || !string.Equals(UpdatePackage.Hash(path), installed.Hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Installed mod set changed before the update: " + installed.Id);
            }
            var transaction = new FileTransaction();
            if (Loader != null) Loader.PrepareInstall(Path.Combine(work, "loader-stage"), game, transaction);
            foreach (var candidate in Mods)
            {
                var mod = candidate.Package;
                var installed = UpdatePackage.ModPath(game, mod.Installed.FileName);
                if (!File.Exists(installed) || !string.Equals(UpdatePackage.Hash(installed), mod.Installed.Hash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Mod changed before install: " + mod.Id);
                var staged = UpdatePackage.ModPath(Path.Combine(work, "mod-stage"), mod.Installed.FileName);
                mod.ValidateDll(staged, Request.Game, Path.Combine(game, "Mods", "Loader"));
                transaction.Copy(staged, installed);
            }
            transaction.Apply(afterWrite);
        }
    }
}
