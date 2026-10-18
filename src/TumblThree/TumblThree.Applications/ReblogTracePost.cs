using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json.Linq;

namespace TumblThree.Applications
{
    internal sealed class ReblogTracePost
    {
        public string Id;
        public string Url;
        public string Date;
        public string From;
        public string Root;
        public readonly List<string> Trail = new List<string>();
        public readonly List<string> Candidates = new List<string>();

        // The API, SVC and NPF models use different naming conventions.
        private static object Get(object value, string name)
        {
            if (value == null) return null;
            var json = value as JObject;
            if (json != null)
                return json.Properties().FirstOrDefault(p => Key(p.Name) == Key(name))?.Value;
            var properties = value as IDictionary<string, object>;
            if (properties != null)
                return properties.FirstOrDefault(p => Key(p.Key) == Key(name)).Value;
            return value.GetType().GetProperties().FirstOrDefault(p => Key(p.Name) == Key(name))?.GetValue(value);
        }

        private static string Key(string name) => name.Replace("-", "").Replace("_", "").ToLowerInvariant();
        private static string Text(object value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

        private static string FirstText(object value, params string[] names)
        {
            return names.Select(name => Text(Get(value, name))).FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "";
        }

        private static IEnumerable<string> Names(object value)
        {
            var name = Text(Get(value, "Name"));
            if (!string.IsNullOrWhiteSpace(name))
            {
                yield return name;
                yield break;
            }
            name = Text(Get(value, "Url"));
            if (!string.IsNullOrWhiteSpace(name)) yield return ReblogTraceMatcher.ExtractBlogName(name);
            name = Text(Get(value, "Title"));
            if (!string.IsNullOrWhiteSpace(name)) yield return name;
        }

        private string Origin(object post, string prefix)
        {
            var name = Text(Get(post, prefix + "Name"));
            if (!string.IsNullOrWhiteSpace(name))
            {
                Candidates.Add(name);
                return name;
            }
            var url = Text(Get(post, prefix + "Url"));
            var title = Text(Get(post, prefix + "Title"));
            if (!string.IsNullOrWhiteSpace(url)) Candidates.Add(ReblogTraceMatcher.ExtractBlogName(url));
            if (!string.IsNullOrWhiteSpace(title)) Candidates.Add(title);
            return string.IsNullOrWhiteSpace(url) ? title : ReblogTraceMatcher.ExtractBlogName(url);
        }

        public static ReblogTracePost Read(object post)
        {
            var result = new ReblogTracePost
            {
                Id = FirstText(post, "Id", "IdString"),
                Url = FirstText(post, "PostUrl", "UrlWithSlug", "Url"),
                Date = FirstText(post, "Date", "DateGmt", "Timestamp", "UnixTimestamp")
            };
            result.From = result.Origin(post, "RebloggedFrom");
            result.Root = result.Origin(post, "RebloggedRoot");
            var trail = Get(post, "Trail") as IEnumerable;
            if (trail != null)
            {
                foreach (var entry in trail)
                {
                    foreach (var blog in new[] { Get(entry, "Blog"), Get(entry, "BrokenBlog") })
                    {
                        var names = Names(blog).ToList();
                        if (names.Count == 0) continue;
                        result.Trail.Add(names[0]);
                        result.Candidates.AddRange(names);
                    }
                    var brokenName = Text(Get(entry, "BrokenBlogName"));
                    if (!string.IsNullOrWhiteSpace(brokenName))
                    {
                        result.Trail.Add(brokenName);
                        result.Candidates.Add(brokenName);
                    }
                }
            }
            return result;
        }
    }

    [DataContract]
    public sealed class ReblogTraceTrail
    {
        [DataMember(Name = "blog", EmitDefaultValue = false)]
        public ReblogTraceBlog Blog { get; set; }

        [DataMember(Name = "broken_blog", EmitDefaultValue = false)]
        public ReblogTraceBlog BrokenBlog { get; set; }

        [DataMember(Name = "broken_blog_name", EmitDefaultValue = false)]
        public string BrokenBlogName { get; set; }

        [DataMember(Name = "brokenBlog", EmitDefaultValue = false)]
        private ReblogTraceBlog BrokenBlogCamel
        {
            get { return null; }
            set { if (BrokenBlog == null) BrokenBlog = value; }
        }
    }

    [DataContract]
    public sealed class ReblogTraceBlog
    {
        [DataMember(Name = "name", EmitDefaultValue = false)]
        public string Name { get; set; }

        [DataMember(Name = "url", EmitDefaultValue = false)]
        public string Url { get; set; }

        [DataMember(Name = "title", EmitDefaultValue = false)]
        public string Title { get; set; }
    }
}
