namespace PowerForge.Web;

/// <summary>Structured data configuration (JSON-LD).</summary>
public sealed class StructuredDataSpec
{
    /// <summary>When true, emit structured data.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>When true, emit breadcrumb structured data.</summary>
    public bool Breadcrumbs { get; set; } = true;
    /// <summary>When true, emit WebSite JSON-LD on home pages.</summary>
    public bool Website { get; set; } = true;
    /// <summary>When true, emit Organization JSON-LD on home pages.</summary>
    public bool Organization { get; set; } = true;
    /// <summary>When true, emit Article JSON-LD for article-like pages.</summary>
    public bool Article { get; set; } = true;
}
