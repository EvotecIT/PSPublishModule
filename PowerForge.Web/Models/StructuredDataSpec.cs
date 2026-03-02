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
    /// <summary>Optional legal business name for Organization JSON-LD.</summary>
    public string? OrganizationLegalName { get; set; }
    /// <summary>Optional organization description for Organization JSON-LD.</summary>
    public string? OrganizationDescription { get; set; }
    /// <summary>Optional logo URL/path override for Organization JSON-LD.</summary>
    public string? OrganizationLogo { get; set; }
    /// <summary>Optional sameAs links for Organization JSON-LD.</summary>
    public string[] OrganizationSameAs { get; set; } = Array.Empty<string>();
    /// <summary>Optional support email for Organization contact point JSON-LD.</summary>
    public string? OrganizationEmail { get; set; }
    /// <summary>Optional support phone for Organization contact point JSON-LD.</summary>
    public string? OrganizationTelephone { get; set; }
    /// <summary>Optional support page URL/path for Organization contact point JSON-LD.</summary>
    public string? OrganizationContactUrl { get; set; }
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
