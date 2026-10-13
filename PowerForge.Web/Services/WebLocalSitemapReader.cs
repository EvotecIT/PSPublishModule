using System.Xml;
using System.Xml.Linq;

namespace PowerForge.Web;

/// <summary>Reads generated local sitemap trees without fetching remote files.</summary>
internal static class WebLocalSitemapReader
{
    internal sealed record Entry(string Url, string? LastModified, string SourcePath);
    internal sealed record Result(IReadOnlyList<Entry> Entries, IReadOnlyList<string> InputPaths);

    internal static Result Read(string path, string? baseUrl, string? siteRoot = null, bool allowMissing = false)
    {
        var root = Path.GetFullPath(siteRoot ?? Path.GetDirectoryName(Path.GetFullPath(path))!);
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var visited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var entries = new List<Entry>();
        Uri.TryCreate(baseUrl?.TrimEnd('/') + "/", UriKind.Absolute, out var site);
        ReadDocument(Path.GetFullPath(path), 0);
        return new Result(entries, visited.ToArray());

        void ReadDocument(string input, int depth)
        {
            if (depth > 5 || visited.Count >= 1000)
                throw new InvalidOperationException("Local sitemap nesting exceeds five levels or 1000 files.");
            if (!visited.Add(input)) return;
            if (allowMissing && !File.Exists(input)) return;
            const long maxBytes = 50L * 1024 * 1024;
            if (new FileInfo(input).Length > maxBytes)
                throw new InvalidOperationException("Sitemap exceeds the 50 MiB protocol limit.");
            var document = PowerForge.Web.Protocols.Sitemaps.SitemapReader.Read(File.ReadAllText(input), strict: true);
            if (!document.IsIndex)
            {
                entries.AddRange(document.Entries.Select(entry => new Entry(entry.Url, entry.LastModifiedText, input)));
                return;
            }
            if (site is null || site.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("A local sitemap index requires an HTTP(S) baseUrl and matching siteRoot.");
            foreach (var sitemap in document.Entries)
            {
                var loc = sitemap.Url;
                if (!Uri.TryCreate(site, loc, out var child) || child.Scheme != site.Scheme || child.Authority != site.Authority ||
                    child.UserInfo.Length > 0 || child.Query.Length > 0 || child.Fragment.Length > 0)
                    throw new InvalidOperationException("A local sitemap index contains a location outside its configured host.");
                var relative = Uri.UnescapeDataString(site.MakeRelativeUri(child).ToString());
                var local = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!local.StartsWith(rootPrefix, comparison)) throw new InvalidOperationException("A local sitemap location escapes siteRoot.");
                // An index must not turn a URL into filesystem access outside the local publication tree.
                var current = root;
                foreach (var segment in Path.GetRelativePath(root, local).Split(Path.DirectorySeparatorChar))
                {
                    current = Path.Combine(current, segment);
                    if ((File.Exists(current) || Directory.Exists(current)) &&
                        (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("A local sitemap location traverses a symbolic link or reparse point.");
                }
                ReadDocument(local, depth + 1);
            }
        }
    }
}
