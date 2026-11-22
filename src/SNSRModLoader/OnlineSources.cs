using System;

namespace SNSRModLoader
{
    // Published builds embed their own repository endpoints. Config may override them.
    public static class OnlineSources
    {
        public static string Get(string name)
        {
            string configured = "";
            try { configured = Registry.ReadEmbedded("online.xml").DocumentElement.GetAttribute(name); }
            catch (InvalidOperationException) { }
            if (configured.Length > 0) return configured;
            const string repository = "https://raw.githubusercontent.com/ItsFrostyYo/SNSRModLoader/main/";
            if (name == "verifiedModsUrl") return repository + "verified-mods.xml";
            if (name == "updateManifestUrl") return repository + "loader-update.xml";
            return "";
        }
    }
}
