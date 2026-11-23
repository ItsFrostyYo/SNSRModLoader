using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml;

namespace SNSRModLoader
{
    public enum VerificationState { Pending, Available, Unavailable }
    public sealed class VerificationResult
    {
        public VerificationState State;
        public string Message;
        public XmlDocument Registry;
    }

    public static class Verification
    {
        public static VerificationResult Fetch(string url)
        { return FetchCore(url, Download); }
        internal static VerificationResult FetchCore(string url, Func<Uri, byte[]> download)
        {
            if (string.IsNullOrEmpty(url)) return new VerificationResult { State = VerificationState.Unavailable,
                Message = "Verified-mod check unavailable: the loader's GitHub repository has not been published/configured yet. Mod verification is unknown; this session is not eligible for leaderboard submissions." };
            try
            {
                var uri = ValidateUrl(url);
                using (var stream = new MemoryStream(download(uri)))
                    return new VerificationResult { State = VerificationState.Available, Registry = Parse(stream), Message = "Verified-mod list fetched from GitHub into memory." };
            }
            catch (Win32Exception ex)
            {
                return new VerificationResult { State = VerificationState.Unavailable,
                    Message = "No connection to GitHub; cannot check verified mods (network/TLS error " + ex.NativeErrorCode + "). Verification is unknown; this session is not eligible for leaderboard submissions." };
            }
            catch (Exception ex)
            {
                return new VerificationResult { State = VerificationState.Unavailable,
                    Message = "Cannot check verified mods: " + ex.Message + " Verification is unknown; this session is not eligible for leaderboard submissions." };
            }
        }
        public static Uri ValidateUrl(string url)
        {
            var uri = new Uri(url, UriKind.Absolute);
            if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.Host != "raw.githubusercontent.com" || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0)
                throw new ArgumentException("Verification requires an HTTPS raw.githubusercontent.com URL in the loader's repository.");
            return uri;
        }
        public static XmlDocument Parse(Stream stream)
        {
            var document = Registry.Read(stream);
            var root = document.DocumentElement;
            if (root == null || root.Name != "verifiedMods" || root.GetAttribute("schemaVersion") != "1") throw new FormatException("Invalid verified-mod registry schema.");
            foreach (XmlNode node in root.ChildNodes)
            {
                var mod = node as XmlElement;
                if (mod == null) continue;
                if (mod.Name != "mod" || !PluginContext.IsValidId(mod.GetAttribute("id")) || !Regex.IsMatch(mod.GetAttribute("sha256"), "^[0-9a-fA-F]{64}$")) throw new FormatException("Invalid verified-mod identity or SHA-256.");
                new Version(mod.GetAttribute("version"));
                if (mod.SelectNodes("target").Count == 0) throw new FormatException("Verified mod has no game targets.");
                foreach (XmlElement target in mod.SelectNodes("target"))
                {
                    var game = (GameVersion)Enum.Parse(typeof(GameVersion), target.GetAttribute("version"));
                    if (game == GameVersion.Unknown || !Enum.IsDefined(typeof(GameVersion), game)) throw new FormatException("Invalid verified-mod game target.");
                }
            }
            return document;
        }

        // Windows WinHTTP supplies modern TLS on Unity 5.6's old Mono runtime.
        // No WebClient disk cache, no fallback list, no credentials, no redirects.
        private static byte[] Download(Uri uri)
        {
            IntPtr session = IntPtr.Zero, connection = IntPtr.Zero, request = IntPtr.Zero;
            try
            {
                session = WinHttpOpen("SNSRModLoader/" + typeof(Plugin).Assembly.GetName().Version, 0, null, null, 0); Require(session != IntPtr.Zero);
                Require(WinHttpSetTimeouts(session, 2500, 2500, 2500, 2500));
                uint protocols = 0x800; Require(WinHttpSetOption(session, 84, ref protocols, 4));
                connection = WinHttpConnect(session, uri.Host, 443, 0); Require(connection != IntPtr.Zero);
                // GitHub's raw CDN can otherwise serve a previous approval list for minutes.
                var freshPath = uri.PathAndQuery + (uri.Query.Length == 0 ? "?" : "&") + "snsrCheck=" + DateTime.UtcNow.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
                request = WinHttpOpenRequest(connection, "GET", freshPath, null, null, IntPtr.Zero, 0x00800000); Require(request != IntPtr.Zero);
                uint redirects = 0; Require(WinHttpSetOption(request, 88, ref redirects, 4));
                Require(WinHttpSendRequest(request, null, 0, IntPtr.Zero, 0, 0, UIntPtr.Zero));
                Require(WinHttpReceiveResponse(request, IntPtr.Zero));
                uint status = 0, size = 4, index = 0;
                Require(WinHttpQueryHeaders(request, 19 | 0x20000000, null, ref status, ref size, ref index));
                if (status != 200) throw new IOException("GitHub returned HTTP " + status + ".");
                var timer = Stopwatch.StartNew();
                using (var data = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    uint read;
                    while (true)
                    {
                        Require(WinHttpReadData(request, buffer, (uint)buffer.Length, out read));
                        if (read == 0) return data.ToArray();
                        if (data.Length + read > 1024 * 1024 || timer.ElapsedMilliseconds > 10000) throw new IOException("GitHub verification response exceeded its size/time limit.");
                        data.Write(buffer, 0, (int)read);
                    }
                }
            }
            finally
            {
                if (request != IntPtr.Zero) WinHttpCloseHandle(request);
                if (connection != IntPtr.Zero) WinHttpCloseHandle(connection);
                if (session != IntPtr.Zero) WinHttpCloseHandle(session);
            }
        }
        private static void Require(bool result) { if (!result) throw new Win32Exception(Marshal.GetLastWin32Error()); }
        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr WinHttpOpen(string agent, uint access, string proxy, string bypass, uint flags);
        [DllImport("winhttp.dll", SetLastError = true)] private static extern bool WinHttpSetTimeouts(IntPtr session, int resolve, int connect, int send, int receive);
        [DllImport("winhttp.dll", SetLastError = true)] private static extern bool WinHttpSetOption(IntPtr handle, uint option, ref uint value, uint size);
        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr WinHttpConnect(IntPtr session, string server, ushort port, uint reserved);
        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr WinHttpOpenRequest(IntPtr connection, string verb, string name, string version, string referer, IntPtr accept, uint flags);
        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool WinHttpSendRequest(IntPtr request, string headers, uint headersLength, IntPtr optional, uint optionalLength, uint totalLength, UIntPtr context);
        [DllImport("winhttp.dll", SetLastError = true)] private static extern bool WinHttpReceiveResponse(IntPtr request, IntPtr reserved);
        [DllImport("winhttp.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool WinHttpQueryHeaders(IntPtr request, uint level, string name, ref uint value, ref uint size, ref uint index);
        [DllImport("winhttp.dll", SetLastError = true)] private static extern bool WinHttpReadData(IntPtr request, byte[] buffer, uint size, out uint read);
        [DllImport("winhttp.dll", SetLastError = true)] private static extern bool WinHttpCloseHandle(IntPtr handle);
    }
}
