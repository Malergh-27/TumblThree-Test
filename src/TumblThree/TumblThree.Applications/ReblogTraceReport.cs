using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace TumblThree.Applications
{
    internal sealed class ReblogTraceReport
    {
        private const string Header = "post_id,post_url,date,reblogged_from,reblogged_root,matched_source,trail_blogs,downloaded_files";
        private readonly string directory;
        private readonly string mutexName;
        private readonly bool writeReport;
        private readonly bool discover;
        private readonly ReblogTraceMatcher matcher;
        private readonly string currentBlog;

        public ReblogTraceReport(string directory, bool writeReport, bool discover, ReblogTraceMatcher matcher, string currentBlog)
        {
            this.directory = Path.GetFullPath(directory);
            using (var hash = SHA256.Create())
                mutexName = "TumblThree.ReblogTrace." + BitConverter.ToString(hash.ComputeHash(
                    Encoding.UTF8.GetBytes(this.directory.TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant()))).Replace("-", "");
            this.writeReport = writeReport;
            this.discover = discover;
            this.matcher = matcher;
            this.currentBlog = ReblogTraceMatcher.Normalize(currentBlog);
        }

        public void Seed(string id)
        {
            id = NormalizeId(id);
            if (writeReport && id != null)
                Update(new[] { id, "", "", "", "", "seed", "", "" }, null, true);
        }

        public Action<string> Record(ReblogTracePost post, string source)
        {
            if (discover)
            {
                WithWriteLock(() =>
                {
                    Directory.CreateDirectory(directory);
                    var path = Path.Combine(directory, "trace-discovered-blogs.txt");
                    var names = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
                    var keys = new HashSet<string>(names.Select(ReblogTraceMatcher.Normalize), StringComparer.Ordinal);
                    foreach (var candidate in new[] { post.From }.Concat(post.Trail))
                    {
                        var name = ReblogTraceMatcher.ExtractBlogName(candidate);
                        if (ReblogTraceMatcher.IsValidBlogName(name) && !matcher.IsSource(name) &&
                            ReblogTraceMatcher.Normalize(name) != currentBlog && keys.Add(ReblogTraceMatcher.Normalize(name)))
                            names.Add(name);
                    }
                    File.WriteAllLines(path, names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), new UTF8Encoding(false));
                });
            }
            var id = NormalizeId(post.Id);
            if (!writeReport || id == null) return null;
            var row = new[] { id, post.Url, post.Date, post.From, post.Root, source,
                string.Join(";", post.Trail.Distinct(StringComparer.OrdinalIgnoreCase)), "" };
            Update(row, null);
            return file => Update(row, file);
        }

        private void Update(string[] row, string downloadedFile, bool isSeed = false)
        {
            WithWriteLock(() =>
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "trace-report.csv");
                var rows = File.Exists(path) ? Parse(File.ReadAllText(path)).Skip(1)
                    .Where(r => r.Length == 8 && NormalizeId(r[0]) != null)
                    .Select(r => { r[0] = NormalizeId(r[0]); return r; })
                    .GroupBy(r => r[0]).Select(g => g.First()).ToList() : new List<string[]>();
                var existing = rows.FirstOrDefault(r => r[0] == row[0]);
                if (existing == null)
                {
                    existing = (string[])row.Clone();
                    rows.Add(existing);
                }
                else if (!isSeed)
                {
                    for (var i = 1; i < 7; i++) existing[i] = row[i] ?? "";
                }
                if (!string.IsNullOrEmpty(downloadedFile))
                {
                    var files = existing[7].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    if (!files.Contains(downloadedFile) && !files.Contains(SafeCell(downloadedFile))) files.Add(downloadedFile);
                    existing[7] = string.Join(";", files);
                }
                var content = Header + "\r\n" + string.Join("\r\n", rows.Select(r => string.Join(",", r.Select(Escape)))) + "\r\n";
                // Replace only after the entire new CSV has been written.
                var staging = path + "." + Guid.NewGuid().ToString("N") + ".new";
                try
                {
                    File.WriteAllText(staging, content, new UTF8Encoding(false));
                    if (File.Exists(path)) File.Replace(staging, path, null);
                    else File.Move(staging, path);
                }
                finally
                {
                    if (File.Exists(staging)) File.Delete(staging);
                }
            });
        }

        private static string NormalizeId(string value)
        {
            ulong id;
            return ulong.TryParse((value ?? "").Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0
                ? id.ToString(CultureInfo.InvariantCulture) : null;
        }

        private void WithWriteLock(Action write)
        {
            using (var mutex = new Mutex(false, mutexName))
            {
                bool acquired = false;
                try
                {
                    try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(30)); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) throw new IOException("Timed out waiting for the reblog trace report writer.");
                    write();
                }
                finally
                {
                    if (acquired) mutex.ReleaseMutex();
                }
            }
        }

        private static string SafeCell(string value)
        {
            value = value ?? "";
            var significant = value.TrimStart();
            // Quoting CSV alone does not prevent spreadsheet formula evaluation.
            if ((significant.Length > 0 && "=+-@".IndexOf(significant[0]) >= 0) ||
                (value.Length > 0 && (value[0] == '\t' || value[0] == '\r' || value[0] == '\n')))
                return "'" + value;
            return value;
        }

        private static string Escape(string value)
        {
            value = SafeCell(value);
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static IEnumerable<string[]> Parse(string content)
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (var i = 0; i < content.Length; i++)
            {
                var c = content[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < content.Length && content[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (!quoted && c == ',') { fields.Add(field.ToString()); field.Clear(); }
                else if (!quoted && (c == '\r' || c == '\n'))
                {
                    if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n') i++;
                    fields.Add(field.ToString());
                    yield return fields.ToArray();
                    fields.Clear();
                    field.Clear();
                }
                else field.Append(c);
            }
            if (field.Length > 0 || fields.Count > 0)
            {
                fields.Add(field.ToString());
                yield return fields.ToArray();
            }
        }
    }
}
