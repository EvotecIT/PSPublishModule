namespace PowerForge.Web;

/// <summary>SEO doctor options for generated HTML output.</summary>
public sealed class WebSeoDoctorOptions
{
    /// <summary>Generated site root.</summary>
    public string SiteRoot { get; set; } = string.Empty;
    /// <summary>Optional include glob filters (relative to site root).</summary>
    public string[] Include { get; set; } = Array.Empty<string>();
    /// <summary>Optional exclude glob filters (relative to site root).</summary>
    public string[] Exclude { get; set; } = Array.Empty<string>();
    /// <summary>When true, built-in utility excludes are applied.</summary>
    public bool UseDefaultExcludes { get; set; } = true;
    /// <summary>Maximum HTML files to scan (0 disables cap).</summary>
    public int MaxHtmlFiles { get; set; }
    /// <summary>When false, pages declaring robots noindex are skipped.</summary>
    public bool IncludeNoIndexPages { get; set; }

    /// <summary>When true, run title length checks.</summary>
    public bool CheckTitleLength { get; set; } = true;
    /// <summary>When true, run meta description length checks.</summary>
    public bool CheckDescriptionLength { get; set; } = true;
    /// <summary>When true, enforce single visible h1 checks.</summary>
    public bool CheckH1 { get; set; } = true;
    /// <summary>When true, detect images missing alt attributes.</summary>
    public bool CheckImageAlt { get; set; } = true;
    /// <summary>When true, detect duplicate title intent across pages.</summary>
    public bool CheckDuplicateTitles { get; set; } = true;
    /// <summary>When true, detect orphan page candidates (zero inbound links).</summary>
    public bool CheckOrphanPages { get; set; } = true;
    /// <summary>When true, evaluate focus keyphrase hints from page metadata.</summary>
    public bool CheckFocusKeyphrase { get; set; }
    /// <summary>When true, validate canonical link tags.</summary>
    public bool CheckCanonical { get; set; } = true;
    /// <summary>When true, validate hreflang alternate link tags.</summary>
    public bool CheckHreflang { get; set; } = true;
    /// <summary>When true, validate JSON-LD structured data blocks.</summary>
    public bool CheckStructuredData { get; set; } = true;
    /// <summary>When true, emit warning when canonical is missing.</summary>
    public bool RequireCanonical { get; set; }
    /// <summary>When true, emit warning when hreflang alternates are missing.</summary>
    public bool RequireHreflang { get; set; }
    /// <summary>When true and hreflang alternates exist, require x-default alternate.</summary>
    public bool RequireHreflangXDefault { get; set; }
    /// <summary>When true, emit warning when no JSON-LD structured data blocks are present.</summary>
    public bool RequireStructuredData { get; set; }

    /// <summary>Minimum recommended title length.</summary>
    public int MinTitleLength { get; set; } = 30;
    /// <summary>Maximum recommended title length.</summary>
    public int MaxTitleLength { get; set; } = 60;
    /// <summary>Minimum recommended meta description length.</summary>
    public int MinDescriptionLength { get; set; } = 70;
    /// <summary>Maximum recommended meta description length.</summary>
    public int MaxDescriptionLength { get; set; } = 160;
    /// <summary>Minimum focus keyphrase mentions in body text when present.</summary>
    public int MinFocusKeyphraseMentions { get; set; } = 2;
    /// <summary>Meta names used to resolve focus keyphrase values.</summary>
    public string[] FocusKeyphraseMetaNames { get; set; } = new[] { "pf:focus-keyphrase", "focus-keyphrase", "seo-focus-keyphrase" };
}
