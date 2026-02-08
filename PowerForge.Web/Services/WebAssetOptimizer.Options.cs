namespace PowerForge.Web;

/// <summary>Options for asset optimization.</summary>
public sealed class WebAssetOptimizerOptions
{
    /// <summary>Root directory of the generated site.</summary>
    public string SiteRoot { get; set; } = ".";
    /// <summary>Optional critical CSS file path.</summary>
    public string? CriticalCssPath { get; set; }
    /// <summary>Regex pattern used to match stylesheet links.</summary>
    public string CssLinkPattern { get; set; } = "(app|api-docs)\\.css";
    /// <summary>When true, minify HTML files.</summary>
    public bool MinifyHtml { get; set; } = false;
    /// <summary>Glob-style include patterns for HTML processing (empty means include all).</summary>
    public string[] HtmlInclude { get; set; } = Array.Empty<string>();
    /// <summary>Glob-style exclude patterns for HTML processing.</summary>
    public string[] HtmlExclude { get; set; } = Array.Empty<string>();
    /// <summary>Max number of HTML files to process (0 disables).</summary>
    public int MaxHtmlFiles { get; set; } = 0;
    /// <summary>When true, minify CSS files.</summary>
    public bool MinifyCss { get; set; } = false;
    /// <summary>When true, minify JavaScript files.</summary>
    public bool MinifyJs { get; set; } = false;
    /// <summary>When true, optimize image files.</summary>
    public bool OptimizeImages { get; set; } = false;
    /// <summary>File extensions considered for image optimization.</summary>
    public string[] ImageExtensions { get; set; } = new[] { ".png", ".jpg", ".jpeg", ".webp" };
    /// <summary>Glob-style include patterns for image optimization.</summary>
    public string[] ImageInclude { get; set; } = Array.Empty<string>();
    /// <summary>Glob-style exclude patterns for image optimization.</summary>
    public string[] ImageExclude { get; set; } = Array.Empty<string>();
    /// <summary>Image quality target in range 1-100.</summary>
    public int ImageQuality { get; set; } = 82;
    /// <summary>When true, strip metadata from optimized images.</summary>
    public bool ImageStripMetadata { get; set; } = true;
    /// <summary>When true, generate WebP variants for supported images.</summary>
    public bool ImageGenerateWebp { get; set; } = false;
    /// <summary>When true, generate AVIF variants for supported images.</summary>
    public bool ImageGenerateAvif { get; set; } = false;
    /// <summary>When true, rewrite image src URLs to preferred next-gen variants when available.</summary>
    public bool ImagePreferNextGen { get; set; } = false;
    /// <summary>Responsive image widths (pixels) used to generate srcset variants.</summary>
    public int[] ResponsiveImageWidths { get; set; } = Array.Empty<int>();
    /// <summary>When true, add lazy-loading and decoding hints to image tags.</summary>
    public bool EnhanceImageTags { get; set; } = false;
    /// <summary>Maximum allowed final bytes per image (0 disables).</summary>
    public long ImageMaxBytesPerFile { get; set; } = 0;
    /// <summary>Maximum allowed total final image bytes (0 disables).</summary>
    public long ImageMaxTotalBytes { get; set; } = 0;
    /// <summary>Enable asset hashing (fingerprinting).</summary>
    public bool HashAssets { get; set; }
    /// <summary>File extensions to hash.</summary>
    public string[] HashExtensions { get; set; } = new[] { ".css", ".js" };
    /// <summary>Glob-style exclude patterns for hashing.</summary>
    public string[] HashExclude { get; set; } = Array.Empty<string>();
    /// <summary>Optional manifest path for hashed assets.</summary>
    public string? HashManifestPath { get; set; }
    /// <summary>Optional optimization report output path (relative to site root).</summary>
    public string? ReportPath { get; set; }
    /// <summary>Optional asset policy for rewrites and headers.</summary>
    public AssetPolicySpec? AssetPolicy { get; set; }
}

