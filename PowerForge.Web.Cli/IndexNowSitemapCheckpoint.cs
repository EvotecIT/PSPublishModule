using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge.Web.Cli;

/// <summary>
/// Keeps a durable, URL-and-lastmod checkpoint for dynamic sitemaps. A checkpoint is published only
/// after every selected IndexNow request succeeds, so a failed run can safely retry the same URLs.
/// </summary>
internal sealed class IndexNowSitemapCheckpoint
{
    private const int SchemaVersion = 1;
    private const long MaxSitemapBytes = 50L * 1024 * 1024;
    private const long MaxCheckpointBytes = 32L * 1024 * 1024;

    private sealed class StoredState
    {
        public int Version { get; set; }
        public Dictionary<string, string?> Urls { get; set; } = new(StringComparer.Ordinal);
    }

    private readonly string _path;
    private readonly Dictionary<string, string?> _current;
    private readonly Dictionary<string, string?> _previous;

    private IndexNowSitemapCheckpoint(
        string path,
        Dictionary<string, string?> current,
        Dictionary<string, string?> previous)
    {
        _path = path;
        _current = current;
        _previous = previous;
    }

    /// <summary>Loads the current sitemap and the previous successful submission state.</summary>
    internal static IndexNowSitemapCheckpoint Load(string sitemapPath, string statePath, string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var site) || site.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(site.UserInfo) || !string.IsNullOrEmpty(site.Query) || !string.IsNullOrEmpty(site.Fragment))
            throw new InvalidOperationException("indexnow: sitemapStatePath requires an HTTPS baseUrl without credentials, query, or fragment.");

        var sitemapFullPath = Path.GetFullPath(sitemapPath);
        var stateFullPath = Path.GetFullPath(statePath);
        if (string.Equals(sitemapFullPath, stateFullPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("indexnow: sitemap and sitemapStatePath must be different files.");

        var current = ReadSitemap(sitemapFullPath, site);
        var previous = ReadState(stateFullPath);
        if (current.Count == 0 && previous.Count > 0)
            throw new InvalidOperationException("indexnow: sitemap is empty; preserving the previous successful checkpoint.");
        return new IndexNowSitemapCheckpoint(stateFullPath, current, previous);
    }

    /// <summary>Returns only new URLs or URLs whose sitemap lastmod value changed.</summary>
    internal string[] ChangedUrls => _current
        .Where(pair => !_previous.TryGetValue(pair.Key, out var oldLastmod) ||
                       !string.Equals(pair.Value, oldLastmod, StringComparison.Ordinal))
        .Select(static pair => pair.Key)
        .ToArray();

    /// <summary>Atomically records a fully successful non-dry-run submission.</summary>
    internal void Save()
    {
        var directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("indexnow: sitemapStatePath has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, "." + Path.GetFileName(_path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            var contents = JsonSerializer.Serialize(new StoredState
            {
                Version = SchemaVersion,
                Urls = _current
            });
            if (Encoding.UTF8.GetByteCount(contents) > MaxCheckpointBytes)
                throw new InvalidOperationException("indexnow: sitemap checkpoint exceeds 32 MiB.");
            File.WriteAllText(temporaryPath, contents);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static Dictionary<string, string?> ReadSitemap(string path, Uri site)
    {
        if (new FileInfo(path).Length > MaxSitemapBytes)
            throw new InvalidOperationException("indexnow: sitemap exceeds the 50 MiB protocol limit.");

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxSitemapBytes
        };
        using var reader = XmlReader.Create(path, settings);
        var document = XDocument.Load(reader, LoadOptions.None);
        var root = document.Root;
        if (root is null || !string.Equals(root.Name.LocalName, "urlset", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("indexnow: sitemapStatePath requires a URL sitemap, not a sitemap index.");

        // The submission path deduplicates URLs without regard to case. Reject those aliases here
        // rather than marking an unsent sitemap entry as successfully submitted.
        var seenSubmissionUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var urls = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var entry in root.Elements().Where(static element => element.Name.LocalName == "url"))
        {
            var value = entry.Elements().FirstOrDefault(static element => element.Name.LocalName == "loc")?.Value.Trim();
            if (!Uri.TryCreate(value, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(url.Authority, site.Authority, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Fragment))
                throw new InvalidOperationException("indexnow: stateful sitemap contains a URL outside the configured HTTPS host.");

            var lastmod = entry.Elements().FirstOrDefault(static element => element.Name.LocalName == "lastmod")?.Value.Trim();
            if (!seenSubmissionUrls.Add(url.AbsoluteUri) || !urls.TryAdd(url.AbsoluteUri, lastmod))
                throw new InvalidOperationException("indexnow: stateful sitemap contains a duplicate URL.");
        }

        return urls;
    }

    private static Dictionary<string, string?> ReadState(string path)
    {
        if (!File.Exists(path))
            return new Dictionary<string, string?>(StringComparer.Ordinal);
        if (new FileInfo(path).Length > MaxCheckpointBytes)
            throw new InvalidOperationException("indexnow: sitemap checkpoint exceeds 32 MiB.");

        var state = JsonSerializer.Deserialize<StoredState>(File.ReadAllText(path))
            ?? throw new InvalidOperationException("indexnow: sitemap checkpoint is empty.");
        if (state.Version != SchemaVersion || state.Urls is null)
            throw new InvalidOperationException("indexnow: sitemap checkpoint has an unsupported schema.");
        return new Dictionary<string, string?>(state.Urls, StringComparer.Ordinal);
    }
}
