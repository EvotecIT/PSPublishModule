using System.Globalization;
using System.Xml.Linq;

namespace PowerForge.Web.Protocols.Feeds;

/// <summary>Serializes explicit feed metadata and entries; selection, fallbacks and scheduling belong to callers.</summary>
public static class SyndicationWriter
{
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";

    /// <summary>Writes RSS 2.0, including author, category and optional content-module metadata.</summary>
    public static string WriteRss(SyndicationFeed feed)
    {
        if (feed is null) throw new ArgumentNullException(nameof(feed));
        var channel = new XElement("channel", new XElement("title", feed.Title), new XElement("link", feed.Url),
            new XElement("description", feed.Description ?? string.Empty));
        if (!string.IsNullOrWhiteSpace(feed.SourceFeedUrl)) channel.Add(new XElement(Atom + "link",
            new XAttribute("href", feed.SourceFeedUrl), new XAttribute("rel", "self"), new XAttribute("type", "application/rss+xml")));
        if (feed.Updated.HasValue) channel.Add(new XElement("lastBuildDate", feed.Updated.Value.UtcDateTime.ToString("r", CultureInfo.InvariantCulture)));
        channel.Add(feed.Authors.Select(author => new XElement(Dc + "creator", author.Name)));
        foreach (var item in feed.Items)
        {
            var element = new XElement("item", new XElement("title", item.Title), new XElement("link", item.Url),
                new XElement("description", item.Summary ?? string.Empty));
            if (item.Published.HasValue) element.Add(new XElement("pubDate", item.Published.Value.UtcDateTime.ToString("r", CultureInfo.InvariantCulture)));
            if (item.Updated.HasValue) element.Add(new XElement(XName.Get("modified", "http://purl.org/dc/terms/"), item.Updated.Value.ToString("O", CultureInfo.InvariantCulture)));
            if (item.Id != null) element.Add(new XElement("guid", new XAttribute("isPermaLink", item.IdIsPermaLink ? "true" : "false"), item.Id));
            element.Add(item.Authors.Select(author => new XElement(Dc + "creator", author.Name)));
            element.Add(item.Categories.Select(category => new XElement("category", category)));
            if (!string.IsNullOrWhiteSpace(item.Content)) element.Add(new XElement(Content + "encoded", item.Content));
            element.Add(item.Extensions.Select(e => new XElement(e)));
            channel.Add(element);
        }
        channel.Add(feed.Extensions.Select(e => new XElement(e)));
        var root = new XElement("rss", new XAttribute("version", "2.0"), new XAttribute(XNamespace.Xmlns + "atom", Atom),
            new XAttribute(XNamespace.Xmlns + "dc", Dc), new XAttribute(XNamespace.Xmlns + "content", Content), channel);
        return new XDocument(root).ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Writes Atom 1.0. The caller supplies stable identifiers and update dates.</summary>
    public static string WriteAtom(SyndicationFeed feed)
    {
        if (feed is null) throw new ArgumentNullException(nameof(feed));
        if (string.IsNullOrWhiteSpace(feed.Id) || !feed.Updated.HasValue)
            throw new ArgumentException("Atom requires a feed identifier and update date.", nameof(feed));
        if (feed.Authors.Count == 0 && (feed.Items.Count == 0 || feed.Items.Any(item => item.Authors.Count == 0)))
            throw new ArgumentException("Atom requires a feed author or an author for every entry.", nameof(feed));
        var root = new XElement(Atom + "feed", new XElement(Atom + "title", feed.Title), new XElement(Atom + "id", feed.Id),
            new XElement(Atom + "updated", feed.Updated.Value.ToString("O", CultureInfo.InvariantCulture)),
            new XElement(Atom + "subtitle", feed.Description ?? string.Empty),
            new XElement(Atom + "link", new XAttribute("href", feed.Url)));
        if (!string.IsNullOrWhiteSpace(feed.SourceFeedUrl)) root.Add(new XElement(Atom + "link", new XAttribute("href", feed.SourceFeedUrl),
            new XAttribute("rel", "self"), new XAttribute("type", "application/atom+xml")));
        root.Add(feed.Authors.Select(AtomAuthor));
        foreach (var item in feed.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !item.Updated.HasValue)
                throw new ArgumentException("Atom requires entry identifiers and update dates.", nameof(feed));
            var entry = new XElement(Atom + "entry", new XElement(Atom + "title", item.Title), new XElement(Atom + "id", item.Id),
                new XElement(Atom + "link", new XAttribute("href", item.Url)),
                new XElement(Atom + "updated", item.Updated.Value.ToString("O", CultureInfo.InvariantCulture)),
                new XElement(Atom + "summary", item.Summary ?? string.Empty));
            if (item.Published.HasValue) entry.Add(new XElement(Atom + "published", item.Published.Value.ToString("O", CultureInfo.InvariantCulture)));
            entry.Add(item.Authors.Select(AtomAuthor));
            if (!string.IsNullOrWhiteSpace(item.Content))
            {
                var content = new XElement(Atom + "content", new XAttribute("type", item.ContentType ?? "html"));
                if (string.Equals(item.ContentType, "xhtml", StringComparison.OrdinalIgnoreCase))
                    content.Add(ProtocolXml.Parse("<div xmlns=\"http://www.w3.org/1999/xhtml\">" + item.Content + "</div>").Root);
                else content.Value = item.Content!;
                entry.Add(content);
            }
            entry.Add(item.Categories.Select(category => new XElement(Atom + "category", new XAttribute("term", category))));
            entry.Add(item.Extensions.Select(e => new XElement(e)));
            root.Add(entry);
        }
        root.Add(feed.Extensions.Select(e => new XElement(e)));
        return new XDocument(root).ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Writes JSON Feed 1.1 for the existing website feed consumer.</summary>
    public static string WriteJsonFeed(SyndicationFeed feed)
    {
        if (feed is null) throw new ArgumentNullException(nameof(feed));
        var items = feed.Items.Select(item =>
        {
            if (string.IsNullOrWhiteSpace(item.Id)) throw new ArgumentException("JSON Feed requires a stable entry identifier.", nameof(feed));
            var model = new Dictionary<string, object?> { ["id"] = item.Id };
            AddOptional(model, "url", item.Url);
            AddOptional(model, "title", item.Title);
            AddOptional(model, "summary", item.Summary);
            // JSON Feed requires content_text or content_html even when the site omits full article bodies.
            model[string.Equals(item.ContentType, "text", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(item.Content) ? "content_text" : "content_html"] = item.Content ?? item.Summary ?? string.Empty;
            if (item.Published.HasValue) model["date_published"] = item.Published.Value.ToString("O", CultureInfo.InvariantCulture);
            if (item.Updated.HasValue) model["date_modified"] = item.Updated.Value.ToString("O", CultureInfo.InvariantCulture);
            if (item.Categories.Count > 0) model["tags"] = item.Categories;
            if (item.Authors.Count > 0) model["authors"] = item.Authors.Select(JsonAuthor).ToArray();
            return model;
        }).ToArray();
        var document = new Dictionary<string, object?>
        {
            ["version"] = "https://jsonfeed.org/version/1.1", ["title"] = feed.Title,
            ["items"] = items
        };
        AddOptional(document, "home_page_url", feed.Url);
        AddOptional(document, "feed_url", feed.SourceFeedUrl);
        AddOptional(document, "description", feed.Description);
        if (feed.Authors.Count > 0) document["authors"] = feed.Authors.Select(JsonAuthor).ToArray();
        return ProtocolJson.Serialize(document, indented: true);
    }

    private static void AddOptional(Dictionary<string, object?> model, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) model[name] = value;
    }

    private static Dictionary<string, object?> JsonAuthor(FeedAuthor author)
    {
        var value = new Dictionary<string, object?> { ["name"] = author.Name };
        if (!string.IsNullOrWhiteSpace(author.Url)) value["url"] = author.Url;
        return value;
    }

    private static XElement AtomAuthor(FeedAuthor author) => new(Atom + "author", new XElement(Atom + "name", author.Name),
        string.IsNullOrWhiteSpace(author.Url) ? null : new XElement(Atom + "uri", author.Url));
}
