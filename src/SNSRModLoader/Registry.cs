using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml;

namespace SNSRModLoader
{
    public static class Registry
    {
        public static string Hash(string path)
        {
            using (var file = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
        }
        public static XmlDocument ReadEmbedded(string name)
        {
            var assembly = typeof(Registry).Assembly;
            foreach (var resource in assembly.GetManifestResourceNames())
                if (resource.EndsWith("." + name, StringComparison.Ordinal))
                using (var stream = assembly.GetManifestResourceStream(resource)) return Read(stream);
            throw new InvalidOperationException("Missing registry: " + name);
        }
        public static XmlDocument Read(Stream stream)
        {
            var settings = new XmlReaderSettings { ProhibitDtd = true, XmlResolver = null };
            using (var reader = XmlReader.Create(stream, settings))
            { var document = new XmlDocument { XmlResolver = null }; document.Load(reader); return document; }
        }
        public static GameVersion Detect(string assemblyPath)
        { return DetectHash(ReadEmbedded("game-builds.xml"), Hash(assemblyPath)); }
        public static GameVersion DetectHash(XmlDocument registry, string hash)
        {
            foreach (XmlElement build in registry.SelectNodes("/gameBuilds/build"))
                if (string.Equals(build.GetAttribute("sha256"), hash, StringComparison.OrdinalIgnoreCase))
                    return (GameVersion)Enum.Parse(typeof(GameVersion), build.GetAttribute("version"));
            return GameVersion.Unknown;
        }
        public static bool IsVerified(XmlDocument registry, string id, string version, string sha256, GameVersion game)
        {
            foreach (XmlElement mod in registry.SelectNodes("/verifiedMods/mod"))
            {
                if (mod.GetAttribute("id") != id || mod.GetAttribute("version") != version ||
                    !string.Equals(mod.GetAttribute("sha256"), sha256, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (XmlElement target in mod.SelectNodes("target"))
                    if (target.GetAttribute("version") == game.ToString()) return true;
            }
            return false;
        }
        public static string DisplayName(GameVersion game)
        {
            switch (game)
            {
                case GameVersion.September2018: return "September 2018";
                case GameVersion.December2021: return "December 2021";
                case GameVersion.March2023: return "March 2023";
                case GameVersion.October2025: return "October 2025";
                default: return "Unknown build";
            }
        }
    }
}
