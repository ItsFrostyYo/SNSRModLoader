using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace SNSRModLoader.Updater
{
    internal static class Program
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBoxW(IntPtr owner, string text, string title, uint flags);
        private static string game, work;
        private static readonly object logGate = new object();
        private static void Log(string message)
        { try { lock (logGate) File.AppendAllText(Path.Combine(game, "Mods", "Loader", "updater.log"), DateTime.UtcNow.ToString("o") + " " + message + Environment.NewLine); } catch { } }
        private static void Main(string[] args)
        {
            try
            {
                if (args.Length != 5 || (args[0] != "--check" && args[0] != "--apply" && args[0] != "--check-batch" && args[0] != "--apply-batch")) return;
                game = Path.GetFullPath(args[1]);
                if (!File.Exists(Path.Combine(game, "Subnautica.exe"))) throw new IOException("Game executable not found.");
                using (var mutex = new Mutex(false, "Local\\SNSRModLoader.Update." + StableId(game)))
                {
                    bool acquired;
                    try { acquired = mutex.WaitOne(args[0].StartsWith("--apply", StringComparison.Ordinal) ? 30000 : 0); } catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) return;
                    try {
                        if (args[0] == "--check") Check(args);
                        else if (args[0] == "--apply") Apply(args);
                        else if (args[0] == "--check-batch") CheckBatch(args);
                        else ApplyBatch(args);
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception ex)
            {
                Log(ex.ToString());
                // Offline/check failures are informational; update/install failures need action.
                if (work != null || (args.Length > 0 && args[0].StartsWith("--apply", StringComparison.Ordinal))) MessageBoxW(IntPtr.Zero, ex.Message + "\n\nSee Mods/Loader/updater.log. Restart the game only after resolving an incomplete rollback.", "SNSRModLoader update failed", 0x10);
            }
        }
        private static string StableId(string text)
        { using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(text.ToLowerInvariant()))).Replace("-", ""); }
        private static void CheckBatch(string[] args)
        {
            var expected = Path.Combine(game, "Mods", "Loader", "update-request.xml");
            if (!string.Equals(Path.GetFullPath(args[4]), expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid update request location.");
            var request = BatchRequest.Read(expected, game);
            var batch = UpdateBatch.Check(request, Assembly.GetExecutingAssembly().GetName().Version, game, Download, Log);
            if (!batch.HasUpdates) { Log("Batch check complete; no compatible newer updates found."); return; }
            if (MessageBoxW(IntPtr.Zero, "Updates available:\n\n" + batch.Summary() + "\n\nPrepare all these updates together? After downloading, close the game once to install the complete batch. Mods/settings/config are preserved. Updated mods must pass verification again on next launch.\n\nYes: prepare updates. No: remind me next launch.", "SNSRModLoader updates available", 0x24) != 6) return;
            work = Path.Combine(Path.GetTempPath(), "SNSRModLoader", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            batch.Stage(work, game, Download);
            var helper = Path.Combine(work, "SNSRModLoader.Updater.exe");
            File.Copy(Assembly.GetExecutingAssembly().Location, helper);
            File.Copy(typeof(Mono.Cecil.AssemblyDefinition).Assembly.Location, Path.Combine(work, "Mono.Cecil.dll"));
            SaveChecker();
            Process.Start(new ProcessStartInfo(helper, "--apply-batch " + Quote(game) + " " + args[2] + " " + args[3] + " " + Quote(work)) { UseShellExecute = false, CreateNoWindow = true });
            Log("Complete update batch validated and staged; waiting for game exit.");
        }
        private static void ApplyBatch(string[] args)
        {
            work = Path.GetFullPath(args[4]);
            var expected = Path.Combine(Path.GetTempPath(), "SNSRModLoader") + Path.DirectorySeparatorChar;
            if (!work.StartsWith(expected, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), work, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid update staging location.");
            var batch = UpdateBatch.ReadStaged(work, game, Assembly.GetExecutingAssembly().GetName().Version);
            MessageBoxW(IntPtr.Zero, "Updates are ready:\n\n" + batch.Summary() + "\n\nClose Subnautica normally when ready. All updates will install together after the game finishes closing.", "SNSRModLoader updates ready", 0x40);
            WaitForGame(args);
            batch.Install(work, game);
            Log("Installed complete update batch:\n" + batch.Summary());
            MessageBoxW(IntPtr.Zero, "All prepared updates were installed successfully.\n\nYou can now restart Subnautica.", "SNSRModLoader updated", 0x40);
            CleanupWork();
        }
        private static void WaitForGame(string[] args)
        {
            try
            {
                using (var process = Process.GetProcessById(int.Parse(args[2])))
                    if (process.StartTime.ToUniversalTime().Ticks == long.Parse(args[3])) process.WaitForExit();
            }
            catch (ArgumentException) { }
            foreach (var process in Process.GetProcessesByName("Subnautica"))
            using (process)
            {
                try { if (string.Equals(process.MainModule.FileName, Path.Combine(game, "Subnautica.exe"), StringComparison.OrdinalIgnoreCase)) throw new IOException("This game was restarted before the update. Close it and retry on next launch."); }
                catch (System.ComponentModel.Win32Exception) { throw new IOException("Cannot confirm the game is closed; update cancelled."); }
            }
            var checker = work == null ? null : Path.Combine(work, "checker.txt");
            if (checker == null || !File.Exists(checker)) { Thread.Sleep(300); return; }
            if (new FileInfo(checker).Length > 128) throw new IOException("Invalid checking-process record.");
            var checking = File.ReadAllLines(checker);
            try {
                using (var process = Process.GetProcessById(int.Parse(checking[0])))
                    if (process.StartTime.ToUniversalTime().Ticks == long.Parse(checking[1])) process.WaitForExit();
            } catch (ArgumentException) { }
        }
        private static void SaveChecker()
        {
            using (var process = Process.GetCurrentProcess())
                File.WriteAllLines(Path.Combine(work, "checker.txt"), new[] { process.Id.ToString(), process.StartTime.ToUniversalTime().Ticks.ToString() });
        }
        private static void CleanupWork()
        {
            // Only clean this successful transaction; failed staging is kept for recovery.
            try
            {
                foreach (var directory in Directory.GetDirectories(work)) Directory.Delete(directory, true);
                foreach (var file in Directory.GetFiles(work))
                    if (!string.Equals(Path.GetFileName(file), "SNSRModLoader.Updater.exe", StringComparison.OrdinalIgnoreCase) && !string.Equals(Path.GetFileName(file), "Mono.Cecil.dll", StringComparison.OrdinalIgnoreCase)) File.Delete(file);
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        private static void Check(string[] args)
        {
            if (string.IsNullOrEmpty(args[4])) { Log("Update check deferred: GitHub repository endpoint is not configured yet."); return; }
            var manifest = UpdatePackage.ManifestUrl(args[4]);
            var bytes = Download(manifest, 1024 * 1024, false);
            var update = UpdatePackage.Parse(bytes, manifest);
            var current = Assembly.GetExecutingAssembly().GetName().Version;
            if (update.Version <= current) { Log("Loader is current: " + current); return; }
            Log("Update available: " + current + " -> " + update.Version);
            if (MessageBoxW(IntPtr.Zero, "SNSRModLoader " + update.Version + " is available (installed: " + current + ").\n\nDownload and prepare this update? After downloading, close the game to install it, then restart the game. The loader and its dependencies will be updated together. Your mods, settings and config will be preserved.\n\nYes: prepare update. No: remind me next launch.", "SNSRModLoader update available", 0x24) != 6) return;
            work = Path.Combine(Path.GetTempPath(), "SNSRModLoader", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            File.WriteAllBytes(Path.Combine(work, "manifest.xml"), bytes);
            File.WriteAllText(Path.Combine(work, "source.txt"), manifest.AbsoluteUri);
            var package = Path.Combine(work, "package.zip");
            File.WriteAllBytes(package, Download(update.Download, 64 * 1024 * 1024, true));
            update.Extract(package, Path.Combine(work, "stage"));
            var helper = Path.Combine(work, "SNSRModLoader.Updater.exe");
            File.Copy(Assembly.GetExecutingAssembly().Location, helper);
            File.Copy(typeof(Mono.Cecil.AssemblyDefinition).Assembly.Location, Path.Combine(work, "Mono.Cecil.dll"));
            SaveChecker();
            Process.Start(new ProcessStartInfo(helper, "--apply " + Quote(game) + " " + args[2] + " " + args[3] + " " + Quote(work)) { UseShellExecute = false, CreateNoWindow = true });
            Log("Update validated and staged; waiting for game exit.");
        }
        private static string Quote(string text)
        { if (text.Contains("\"") || text.EndsWith("\\")) throw new ArgumentException("Invalid updater argument."); return "\"" + text + "\""; }
        private static void Apply(string[] args)
        {
            work = Path.GetFullPath(args[4]);
            var expected = Path.Combine(Path.GetTempPath(), "SNSRModLoader") + Path.DirectorySeparatorChar;
            if (!work.StartsWith(expected, StringComparison.OrdinalIgnoreCase) || !string.Equals(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), work, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid update staging location.");
            var update = UpdatePackage.Parse(File.ReadAllBytes(Path.Combine(work, "manifest.xml")), UpdatePackage.ManifestUrl(File.ReadAllText(Path.Combine(work, "source.txt"))));
            // Revalidate the archive in the applying process too.
            update.Extract(Path.Combine(work, "package.zip"), Path.Combine(work, "stage"));
            MessageBoxW(IntPtr.Zero, "SNSRModLoader " + update.Version + " is ready.\n\nClose Subnautica normally to install the update. You can continue playing until then. This updater will wait for the game to finish closing.", "SNSRModLoader update ready", 0x40);
            WaitForGame(args);
            update.Install(Path.Combine(work, "stage"), game);
            Log("Updated complete loader package to " + update.Version + ". Restart the game.");
            MessageBoxW(IntPtr.Zero, "SNSRModLoader " + update.Version + " was installed successfully.\n\nYou can now restart Subnautica.", "SNSRModLoader updated", 0x40);
            CleanupWork();
        }
        private static byte[] Download(Uri uri, int limit, bool release)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var timer = Stopwatch.StartNew();
            for (int redirect = 0; redirect < 5; redirect++)
            {
                if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo != "" || uri.Fragment != "" ||
                    (release ? uri.Host != "github.com" && uri.Host != "release-assets.githubusercontent.com" && uri.Host != "objects.githubusercontent.com" : uri.Host != "raw.githubusercontent.com"))
                    throw new IOException("Unexpected update download host.");
                var request = (HttpWebRequest)WebRequest.Create(uri);
                request.UserAgent = "SNSRModLoader-Updater"; request.AllowAutoRedirect = false; request.Timeout = 15000; request.ReadWriteTimeout = 15000;
                request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400) { uri = new Uri(uri, response.Headers["Location"]); continue; }
                    if (response.StatusCode != HttpStatusCode.OK || response.ContentLength > limit) throw new IOException("Invalid update download response.");
                    using (var input = response.GetResponseStream()) using (var output = new MemoryStream())
                    {
                        var buffer = new byte[16384]; int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                        { if (output.Length + count > limit || timer.Elapsed.TotalSeconds > 90) throw new IOException("Update exceeded size/time limit."); output.Write(buffer, 0, count); }
                        return output.ToArray();
                    }
                }
            }
            throw new IOException("Too many download redirects.");
        }
    }
}
