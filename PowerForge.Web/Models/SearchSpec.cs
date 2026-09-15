namespace PowerForge.Web;

/// <summary>Sources included alongside content pages in the site-wide search index.</summary>
public sealed class SearchSpec
{
    /// <summary>
    /// Output-relative API documentation roots whose generated search.json catalogs are included.
    /// Generate API documentation before the final site build so reference entries are current.
    /// </summary>
    public string[] ApiRoots { get; set; } = Array.Empty<string>();
}
