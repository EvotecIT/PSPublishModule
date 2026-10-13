using System;
using System.Collections.Generic;

namespace PowerForge.Web.Protocols.Feeds;

/// <summary>
/// Represents a normalized item from an RSS or Atom feed.
/// </summary>
public sealed class SyndicationItem {
    /// <summary>Atom content kind (text, html, xhtml or a media type), when known.</summary>
    public string? ContentType { get; set; }
    /// <summary>Item title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Absolute item URL when available.</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Item summary or description when available.</summary>
    public string? Summary { get; set; }

    /// <summary>Publication timestamp when available.</summary>
    public DateTimeOffset? Published { get; set; }

    /// <summary>Last update timestamp when available.</summary>
    public DateTimeOffset? Updated { get; set; }

    /// <summary>URL of the feed that produced this item when known.</summary>
    public string? SourceFeedUrl { get; set; }

    /// <summary>
    /// Item identifier as the feed wrote it: the RSS <c>guid</c> text or the Atom <c>id</c>; null when the item has none.
    /// Stable across edits, so use it rather than <see cref="Url"/> to recognize an item again, for example when every
    /// item of a status feed links to the same page.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// True when <see cref="Id"/> is also the item's permanent URL: an RSS <c>guid</c> without <c>isPermaLink="false"</c>
    /// (the RSS default). False for Atom ids, for <c>isPermaLink="false"</c>, and when there is no <see cref="Id"/>.
    /// </summary>
    public bool IdIsPermaLink { get; set; }

    /// <summary>Item categories in feed order without duplicates: RSS <c>category</c> text or the Atom <c>category</c> term.</summary>
    public IReadOnlyList<string> Categories { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Full item body when the feed carries one separately from the summary: RSS <c>content:encoded</c> or the Atom
    /// <c>content</c> element. Kept as the feed's raw string, which is often HTML; treat it as untrusted markup and
    /// sanitize or reduce it to text before display. Like <see cref="Summary"/>, it is not truncated here, so bound
    /// the feed document you pass in.
    /// </summary>
    public string? Content { get; set; }
    /// <summary>Authors supplied by the publisher, in document order.</summary>
    public IReadOnlyList<FeedAuthor> Authors { get; set; } = Array.Empty<FeedAuthor>();
    /// <summary>Unrecognized extension elements, preserved as XML for explicit interpretation.</summary>
    public IReadOnlyList<System.Xml.Linq.XElement> Extensions { get; set; } = Array.Empty<System.Xml.Linq.XElement>();
}
