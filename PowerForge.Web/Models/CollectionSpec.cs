namespace PowerForge.Web;

/// <summary>Defines a content collection (pages, docs, blog).</summary>
public sealed class CollectionSpec
{
    /// <summary>Collection name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Optional preset that applies sensible defaults (for example: blog, news, changelog, docs).</summary>
    public string? Preset { get; set; }
    /// <summary>Input folder path.</summary>
    public string Input { get; set; } = string.Empty;
    /// <summary>Output route prefix.</summary>
    public string Output { get; set; } = string.Empty;
    /// <summary>Default layout for items in the collection.</summary>
    public string? DefaultLayout { get; set; }
    /// <summary>Layout for section/list pages.</summary>
    public string? ListLayout { get; set; }
    /// <summary>Optional TOC file path for navigation overrides.</summary>
    public string? TocFile { get; set; }
    /// <summary>Whether to use TOC files for navigation (default: true).</summary>
    public bool UseToc { get; set; } = true;
    /// <summary>Include glob patterns.</summary>
    public string[] Include { get; set; } = Array.Empty<string>();
    /// <summary>Exclude glob patterns.</summary>
    public string[] Exclude { get; set; } = Array.Empty<string>();
    /// <summary>Field used for sorting.</summary>
    public string? SortBy { get; set; }
    /// <summary>Sort order.</summary>
    public SortOrder? SortOrder { get; set; }
    /// <summary>Optional output formats for the collection.</summary>
    public string[] Outputs { get; set; } = Array.Empty<string>();
    /// <summary>Collection-level SEO configuration (overrides site-level templates).</summary>
    public SeoSpec? Seo { get; set; }
    /// <summary>Optional page size for generated section pagination.</summary>
    public int? PageSize { get; set; }
    /// <summary>Auto-generate the collection landing page (section index) when no non-draft index exists.</summary>
    public bool AutoGenerateSectionIndex { get; set; }
    /// <summary>Optional title for auto-generated section index pages.</summary>
    public string? AutoSectionTitle { get; set; }
    /// <summary>Optional description for auto-generated section index pages.</summary>
    public string? AutoSectionDescription { get; set; }
    /// <summary>Optional editorial card defaults used by helper-based list layouts.</summary>
    public EditorialCardsSpec? EditorialCards { get; set; }
}

/// <summary>Collection-level editorial card defaults.</summary>
public sealed class EditorialCardsSpec
{
    /// <summary>Default fallback card image for collection items without an image.</summary>
    public string? Image { get; set; }
    /// <summary>Default object-fit value for card media (for example: cover, contain).</summary>
    public string? ImageFit { get; set; }
    /// <summary>Default object-position value for card media (for example: center, top center).</summary>
    public string? ImagePosition { get; set; }
    /// <summary>Default aspect ratio for card media (for example: 16/9, 4:3).</summary>
    public string? ImageAspect { get; set; }
    /// <summary>Default editorial_cards variant (default, compact, hero, featured).</summary>
    public string? Variant { get; set; }
    /// <summary>Optional CSS classes appended to the editorial grid wrapper.</summary>
    public string? GridClass { get; set; }
    /// <summary>Optional CSS classes appended to each rendered editorial card.</summary>
    public string? CardClass { get; set; }
}
