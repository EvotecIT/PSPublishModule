namespace PowerForge.Web;

/// <summary>Defines a taxonomy such as tags or categories.</summary>
public sealed class TaxonomySpec
{
    /// <summary>Taxonomy name (tags, categories).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Base path where taxonomy pages are generated.</summary>
    public string BasePath { get; set; } = string.Empty;

    /// <summary>Layout for the taxonomy list page.</summary>
    public string? ListLayout { get; set; }

    /// <summary>Layout for taxonomy term pages.</summary>
    public string? TermLayout { get; set; }

    /// <summary>Optional feed title override for the taxonomy list page.</summary>
    public string? FeedTitle { get; set; }

    /// <summary>Optional feed description override for the taxonomy list page.</summary>
    public string? FeedDescription { get; set; }

    /// <summary>Optional feed title template for taxonomy term feeds. Supports {term}, {taxonomy}, and {site} tokens.</summary>
    public string? TermFeedTitleTemplate { get; set; }

    /// <summary>Optional feed description template for taxonomy term feeds. Supports {term}, {taxonomy}, and {site} tokens.</summary>
    public string? TermFeedDescriptionTemplate { get; set; }

    /// <summary>Optional output formats for taxonomy list/term pages.</summary>
    public string[] Outputs { get; set; } = Array.Empty<string>();
    /// <summary>Optional page size for taxonomy and term listing pages.</summary>
    public int? PageSize { get; set; }

    /// <summary>Optional sort field for terms.</summary>
    public string? SortBy { get; set; }

    /// <summary>Sort order for terms.</summary>
    public SortOrder? SortOrder { get; set; }
}
