namespace PowerForge.Web;

/// <summary>Options for sitemap generation.</summary>
public sealed class WebSitemapOptions
{
    /// <summary>Root directory of the generated site.</summary>
    public string SiteRoot { get; set; } = ".";
    /// <summary>Base URL for sitemap entries.</summary>
    public string BaseUrl { get; set; } = string.Empty;
    /// <summary>Optional output path override.</summary>
    public string? OutputPath { get; set; }
    /// <summary>Optional existing API sitemap path to merge.</summary>
    public string? ApiSitemapPath { get; set; }
    /// <summary>Additional paths to include.</summary>
    public string[]? ExtraPaths { get; set; }
    /// <summary>Explicit sitemap entries.</summary>
    public WebSitemapEntry[]? Entries { get; set; }
    /// <summary>Optional JSON file containing sitemap entries (array or object with entries[]).</summary>
    public string? EntriesJsonPath { get; set; }
    /// <summary>When true, merge sitemap metadata emitted by the PowerForge site build under _powerforge/sitemap-entries.json.</summary>
    public bool UseGeneratedSitemapMetadata { get; set; } = true;
    /// <summary>When true, include HTML files.</summary>
    public bool IncludeHtmlFiles { get; set; } = true;
    /// <summary>When true, include HTML files that declare robots noindex.</summary>
    public bool IncludeNoIndexHtml { get; set; }
    /// <summary>When true, apply default exclusion patterns for utility HTML files.</summary>
    public bool UseDefaultExcludePatterns { get; set; } = true;
    /// <summary>Additional exclusion patterns for HTML route discovery.</summary>
    public string[]? ExcludePatterns { get; set; }
    /// <summary>When true, include text files (robots/llms).</summary>
    public bool IncludeTextFiles { get; set; } = true;
    /// <summary>When true, include HTML pages that declare robots noindex metadata.</summary>
    public bool IncludeNoIndexPages { get; set; }
    /// <summary>When true, emit localized alternate URLs (hreflang/x-default) when localization is configured.</summary>
    public bool IncludeLanguageAlternates { get; set; } = true;
    /// <summary>When true, generate an HTML sitemap.</summary>
    public bool GenerateHtml { get; set; }
    /// <summary>When true, generate a machine-readable sitemap JSON file.</summary>
    public bool GenerateJson { get; set; }
    /// <summary>Optional sitemap JSON output path.</summary>
    public string? JsonOutputPath { get; set; }
    /// <summary>Optional HTML sitemap output path.</summary>
    public string? HtmlOutputPath { get; set; }
    /// <summary>Optional HTML sitemap template path.</summary>
    public string? HtmlTemplatePath { get; set; }
    /// <summary>Optional HTML title override.</summary>
    public string? HtmlTitle { get; set; }
    /// <summary>Optional CSS href to include in the HTML sitemap.</summary>
    public string? HtmlCssHref { get; set; }
    /// <summary>When true, include the generated HTML sitemap route in sitemap.xml.</summary>
    public bool IncludeGeneratedHtmlRouteInXml { get; set; }
    /// <summary>When true, write a lightweight browser stylesheet for generated XML sitemaps. The generated stylesheet file is overwritten on each run when the href resolves under the site root.</summary>
    public bool GenerateBrowserStylesheet { get; set; } = true;
    /// <summary>Optional href override for the generated XML sitemap browser stylesheet.</summary>
    public string? BrowserStylesheetHref { get; set; }
    /// <summary>Optional news sitemap generation options.</summary>
    public WebSitemapNewsOptions? NewsSitemap { get; set; }
    /// <summary>Optional image sitemap generation options.</summary>
    public WebSitemapImageOptions? ImageSitemap { get; set; }
    /// <summary>Optional video sitemap generation options.</summary>
    public WebSitemapVideoOptions? VideoSitemap { get; set; }
    /// <summary>Optional sitemap index output path.</summary>
    public string? SitemapIndexPath { get; set; }
}

/// <summary>Options for specialized news sitemap output.</summary>
public sealed class WebSitemapNewsOptions
{
    /// <summary>Optional output path override for the news sitemap XML.</summary>
    public string? OutputPath { get; set; }
    /// <summary>Optional path patterns used to select entries for news sitemap output.</summary>
    public string[]? PathPatterns { get; set; }
    /// <summary>Optional publication name for news metadata.</summary>
    public string? PublicationName { get; set; }
    /// <summary>Optional publication language code for news metadata (for example: en).</summary>
    public string? PublicationLanguage { get; set; }
    /// <summary>Optional news genres metadata.</summary>
    public string? Genres { get; set; }
    /// <summary>Optional news access metadata.</summary>
    public string? Access { get; set; }
    /// <summary>Optional news keywords metadata.</summary>
    public string? Keywords { get; set; }
}

/// <summary>Options for specialized image sitemap output.</summary>
public sealed class WebSitemapImageOptions
{
    /// <summary>Optional output path override for the image sitemap XML.</summary>
    public string? OutputPath { get; set; }
    /// <summary>Optional path patterns used to select entries for image sitemap output.</summary>
    public string[]? PathPatterns { get; set; }
}

/// <summary>Options for specialized video sitemap output.</summary>
public sealed class WebSitemapVideoOptions
{
    /// <summary>Optional output path override for the video sitemap XML.</summary>
    public string? OutputPath { get; set; }
    /// <summary>Optional path patterns used to select entries for video sitemap output.</summary>
    public string[]? PathPatterns { get; set; }
}

/// <summary>Explicit sitemap entry metadata.</summary>
public sealed class WebSitemapEntry
{
    /// <summary>Route path (relative to base URL).</summary>
    public string Path { get; set; } = "/";
    /// <summary>Optional display title used by HTML sitemap renderers.</summary>
    public string? Title { get; set; }
    /// <summary>Optional description used by HTML sitemap renderers.</summary>
    public string? Description { get; set; }
    /// <summary>Optional section/group label used by HTML sitemap renderers.</summary>
    public string? Section { get; set; }
    /// <summary>Optional change frequency value.</summary>
    public string? ChangeFrequency { get; set; }
    /// <summary>Optional priority value.</summary>
    public string? Priority { get; set; }
    /// <summary>Optional last-modified date.</summary>
    public string? LastModified { get; set; }
    /// <summary>Optional canonical URL; noncanonical variants are excluded from generated sitemaps.</summary>
    public string? Canonical { get; set; }
    /// <summary>Original publication date used by news sitemaps, independently of modification time.</summary>
    public string? PublicationDate { get; set; }
    /// <summary>Optional localized alternate URLs for this path.</summary>
    public WebSitemapAlternate[] Alternates { get; set; } = Array.Empty<WebSitemapAlternate>();
    /// <summary>Optional page-associated image URLs (absolute or site-relative).</summary>
    public string[] ImageUrls { get; set; } = Array.Empty<string>();
    /// <summary>Optional page-associated video URLs (absolute or site-relative).</summary>
    public string[] VideoUrls { get; set; } = Array.Empty<string>();
    /// <summary>When true, page declares robots noindex metadata.</summary>
    public bool NoIndex { get; set; }
}

/// <summary>Localized alternate URL mapping for sitemap entries.</summary>
public sealed class WebSitemapAlternate
{
    /// <summary>Language code (for example en, pl, x-default).</summary>
    public string HrefLang { get; set; } = string.Empty;
    /// <summary>Route path relative to site root.</summary>
    public string Path { get; set; } = "/";
    /// <summary>Optional absolute URL override for this alternate.</summary>
    public string? Url { get; set; }
}
