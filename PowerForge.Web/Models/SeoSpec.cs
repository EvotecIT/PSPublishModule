namespace PowerForge.Web;

/// <summary>SEO configuration for site and collection metadata rendering.</summary>
public sealed class SeoSpec
{
    /// <summary>When false, SEO template resolution is disabled.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Title/description template settings.</summary>
    public SeoTemplatesSpec? Templates { get; set; }
}

/// <summary>SEO title/description template strings.</summary>
public sealed class SeoTemplatesSpec
{
    /// <summary>Template for HTML title and default social title fallback.</summary>
    public string? Title { get; set; }
    /// <summary>Template for meta description.</summary>
    public string? Description { get; set; }
}
