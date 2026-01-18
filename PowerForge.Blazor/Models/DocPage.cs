namespace PowerForge.Blazor;

/// <summary>
/// Represents a single documentation page.
/// </summary>
public class DocPage
{
    /// <summary>
    /// Unique identifier/slug for this page (used in URLs).
    /// </summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>
    /// Page title.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Optional short description for search/preview.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Raw content before rendering.
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Content type (e.g., "markdown", "html", "xml").
    /// </summary>
    public string ContentType { get; set; } = "markdown";

    /// <summary>
    /// Rendered HTML content (populated after rendering).
    /// </summary>
    public string? RenderedHtml { get; set; }

    /// <summary>
    /// Source file path (if loaded from file system).
    /// </summary>
    public string? SourcePath { get; set; }

    /// <summary>
    /// Last modified date.
    /// </summary>
    public DateTime? LastModified { get; set; }

    /// <summary>
    /// Sort order within the navigation.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Parent page slug for hierarchical navigation.
    /// </summary>
    public string? ParentSlug { get; set; }

    /// <summary>
    /// Tags/categories for this page.
    /// </summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>
    /// Front matter metadata (from markdown files).
    /// </summary>
    public Dictionary<string, object> Metadata { get; set; } = new();

    /// <summary>
    /// Table of contents entries extracted from headings.
    /// </summary>
    public List<TocEntry> TableOfContents { get; set; } = new();
}

/// <summary>
/// Table of contents entry.
/// </summary>
public class TocEntry
{
    /// <summary>
    /// Heading level (1-6).
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// Heading text.
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Anchor ID for linking.
    /// </summary>
    public string Anchor { get; set; } = string.Empty;

    /// <summary>
    /// Child entries for nested TOC.
    /// </summary>
    public List<TocEntry> Children { get; set; } = new();
}
