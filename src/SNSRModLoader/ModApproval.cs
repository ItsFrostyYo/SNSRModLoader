using System;
using System.IO;
using System.Linq;

namespace SNSRModLoader
{
    public enum PluginStatus { Unknown, Verified, Known, Conflicted }

    public sealed class ModCatalogResult
    {
        public VerificationResult Verified, Known;
    }

    public static class ModApproval
    {
        public static ModCatalogResult Fetch(bool enabled, string verifiedUrl, string knownUrl, PluginInfo[] plugins, GameVersion game)
        { return FetchCore(enabled, verifiedUrl, knownUrl, plugins, game, Verification.Fetch, Verification.FetchKnown); }

        private static ModCatalogResult FetchCore(bool enabled, string verifiedUrl, string knownUrl, PluginInfo[] plugins, GameVersion game,
            Func<string, VerificationResult> fetchVerified, Func<string, VerificationResult> fetchKnown)
        {
            if (!enabled) return new ModCatalogResult {
                Verified = new VerificationResult { State = VerificationState.Disabled, Message = "Mod classification checks disabled in loader.ini; leaderboard eligibility is unknown." },
                Known = new VerificationResult { State = VerificationState.Disabled }
            };
            var verified = fetchVerified(verifiedUrl);
            bool needsKnown = plugins.Any(p => !p.HasAdditionalAssemblies &&
                (verified.State != VerificationState.Available || !Registry.IsVerified(verified.Registry, p.Id, p.Version, p.Sha256, game)));
            return new ModCatalogResult { Verified = verified, Known = needsKnown ? fetchKnown(knownUrl) :
                new VerificationResult { State = VerificationState.NotRequired, Message = "All loaded mods are verified; no known-mod check is required." } };
        }

        public static void Classify(PluginInfo info, ModCatalogResult catalogs, GameVersion game)
        {
            info.Verified = false; info.UCEAllowed = false;
            if (info.InstalledConflicts.Count != 0 || info.Status == PluginStatus.Conflicted) { info.Status = PluginStatus.Conflicted; return; }
            info.Status = PluginStatus.Unknown;
            if (info.HasAdditionalAssemblies) return;
            if (catalogs.Verified.State == VerificationState.Available && Registry.IsVerified(catalogs.Verified.Registry, info.Id, info.Version, info.Sha256, game)) {
                info.Status = PluginStatus.Verified; info.Verified = true; return;
            }
            bool uce;
            if (catalogs.Known.State == VerificationState.Available && Registry.IsKnown(catalogs.Known.Registry, info.Id, info.Version, info.Sha256, game, out uce)) {
                info.Status = PluginStatus.Known; info.UCEAllowed = uce;
            }
        }

        public static string Warning(PluginInfo info)
        {
            string label = info.Name + " [" + info.Id + "]" + (string.IsNullOrEmpty(info.Path) ? "" : " (" + Path.GetFileName(info.Path) + ")");
            if (info.Status == PluginStatus.Verified || info.Status == PluginStatus.Conflicted) return null;
            if (info.Status == PluginStatus.Known)
                return label + (info.UCEAllowed
                    ? " is a known Plugin, but it is not allowed for leaderboard submission on the main leaderboard. It is only allowed for the Unofficial Category Extensions Leaderboard."
                    : " is a known Plugin, but it is not allowed for leaderboard submission.");
            return label + " is an unknown mod. It is not allowed for submission on any leaderboard.";
        }
    }
}
