using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TumblThree.Applications
{
    public sealed class ReblogTraceMatcher
    {
        private static readonly Regex Deactivated = new Regex(@"^(.+?)-deactivated\d*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private readonly Dictionary<string, string> sources = new Dictionary<string, string>(StringComparer.Ordinal);

        public ReblogTraceMatcher(string sourceBlogs)
        {
            foreach (var source in Regex.Split(sourceBlogs ?? "", @"[,\s]+").Where(s => s.Length > 0))
            {
                var name = ExtractBlogName(source.TrimEnd('*')).TrimEnd('*');
                var key = Normalize(name);
                if (key.Length > 0 && !sources.ContainsKey(key)) sources.Add(key, name);
            }
        }

        public ReblogTraceMatcher(string globalSourceBlogs, string blogSourceBlogs)
            : this((globalSourceBlogs ?? "") + "\n" + (blogSourceBlogs ?? ""))
        {
        }

        public bool IsEnabled => sources.Count != 0;

        public static string ExtractBlogName(string value)
        {
            value = (value ?? "").Trim().TrimStart('@');
            Uri uri;
            if (Uri.TryCreate(value.Contains("://") ? value : "https://" + value, UriKind.Absolute, out uri))
            {
                if (string.Equals(uri.Host, "tumblr.com", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Host, "www.tumblr.com", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 0) return "";
                    var index = 0;
                    if (parts[0] == "blog" && parts.Length > 1)
                        index = parts[1] == "view" && parts.Length > 2 ? 2 : 1;
                    else if (parts[0] == "dashboard" && parts.Length > 2 && parts[1] == "blog")
                        index = 2;
                    return parts[index].TrimStart('@');
                }
                if (uri.Host.EndsWith(".tumblr.com", StringComparison.OrdinalIgnoreCase))
                    return uri.Host.Substring(0, uri.Host.Length - ".tumblr.com".Length);
            }
            return value;
        }

        public static string Normalize(string value)
        {
            value = ExtractBlogName(value).ToLowerInvariant();
            var match = Deactivated.Match(value);
            if (match.Success) value = match.Groups[1].Value;
            return value.Replace("-", "").Replace("_", "");
        }

        public string Match(IEnumerable<string> blogNames)
        {
            foreach (var name in blogNames ?? Enumerable.Empty<string>())
            {
                var source = MatchSource(name);
                if (source != null) return source;
            }
            return null;
        }

        private string MatchSource(string name)
        {
            var key = Normalize(name);
            string source;
            if (sources.TryGetValue(key, out source)) return source;
            return sources.Where(entry => key.StartsWith(entry.Key, StringComparison.Ordinal))
                .OrderByDescending(entry => entry.Key.Length).FirstOrDefault().Value;
        }

        public bool IsSource(string name) => MatchSource(name) != null;

        public static bool IsValidBlogName(string name)
        {
            return Regex.IsMatch(name ?? "", @"^[A-Za-z0-9][A-Za-z0-9_-]*$") && name.Length <= 64 &&
                !new[] { "www", "assets", "media", "static", "blog", "dashboard" }.Contains(name.ToLowerInvariant());
        }
    }
}
