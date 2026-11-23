using System;
using System.Text.RegularExpressions;

namespace SNSRModLoader
{
    public static class UpdateSource
    {
        public static Uri Validate(string repository, string manifestUrl)
        {
            if (string.IsNullOrEmpty(repository) || !Regex.IsMatch(repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"))
                throw new ArgumentException("UpdateRepository must be owner/repository.");
            var uri = new Uri(manifestUrl, UriKind.Absolute);
            if (uri.Scheme != "https" || uri.Host != "raw.githubusercontent.com" || !uri.IsDefaultPort || uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "" ||
                !uri.AbsolutePath.StartsWith("/" + repository + "/", StringComparison.OrdinalIgnoreCase) || uri.Segments.Length < 5)
                throw new ArgumentException("Mod update manifest must be a raw HTTPS URL in its declared GitHub repository.");
            return uri;
        }
    }
}
