namespace PowerForge.Web;

/// <summary>Front matter parsed from a Markdown file.</summary>
public sealed class FrontMatter
{
    /// <summary>Page title.</summary>
    public string? Title { get; set; }
    /// <summary>Page description.</summary>
    public string? Description { get; set; }
    /// <summary>Publication date.</summary>
    public DateTime? Date { get; set; }
    /// <summary>Tag list.</summary>
    public string[] Tags { get; set; } = Array.Empty<string>();
    /// <summary>URL slug override.</summary>
    public string? Slug { get; set; }
    /// <summary>Ordering hint within a collection.</summary>
    public int? Order { get; set; }
    /// <summary>Marks the page as draft.</summary>
    public bool Draft { get; set; }
    /// <summary>Collection override.</summary>
    public string? Collection { get; set; }
    /// <summary>Legacy aliases for redirects.</summary>
    public string[] Aliases { get; set; } = Array.Empty<string>();
    /// <summary>Canonical URL.</summary>
    public string? Canonical { get; set; }
    /// <summary>Edit link path.</summary>
    public string? EditPath { get; set; }
    /// <summary>Resolved edit link URL.</summary>
    public string? EditUrl { get; set; }
    /// <summary>Layout override.</summary>
    public string? Layout { get; set; }
    /// <summary>Template override.</summary>
    public string? Template { get; set; }
    /// <summary>Additional front matter values.</summary>
    public Dictionary<string, object?> Meta { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Resolved content item ready for rendering.</summary>
public sealed class ContentItem
{
    /// <summary>Source file path.</summary>
    public string SourcePath { get; set; } = string.Empty;
    /// <summary>Collection name.</summary>
    public string Collection { get; set; } = string.Empty;
    /// <summary>Output route.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Page title.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Page description.</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>Publication date.</summary>
    public DateTime? Date { get; set; }
    /// <summary>Ordering hint within a collection.</summary>
    public int? Order { get; set; }
    /// <summary>URL slug.</summary>
    public string Slug { get; set; } = string.Empty;
    /// <summary>Tag list.</summary>
    public string[] Tags { get; set; } = Array.Empty<string>();
    /// <summary>Legacy aliases for redirects.</summary>
    public string[] Aliases { get; set; } = Array.Empty<string>();
    /// <summary>Marks the page as draft.</summary>
    public bool Draft { get; set; }
    /// <summary>Canonical URL.</summary>
    public string? Canonical { get; set; }
    /// <summary>Edit link path.</summary>
    public string? EditPath { get; set; }
    /// <summary>Resolved edit link URL.</summary>
    public string? EditUrl { get; set; }
    /// <summary>Layout override.</summary>
    public string? Layout { get; set; }
    /// <summary>Template override.</summary>
    public string? Template { get; set; }
    /// <summary>Page kind (page, section, taxonomy, term).</summary>
    public PageKind Kind { get; set; } = PageKind.Page;
    /// <summary>Rendered HTML content.</summary>
    public string HtmlContent { get; set; } = string.Empty;
    /// <summary>Rendered table of contents HTML.</summary>
    public string TocHtml { get; set; } = string.Empty;
    /// <summary>Optional resource list for page bundles.</summary>
    public PageResource[] Resources { get; set; } = Array.Empty<PageResource>();
    /// <summary>Associated project slug.</summary>
    public string? ProjectSlug { get; set; }
    /// <summary>Additional meta values.</summary>
    public Dictionary<string, object?> Meta { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Output formats for the page.</summary>
    public string[] Outputs { get; set; } = Array.Empty<string>();
}
