using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace PowerForge.Web.Protocols.Sitemaps;

/// <summary>Creates sitemap XML and partitions URL sets by protocol count and serialized UTF-8 size.</summary>
public static class SitemapWriter
{
    /// <summary>Sitemaps.org XML namespace.</summary>
    public const string Namespace = "http://www.sitemaps.org/schemas/sitemap/0.9";
    /// <summary>Maximum entries in an individual sitemap or index.</summary>
    public const int MaximumEntries = 50_000;
    /// <summary>Maximum uncompressed size of an individual sitemap.</summary>
    public const long MaximumBytes = 50L * 1024 * 1024;

    /// <summary>Creates a document from explicit publisher entries and preserved extension elements.</summary>
    public static XDocument Create(SitemapDocument sitemap, string? stylesheet = null)
    {
        if (sitemap is null) throw new ArgumentNullException(nameof(sitemap));
        if (sitemap.Entries.Count == 0) throw new ArgumentException("A sitemap requires at least one entry.", nameof(sitemap));
        XNamespace ns = Namespace;
        if (sitemap.Entries.Count > MaximumEntries) throw new ArgumentException("Partition the sitemap before writing more than 50,000 entries.", nameof(sitemap));
        var root = new XElement(ns + (sitemap.IsIndex ? "sitemapindex" : "urlset"), sitemap.Entries.Select(entry =>
        {
            if (entry.IsSitemap != sitemap.IsIndex) throw new ArgumentException("Sitemap entries must match the document kind.", nameof(sitemap));
            ValidateLocation(entry.Url, nameof(sitemap));
            string? date = entry.LastModifiedText ?? entry.LastModified?.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
            ValidateLastModified(date, nameof(sitemap));
            return new XElement(ns + (sitemap.IsIndex ? "sitemap" : "url"), new XElement(ns + "loc", entry.Url),
                date is null ? null : new XElement(ns + "lastmod", date), entry.Extensions.Select(e => new XElement(e)));
        }));
        var document = CreateDocument(root, stylesheet);
        if (GetUtf8Size(document) > MaximumBytes) throw new ArgumentException("The sitemap exceeds 50 MiB.", nameof(sitemap));
        return document;
    }

    /// <summary>Adds an XML declaration and optional CSS stylesheet to a sitemap root.</summary>
    public static XDocument CreateDocument(XElement root, string? stylesheet = null)
    {
        if (root is null) throw new ArgumentNullException(nameof(root));
        var document = new XDocument(new XDeclaration("1.0", "UTF-8", null));
        if (!string.IsNullOrWhiteSpace(stylesheet))
        {
            var attribute = new XAttribute("href", stylesheet).ToString();
            document.Add(new XProcessingInstruction("xml-stylesheet", "type=\"text/css\" " + attribute));
        }
        document.Add(root);
        return document;
    }

    /// <summary>Partitions explicit URL elements without choosing output paths or deleting files.</summary>
    public static IReadOnlyList<XDocument> Partition(IEnumerable<XElement> entries, IEnumerable<XAttribute>? attributes = null,
        string? stylesheet = null, int entryLimit = MaximumEntries)
    {
        if (entries is null) throw new ArgumentNullException(nameof(entries));
        if (entryLimit < 1 || entryLimit > MaximumEntries) throw new ArgumentOutOfRangeException(nameof(entryLimit));
        var attrs = (attributes ?? Array.Empty<XAttribute>()).Select(a => new XAttribute(a)).ToArray();
        // Leave room for a UTF-8 BOM and a short publisher ownership comment added when saving files.
        const long partitionByteLimit = MaximumBytes - 256;
        XDocument Make() => CreateDocument(new XElement(XName.Get("urlset", Namespace), attrs.Select(a => new XAttribute(a))), stylesheet);
        var result = new List<XDocument>();
        void AddVerified(XDocument document)
        {
            if (GetUtf8Size(document) <= partitionByteLimit) { result.Add(document); return; }
            var children = document.Root!.Elements().ToArray();
            if (children.Length < 2) throw new InvalidDataException("A sitemap entry or envelope exceeds the uncompressed sitemap size limit.");
            // Custom namespace prefixes can change final byte counts. Verify the actual document before returning it.
            int middle = children.Length / 2;
            var left = Make(); left.Root!.Add(children.Take(middle).Select(e => new XElement(e)));
            var right = Make(); right.Root!.Add(children.Skip(middle).Select(e => new XElement(e)));
            AddVerified(left); AddVerified(right);
        }
        var current = Make();
        long bytes = GetUtf8Size(current);
        int count = 0;
        foreach (var entry in entries)
        {
            if (entry.Name != XName.Get("url", Namespace)) throw new ArgumentException("URL-set partitions require sitemap url elements.", nameof(entries));
            ValidateLocation(entry.Element(XName.Get("loc", Namespace))?.Value, nameof(entries));
            ValidateLastModified(entry.Element(XName.Get("lastmod", Namespace))?.Value, nameof(entries));
            // Standalone element serialization repeats namespace declarations, giving a conservative bound.
            long addition = Encoding.UTF8.GetByteCount(entry.ToString(SaveOptions.DisableFormatting)) + 32;
            if (count > 0 && (count == entryLimit || bytes + addition > partitionByteLimit))
            {
                AddVerified(current); current = Make(); bytes = GetUtf8Size(current); count = 0;
            }
            if (bytes + addition > partitionByteLimit) throw new InvalidDataException("A sitemap entry exceeds the uncompressed sitemap size limit.");
            current.Root!.Add(new XElement(entry)); bytes += addition; count++;
        }
        if (count == 0) throw new ArgumentException("A sitemap requires at least one entry.", nameof(entries));
        AddVerified(current);
        return result;
    }

    private static void ValidateLocation(string? location, string parameterName)
    {
        if (location == null || location.Length > 2048 || !Uri.TryCreate(location, UriKind.Absolute, out var uri)
            || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ArgumentException("A sitemap location must be an absolute HTTP(S) URL of at most 2,048 characters.", parameterName);
    }

    private static void ValidateLastModified(string? value, string parameterName)
    {
        if (value == null) return;
        var names = new NameTable();
        foreach (var code in new[] { XmlTypeCode.Date, XmlTypeCode.DateTime })
        {
            try
            {
                XmlSchemaType.GetBuiltInSimpleType(code).Datatype!.ParseValue(value, names, null);
                return;
            }
            catch (XmlSchemaException) { }
        }
        throw new ArgumentException("Sitemap lastmod must be an XML date or dateTime.", parameterName);
    }

    /// <summary>Measures the compact UTF-8 XML payload, including its declaration.</summary>
    public static long GetUtf8Size(XDocument document)
    {
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, CloseOutput = false }))
            document.Save(writer);
        return stream.Length;
    }
}
