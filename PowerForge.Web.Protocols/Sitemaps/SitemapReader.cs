using System.Globalization;
using System.Xml.Linq;

namespace PowerForge.Web.Protocols.Sitemaps;

/// <summary>Reads bounded sitemap XML without resolving entities or fetching child sitemaps.</summary>
public static class SitemapReader
{
    /// <summary>Reads URL-set or index entries. Strict mode rejects unsupported roots and missing locations.</summary>
    public static SitemapDocument Read(string xml, Uri? baseUri = null, bool strict = false)
    {
        var root = ProtocolXml.Parse(xml).Root;
        if (root is null || (root.Name.LocalName != "urlset" && root.Name.LocalName != "sitemapindex")
            || (strict ? root.Name.NamespaceName != SitemapWriter.Namespace
                : root.Name.NamespaceName.Length != 0 && root.Name.NamespaceName != SitemapWriter.Namespace))
        {
            if (strict) throw new InvalidOperationException("Unsupported sitemap XML root.");
            return new SitemapDocument();
        }
        bool index = root.Name.LocalName == "sitemapindex";
        XNamespace ns = root.Name.Namespace;
        return new SitemapDocument
        {
            IsIndex = index,
            Entries = root.Elements(ns + (index ? "sitemap" : "url")).Select(element =>
            {
                string location = (element.Element(ns + "loc")?.Value ?? string.Empty).Trim();
                if (strict && location.Length == 0) throw new InvalidOperationException("Sitemap entry has no location.");
                if (location.Length > 0 && baseUri != null && Uri.TryCreate(baseUri, location.Replace('\\', '/'), out var resolved))
                    location = resolved.AbsoluteUri;
                var date = element.Element(ns + "lastmod")?.Value.Trim();
                return new SitemapEntry
                {
                    Url = location, IsSitemap = index, LastModifiedText = date,
                    LastModified = DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal, out var parsed) ? parsed : null,
                    Extensions = element.Elements().Where(child => child.Name != ns + "loc" && child.Name != ns + "lastmod")
                        .Select(child => new XElement(child)).ToArray()
                };
            }).Where(entry => !string.IsNullOrWhiteSpace(entry.Url)).ToArray()
        };
    }
}
