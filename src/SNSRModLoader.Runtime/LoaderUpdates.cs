using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace SNSRModLoader.Runtime
{
    internal static class LoaderUpdates
    {
        internal static void Begin(string loader, SettingsFile settings)
        {
            if (!settings.Get("checkForUpdates", true)) return;
#if SNSR_DIAGNOSTICS
            if (Environment.GetEnvironmentVariable("SNSR_SMOKE_TEST") == "1") return;
#endif
            var url = settings.Get("updateManifestUrl", "");
            if (url.Length == 0) url = OnlineSources.Get("updateManifestUrl");
            if (url.Length == 0) { LoaderRuntime.Diagnostic("Update check deferred: GitHub repository is not published/configured yet."); return; }
            var game = Path.GetDirectoryName(Path.GetDirectoryName(loader));
            using (var process = Process.GetCurrentProcess())
            {
                var arguments = "--check \"" + game + "\" " + process.Id + " " + process.StartTime.ToUniversalTime().Ticks + " \"" + url + "\"";
                if (url.Contains("\"") || url.Contains("\r") || url.Contains("\n")) { LoaderRuntime.Diagnostic("Invalid update URL in config."); return; }
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try
                    {
                        Thread.Sleep(3000);
                        using (var child = Process.Start(new ProcessStartInfo(Path.Combine(loader, "SNSRModLoader.Updater.exe"), arguments) { UseShellExecute = false, CreateNoWindow = true })) { }
                    }
                    catch (Exception ex) { LoaderRuntime.Diagnostic("Cannot start update check: " + ex.Message); }
                });
            }
        }
    }
}
