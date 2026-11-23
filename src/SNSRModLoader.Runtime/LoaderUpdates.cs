using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System.Xml;

namespace SNSRModLoader.Runtime
{
    internal static class LoaderUpdates
    {
        internal static void Begin(string loader, SettingsFile settings, GameVersion gameVersion, List<PluginInfo> candidates)
        {
#if SNSR_DIAGNOSTICS
            if (Environment.GetEnvironmentVariable("SNSR_SMOKE_TEST") == "1") return;
#endif
            var url = settings.Get("checkForUpdates", true) ? settings.Get("updateManifestUrl", "") : "";
            if (url.Length == 0 && settings.Get("checkForUpdates", true)) url = OnlineSources.Get("updateManifestUrl");
            var checkMods = settings.Get("checkForModUpdates", true);
            var mods = candidates.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() == 1).Select(g => g.First()).ToArray();
            if (url.Length == 0 && !mods.Any(m => checkMods && m.AutomaticUpdate)) return;
            var game = Path.GetDirectoryName(Path.GetDirectoryName(loader));
            using (var process = Process.GetCurrentProcess())
            {
                var requestPath = Path.Combine(loader, "update-request.xml");
                var arguments = "--check-batch \"" + game + "\" " + process.Id + " " + process.StartTime.ToUniversalTime().Ticks + " \"" + requestPath + "\"";
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        Thread.Sleep(3000);
                        var xml = new XmlDocument(); var root = xml.CreateElement("updateRequest"); xml.AppendChild(root);
                        root.SetAttribute("schemaVersion", "1"); root.SetAttribute("gameVersion", gameVersion.ToString()); root.SetAttribute("loaderManifestUrl", url);
                        foreach (var info in mods)
                        {
                            var mod = xml.CreateElement("mod"); root.AppendChild(mod);
                            mod.SetAttribute("id", info.Id); mod.SetAttribute("name", info.Name); mod.SetAttribute("file", Path.GetFileName(info.Path));
                            mod.SetAttribute("version", info.Version); mod.SetAttribute("sha256", info.Sha256);
                            mod.SetAttribute("automaticUpdate", checkMods && info.AutomaticUpdate ? "true" : "false");
                            mod.SetAttribute("repository", info.UpdateRepository ?? ""); mod.SetAttribute("manifestUrl", info.UpdateManifestUrl ?? "");
                        }
                        var temporary = requestPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        xml.Save(temporary);
                        if (File.Exists(requestPath)) File.Replace(temporary, requestPath, null); else File.Move(temporary, requestPath);
                        using (var child = Process.Start(new ProcessStartInfo(Path.Combine(loader, "SNSRModLoader.Updater.exe"), arguments) { UseShellExecute = false, CreateNoWindow = true })) { }
                    }
                    catch (Exception ex) { LoaderRuntime.Diagnostic("Cannot start update check: " + ex.Message); }
                });
            }
        }
    }
}
