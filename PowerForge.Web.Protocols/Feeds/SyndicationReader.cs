using System.Globalization;
using System.Xml.Linq;
namespace PowerForge.Web.Protocols.Feeds;
/// <summary>Reads RSS and Atom documents without fetching resources or interpreting source policy.</summary>
public static class SyndicationReader {
    /// <summary>Reads metadata and entries in publisher order (RSS 1.0 sequence when present). Markup remains untrusted.</summary>
    public static SyndicationFeed ParseSyndicationFeed(string xml, Uri? baseUri = null, string? sourceFeedUrl = null) {
        if (xml == null) {
            throw new ArgumentNullException(nameof(xml));
        }

        XDocument document = ProtocolXml.Parse(xml);
        XElement? root = document.Root;
        if (root == null) {
            return new SyndicationFeed { SourceFeedUrl = sourceFeedUrl };
        }

        if (root.Name == XName.Get("feed", "http://www.w3.org/2005/Atom")
            || root.Name == XName.Get("feed", "http://purl.org/atom/ns#")) {
            string? effectiveSource = ResolveSourceFeedUrl(root, root.Name.Namespace, baseUri, sourceFeedUrl, out _);
            return new SyndicationFeed {
                Title = ElementValue(root, "title"),
                Id = EmptyToNull(ElementValue(root, "id")),
                Description = EmptyToNull(ElementValue(root, root.Name.NamespaceName == "http://purl.org/atom/ns#" ? "tagline" : "subtitle")),
                Authors = ParseAuthors(root, baseUri, atom: true),
                Extensions = ReadExtensions(root, atom: true),
                Url = GetAtomLink(root, baseUri),
                // Atom 0.3 feeds use modified.
                Updated = TryParseDate(FirstNonEmpty(ElementValue(root, "updated"), ElementValue(root, "modified"))),
                SourceFeedUrl = effectiveSource,
                Items = ParseAtomItems(root, baseUri, effectiveSource)
            };
        }

        XElement? channel = root.Name == XName.Get("RDF", "http://www.w3.org/1999/02/22-rdf-syntax-ns#")
            ? root.Element(XName.Get("channel", "http://purl.org/rss/1.0/"))
            : root.Name == XName.Get("rss") ? root.Element("channel") : null;
        string? rssSource = ResolveSourceFeedUrl(channel, "http://www.w3.org/2005/Atom", baseUri, sourceFeedUrl, out var selfLink);
        return new SyndicationFeed {
            Title = channel == null ? string.Empty : ElementValue(channel, channel.Name.Namespace, "title"),
            Description = channel == null ? null : EmptyToNull(ElementValue(channel, "description")),
            Authors = channel == null ? Array.Empty<FeedAuthor>() : ParseAuthors(channel, baseUri, atom: false),
            Extensions = channel == null ? Array.Empty<XElement>() : ReadExtensions(channel, atom: false, consumedLink: selfLink),
            // The channel's own link, not an atom:link rel="self" that many RSS 2.0 feeds also carry.
            Url = channel == null ? string.Empty : ResolveElementUrl(FirstChild(channel, "link"), baseUri),
            Updated = channel == null ? null : TryParseDate(FirstNonEmpty(ElementValue(channel, "lastBuildDate"), ElementValue(channel, "pubDate"),
                ElementValue(channel, DublinCore, "date"))),
            SourceFeedUrl = rssSource,
            Items = ParseRssItems(channel == null ? Enumerable.Empty<XElement>()
                : root.Name.LocalName == "RDF" ? OrderedRdfItems(root, channel, baseUri)
                : channel.Elements("item"), baseUri, rssSource)
        };
    }

    private static IEnumerable<XElement> OrderedRdfItems(XElement root, XElement channel, Uri? baseUri) {
        XNamespace rss = "http://purl.org/rss/1.0/";
        XNamespace rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        var items = root.Elements(rss + "item").ToArray();
        var sequence = channel.Element(rss + "items")?.Element(rdf + "Seq");
        if (sequence == null) return items;
        var lookup = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var item in items) {
            var about = item.Attribute(rdf + "about");
            string key = about == null ? string.Empty : ResolveReference(about.Value, item, baseUri);
            if (key.Length > 0 && !lookup.ContainsKey(key)) lookup.Add(key, item);
        }
        var ordered = new List<XElement>();
        foreach (var reference in sequence.Elements(rdf + "li")) {
            var resource = reference.Attribute(rdf + "resource");
            if (resource == null) continue;
            string key = ResolveReference(resource.Value, reference, baseUri);
            if (lookup.TryGetValue(key, out var item)) ordered.Add(item);
        }
        return ordered;
    }

    private static string? ResolveSourceFeedUrl(XElement? container, XNamespace ns, Uri? baseUri,
        string? explicitSource, out XElement? selfLink) {
        selfLink = container?.Elements(ns + "link").FirstOrDefault(link => link.Attribute("rel")?.Value == "self"
            && !string.IsNullOrWhiteSpace(link.Attribute("href")?.Value));
        return explicitSource ?? (selfLink == null ? null : EmptyToNull(ResolveUrl(
            selfLink.Attribute("href")!.Value, EffectiveBase(selfLink, baseUri))));
    }

    private static IReadOnlyList<SyndicationItem> ParseRssItems(IEnumerable<XElement> elements, Uri? baseUri, string? sourceFeedUrl) {
        List<SyndicationItem> items = new();
        foreach (XElement item in elements) {
            string title = ElementValue(item, "title");
            string link = ResolveElementUrl(FirstChild(item, "link"), baseUri);
            if (string.IsNullOrWhiteSpace(link) && !string.Equals(FirstChild(item, "guid")?.Attribute("isPermaLink")?.Value?.Trim(), "false", StringComparison.OrdinalIgnoreCase)) {
                link = ResolveElementUrl(FirstChild(item, "guid"), baseUri);
            }

            XElement? guid = FirstChild(item, "guid");
            string? id = EmptyToNull(guid?.Value);
            items.Add(new SyndicationItem {
                Title = title,
                Url = link,
                Summary = FirstNonEmpty(ElementValue(item, "description"), ElementValue(item, "summary")),
                // Many feeds (WordPress, RSS 1.0/RDF, Jamf) date items only with Dublin Core: dc:date, dcterms:created, dcterms:modified.
                Published = TryParseDate(FirstNonEmpty(ElementValue(item, "pubDate"), ElementValue(item, "published"),
                    ElementValue(item, DublinCore, "date"), ElementValue(item, DublinCoreTerms, "created"), ElementValue(item, DublinCoreTerms, "date"))),
                Updated = TryParseDate(FirstNonEmpty(ElementValue(item, "updated"), ElementValue(item, DublinCoreTerms, "modified"))),
                SourceFeedUrl = sourceFeedUrl,
                Id = id,
                // RSS 2.0: a guid is a permalink unless isPermaLink="false".
                IdIsPermaLink = id != null && !string.Equals(guid!.Attribute("isPermaLink")?.Value?.Trim(), "false", StringComparison.OrdinalIgnoreCase),
                Categories = ParseCategories(item),
                Authors = ParseAuthors(item, baseUri, atom: false),
                Extensions = ReadExtensions(item, atom: false),
                ContentType = "html",
                Content = EmptyToNull(ElementValue(item, ContentModule, "encoded"))
            });
        }

        return items;
    }

    private static IReadOnlyList<SyndicationItem> ParseAtomItems(XElement root, Uri? baseUri, string? sourceFeedUrl) {
        List<SyndicationItem> items = new();
        foreach (XElement entry in root.Elements().Where(element => element.Name.Namespace == root.Name.Namespace && string.Equals(element.Name.LocalName, "entry", StringComparison.OrdinalIgnoreCase))) {
            string link = GetAtomLink(entry, baseUri);
            XElement? content = FirstChild(entry, "content");
            string? contentSource = content?.Attribute("src")?.Value;
            items.Add(new SyndicationItem {
                Title = ElementValue(entry, "title"),
                Url = link,
                Summary = FirstNonEmpty(ElementValue(entry, "summary"), ElementValue(entry, "content")),
                // Atom 0.3 feeds use issued and modified.
                Published = TryParseDate(FirstNonEmpty(ElementValue(entry, "published"), ElementValue(entry, "issued"), ElementValue(entry, DublinCore, "date"))),
                Updated = TryParseDate(FirstNonEmpty(ElementValue(entry, "updated"), ElementValue(entry, "modified"))),
                SourceFeedUrl = sourceFeedUrl,
                Id = EmptyToNull(ElementValue(entry, "id")),
                IdIsPermaLink = false,
                Categories = ParseCategories(entry, atom: true),
                Authors = ParseAuthors(entry, baseUri, atom: true),
                Extensions = ReadExtensions(entry, atom: true),
                ContentType = EmptyToNull(content?.Attribute("type")?.Value) ?? (contentSource == null ? "text" : null),
                ContentSourceUrl = contentSource == null ? null : ResolveReference(contentSource, content!, baseUri),
                Content = contentSource == null ? AtomContent(content) : null
            });
        }

        return items;
    }

    private static string ResolveReference(string source, XElement content, Uri? baseUri) {
        var effectiveBase = EffectiveBase(content, baseUri);
        return effectiveBase != null && Uri.TryCreate(effectiveBase, source, out var resolved)
            ? resolved.AbsoluteUri : ResolveUrl(source, effectiveBase);
    }

    /// <summary>RSS category text or the Atom category term (label when there is no term), trimmed, in order, without duplicates.</summary>
    private static IReadOnlyList<string> ParseCategories(XElement parent, bool atom = false) {
        List<string> categories = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (XElement category in parent.Elements().Where(element => element.Name.Namespace == parent.Name.Namespace && string.Equals(element.Name.LocalName, "category", StringComparison.OrdinalIgnoreCase))) {
            string value = atom ? FirstNonEmpty(category.Attribute("term")?.Value ?? string.Empty, category.Attribute("label")?.Value ?? string.Empty)
                : category.Value.Trim();
            if (value.Length > 0 && seen.Add(value)) {
                categories.Add(value);
            }
        }

        return categories.Count == 0 ? Array.Empty<string>() : categories.ToArray();
    }

    /// <summary>The Atom content as the feed wrote it: text or HTML (unescaped once by XML), or the inner markup of an xhtml div.</summary>
    private static string? AtomContent(XElement? content) {
        if (content == null) {
            return null;
        }

        if (string.Equals(content.Attribute("type")?.Value?.Trim(), "xhtml", StringComparison.OrdinalIgnoreCase)) {
            XElement? wrapper = content.Elements().FirstOrDefault(static element => string.Equals(element.Name.LocalName, "div", StringComparison.OrdinalIgnoreCase));
            IEnumerable<XNode> nodes = wrapper?.Nodes() ?? content.Nodes();
            return EmptyToNull(string.Concat(nodes.Select(static node => node.ToString(SaveOptions.DisableFormatting))));
        }

        if (FeedContentFormat.IsXmlMediaType(content.Attribute("type")?.Value)) {
            return EmptyToNull(string.Concat(content.Nodes().Select(static node => node.ToString(SaveOptions.DisableFormatting))));
        }

        return EmptyToNull(content.Value);
    }

    private static string GetAtomLink(XElement entry, Uri? baseUri) {
        XElement? alternate = entry.Elements()
            .Where(element => element.Name.Namespace == entry.Name.Namespace && string.Equals(element.Name.LocalName, "link", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(static element => {
                XAttribute? rel = element.Attribute("rel");
                return rel == null || string.Equals(rel.Value, "alternate", StringComparison.OrdinalIgnoreCase);
            });

        return alternate == null ? string.Empty : ResolveUrl(alternate.Attribute("href")?.Value ?? string.Empty, EffectiveBase(alternate, baseUri));
    }

    private static string ResolveElementUrl(XElement? element, Uri? baseUri) =>
        element == null ? string.Empty : ResolveUrl(element.Value, EffectiveBase(element, baseUri));

    private static Uri? EffectiveBase(XElement element, Uri? baseUri) {
        foreach (var ancestor in element.AncestorsAndSelf().Reverse()) {
            var value = ancestor.Attribute(XNamespace.Xml + "base")?.Value;
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (baseUri != null && Uri.TryCreate(baseUri, value, out var resolved)) baseUri = resolved;
            else if (Uri.TryCreate(value, UriKind.Absolute, out var absolute)) baseUri = absolute;
        }
        return baseUri;
    }

    private static readonly XNamespace DublinCore = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace DublinCoreTerms = "http://purl.org/dc/terms/";
    private static readonly XNamespace ContentModule = "http://purl.org/rss/1.0/modules/content/";

    /// <summary>The trimmed value of the first child with this exact namespace and local name, or empty.</summary>
    private static string ElementValue(XElement parent, XNamespace ns, string localName) =>
        parent.Element(ns + localName)?.Value?.Trim() ?? string.Empty;

    private static string ElementValue(XElement parent, string localName) =>
        FirstChild(parent, localName)?.Value?.Trim() ?? string.Empty;

    private static XElement? FirstChild(XElement parent, string localName) =>
        parent.Elements().FirstOrDefault(child => child.Name.Namespace == parent.Name.Namespace && string.Equals(child.Name.LocalName, localName, StringComparison.OrdinalIgnoreCase));

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private static IReadOnlyList<FeedAuthor> ParseAuthors(XElement parent, Uri? baseUri, bool atom) =>
        parent.Elements().Where(element => atom ? element.Name == parent.Name.Namespace + "author"
            : element.Name == DublinCore + "creator" || element.Name == parent.Name.Namespace + "author")
        .Select(element => new FeedAuthor {
            Name = atom ? ElementValue(element, "name") : element.Value.Trim(),
            Url = atom ? EmptyToNull(ResolveElementUrl(FirstChild(element, element.Name.NamespaceName == "http://purl.org/atom/ns#" ? "url" : "uri"), baseUri)) : null
        }).ToArray();

    private static IReadOnlyList<XElement> ReadExtensions(XElement parent, bool atom, XElement? consumedLink = null) =>
        parent.Elements().Where(element => element.Name.Namespace != parent.Name.Namespace
            && element != consumedLink && !IsMappedExtension(element, parent, atom))
        .Select(element => new XElement(element)).ToArray();

    private static bool IsMappedExtension(XElement element, XElement parent, bool atom) {
        if (atom) return parent.Name.LocalName == "entry" && element.Name == DublinCore + "date";
        if (element.Name == DublinCore + "creator" || element.Name == DublinCore + "date") return true;
        return parent.Name.LocalName == "item" && (element.Name == DublinCoreTerms + "created"
            || element.Name == DublinCoreTerms + "modified" || element.Name == DublinCoreTerms + "date"
            || element.Name == ContentModule + "encoded");
    }

    private static string FirstNonEmpty(params string[] values) {
        foreach (string value in values) {
            if (!string.IsNullOrWhiteSpace(value)) {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string ResolveUrl(string value, Uri? baseUri) {
        string trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0) {
            return string.Empty;
        }

        bool isRootRelativeReference = trimmed[0] == '/' || trimmed[0] == '\\';
        if (baseUri != null && (isRootRelativeReference || !Uri.TryCreate(trimmed, UriKind.Absolute, out _))) {
            string normalized = trimmed.Replace('\\', '/');
            if (Uri.TryCreate(baseUri, normalized, out Uri? resolved)) {
                return resolved.AbsoluteUri;
            }
        }

        if (isRootRelativeReference) {
            return trimmed.Replace('\\', '/');
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? absolute)) {
            return absolute.AbsoluteUri;
        }

        return trimmed;
    }

    private static DateTimeOffset? TryParseDate(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)) {
            return parsed;
        }

        // RFC 822 zone names (PDT, UTC) and a wrong weekday defeat the general parser.
        if (WebDateParser.TryParse(value, out parsed)) {
            return parsed;
        }

        return null;
    }

}
