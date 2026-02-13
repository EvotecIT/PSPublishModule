namespace PowerForge.Web;

/// <summary>Search index entry emitted for client-side search.</summary>
public sealed class SearchIndexEntry
{
    /// <summary>Display title.</summary>
    public string Title { get; set; } = string.Empty;
    /// <summary>Destination URL.</summary>
    public string Url { get; set; } = string.Empty;
    /// <summary>Optional description.</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>Short content snippet.</summary>
    public string Snippet { get; set; } = string.Empty;
    /// <summary>Collection identifier.</summary>
    public string Collection { get; set; } = string.Empty;
    /// <summary>Resolved page kind (for example page, section, taxonomy, term, home).</summary>
    public string Kind { get; set; } = string.Empty;
    /// <summary>Relative ranking weight used by search clients.</summary>
    public double Weight { get; set; } = 1.0;
    /// <summary>Precomputed text blob to simplify lightweight client-side search ranking.</summary>
    public string SearchText { get; set; } = string.Empty;
    /// <summary>Tag list used for filtering.</summary>
    public string[] Tags { get; set; } = Array.Empty<string>();
    /// <summary>Optional project slug.</summary>
    public string? Project { get; set; }
    /// <summary>Resolved page language (for example en/pl).</summary>
    public string? Language { get; set; }
    /// <summary>Optional translation key shared across localized variants.</summary>
    public string? TranslationKey { get; set; }
    /// <summary>Additional metadata for indexing.</summary>
    public Dictionary<string, object?>? Meta { get; set; }
}
