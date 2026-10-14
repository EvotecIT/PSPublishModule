using System.Xml.Linq;

namespace PowerForge.Web.Protocols.Sitemaps;

/// <summary>A sitemap URL set or index. Reading it does not fetch child documents.</summary>
public sealed class SitemapDocument
{
    /// <summary>Whether the document lists child sitemaps rather than pages.</summary>
    public bool IsIndex { get; set; }
    /// <summary>Locations in publisher order.</summary>
    public IReadOnlyList<SitemapEntry> Entries { get; set; } = Array.Empty<SitemapEntry>();
}

/// <summary>A page or child sitemap advertised by a publisher.</summary>
public sealed class SitemapEntry
{
    /// <summary>Location, resolved against the supplied base URI when available.</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>Modification date, never interpreted as a publication date.</summary>
    public DateTimeOffset? LastModified { get; set; }
    /// <summary>Original date text, preserving precision and offsets for round trips.</summary>
    public string? LastModifiedText { get; set; }
    /// <summary>Whether this entry identifies a child sitemap.</summary>
    public bool IsSitemap { get; set; }
    /// <summary>Additional elements, including change frequency, priority and extension metadata.</summary>
    public IReadOnlyList<XElement> Extensions { get; set; } = Array.Empty<XElement>();
}
