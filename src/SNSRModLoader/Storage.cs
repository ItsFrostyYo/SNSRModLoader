using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SNSRModLoader
{
    public sealed class PluginLogger
    {
        public static Action<string> Sink;
        private readonly string source;
        public PluginLogger(string source) { this.source = source; }
        private void Write(string level, object message)
        { var sink = Sink; if (sink != null) sink("[" + level + ":" + source + "] " + message); }
        public void Info(object message) { Write("Info", message); }
        public void Warning(object message) { Write("Warning", message); }
        public void Error(object message) { Write("Error", message); }
    }

    public sealed class SettingsFile
    {
        private readonly string path;
        private readonly SortedDictionary<string, string> values = new SortedDictionary<string, string>(StringComparer.Ordinal);
        public SettingsFile(string path)
        {
            this.path = path;
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadAllLines(path))
            {
                if (line.TrimStart().StartsWith("#") || line.TrimStart().StartsWith(";")) continue;
                int pos = line.IndexOf('=');
                if (pos > 0) values[line.Substring(0, pos).Trim()] = line.Substring(pos + 1).Trim();
            }
        }
        public T Get<T>(string key, T fallback)
        {
            string value;
            if (!values.TryGetValue(key, out value)) return fallback;
            try
            {
                if (typeof(T).IsEnum) return (T)Enum.Parse(typeof(T), value, true);
                return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
            }
            catch (Exception) { return fallback; }
        }
        public void Set<T>(string key, T value)
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(key) || key.IndexOfAny(new[] { '=', '\r', '\n' }) >= 0 || text.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("Settings keys and values must fit on one line.");
            values[key] = text;
        }
        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var lines = new List<string>();
            foreach (var entry in values) lines.Add(entry.Key + "=" + entry.Value);
            File.WriteAllLines(path, lines.ToArray(), Encoding.UTF8);
        }
    }
}
