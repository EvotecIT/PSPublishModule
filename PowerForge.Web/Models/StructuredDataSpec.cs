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
    /// <summary>When true, emit FAQPage JSON-LD when FAQ metadata is present.</summary>
    public bool FaqPage { get; set; }
    /// <summary>When true, emit HowTo JSON-LD when how-to metadata is present.</summary>
    public bool HowTo { get; set; }
    /// <summary>When true, emit SoftwareApplication JSON-LD when application metadata is present.</summary>
    public bool SoftwareApplication { get; set; }
    /// <summary>When true, emit Product JSON-LD when product metadata is present.</summary>
    public bool Product { get; set; }
    /// <summary>When true, emit NewsArticle JSON-LD for news/article pages.</summary>
    public bool NewsArticle { get; set; }
}
