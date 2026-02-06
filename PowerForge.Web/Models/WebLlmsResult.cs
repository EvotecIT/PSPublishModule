namespace PowerForge.Web;

/// <summary>Result payload for llms.txt generation.</summary>
public sealed class WebLlmsResult
{
    /// <summary>Path to the generated llms.txt file.</summary>
    public string LlmsTxtPath { get; set; } = string.Empty;
    /// <summary>Path to the generated llms.json file.</summary>
    public string LlmsJsonPath { get; set; } = string.Empty;
    /// <summary>Path to the generated llms-full.txt file.</summary>
    public string LlmsFullPath { get; set; } = string.Empty;
    /// <summary>Package or project name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Package identifier.</summary>
    public string PackageId { get; set; } = string.Empty;
    /// <summary>Package version.</summary>
    public string Version { get; set; } = "unknown";
    /// <summary>Optional count of API types included.</summary>
    public int? ApiTypeCount { get; set; }
}

/// <summary>Result payload for sitemap generation.</summary>
public sealed class WebSitemapResult
{
    /// <summary>Path to the sitemap output.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Optional path to the HTML sitemap output.</summary>
    public string? HtmlOutputPath { get; set; }
    /// <summary>Number of URLs emitted.</summary>
    public int UrlCount { get; set; }
}

/// <summary>Result payload for API documentation generation.</summary>
public sealed class WebApiDocsResult
{
    /// <summary>Path to the API docs output root.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Path to the API docs index file.</summary>
    public string IndexPath { get; set; } = string.Empty;
    /// <summary>Path to the search index file.</summary>
    public string SearchPath { get; set; } = string.Empty;
    /// <summary>Path to the types index file.</summary>
    public string TypesPath { get; set; } = string.Empty;
    /// <summary>Number of types documented.</summary>
    public int TypeCount { get; set; }
    /// <summary>True when reflection was used to populate types.</summary>
    public bool UsedReflectionFallback { get; set; }
    /// <summary>Warnings emitted during generation.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>Result payload for pipeline execution.</summary>
public sealed class WebPipelineResult
{
    /// <summary>Number of executed steps.</summary>
    public int StepCount { get; set; }
    /// <summary>Overall success status.</summary>
    public bool Success { get; set; }
    /// <summary>Total pipeline duration in milliseconds.</summary>
    public long DurationMs { get; set; }
    /// <summary>Resolved cache path when cache is enabled.</summary>
    public string? CachePath { get; set; }
    /// <summary>Resolved profile path when profile output is written.</summary>
    public string? ProfilePath { get; set; }
    /// <summary>Step-by-step results.</summary>
    public List<WebPipelineStepResult> Steps { get; set; } = new();
}

/// <summary>Result payload for publish command.</summary>
public sealed class WebPublishResult
{
    /// <summary>Overall success status.</summary>
    public bool Success { get; set; }
    /// <summary>Build output path when build step ran.</summary>
    public string? BuildOutputPath { get; set; }
    /// <summary>Count of overlay files copied.</summary>
    public int? OverlayCopiedCount { get; set; }
    /// <summary>Publish output path.</summary>
    public string? PublishOutputPath { get; set; }
    /// <summary>Count of optimized files updated.</summary>
    public int? OptimizeUpdatedCount { get; set; }
}

/// <summary>Result payload for a pipeline step.</summary>
public sealed class WebPipelineStepResult
{
    /// <summary>Step task identifier.</summary>
    public string Task { get; set; } = string.Empty;
    /// <summary>Step success status.</summary>
    public bool Success { get; set; }
    /// <summary>True when step execution was skipped due to cache hit.</summary>
    public bool Cached { get; set; }
    /// <summary>Step duration in milliseconds.</summary>
    public long DurationMs { get; set; }
    /// <summary>Optional step message.</summary>
    public string? Message { get; set; }
}

/// <summary>Result payload for optimization pass.</summary>
public sealed class WebOptimizeResult
{
    /// <summary>Total number of unique files updated.</summary>
    public int UpdatedCount { get; set; }
    /// <summary>Relative paths for files updated during optimization.</summary>
    public string[] UpdatedFiles { get; set; } = Array.Empty<string>();
    /// <summary>Total HTML files discovered under site root.</summary>
    public int HtmlFileCount { get; set; }
    /// <summary>Total CSS files discovered under site root.</summary>
    public int CssFileCount { get; set; }
    /// <summary>Total JavaScript files discovered under site root.</summary>
    public int JsFileCount { get; set; }
    /// <summary>Number of HTML files updated by critical CSS inlining.</summary>
    public int CriticalCssInlinedCount { get; set; }
    /// <summary>Number of HTML files minified.</summary>
    public int HtmlMinifiedCount { get; set; }
    /// <summary>Total UTF-8 bytes saved while minifying HTML files.</summary>
    public long HtmlBytesSaved { get; set; }
    /// <summary>Number of CSS files minified.</summary>
    public int CssMinifiedCount { get; set; }
    /// <summary>Total UTF-8 bytes saved while minifying CSS files.</summary>
    public long CssBytesSaved { get; set; }
    /// <summary>Number of JavaScript files minified.</summary>
    public int JsMinifiedCount { get; set; }
    /// <summary>Total UTF-8 bytes saved while minifying JavaScript files.</summary>
    public long JsBytesSaved { get; set; }
    /// <summary>Number of assets renamed during hashing.</summary>
    public int HashedAssetCount { get; set; }
    /// <summary>Detailed original-to-hashed asset mapping.</summary>
    public WebOptimizeHashedAssetEntry[] HashedAssets { get; set; } = Array.Empty<WebOptimizeHashedAssetEntry>();
    /// <summary>Number of HTML files with rewritten references after hashing.</summary>
    public int HtmlHashRewriteCount { get; set; }
    /// <summary>Number of CSS files with rewritten references after hashing.</summary>
    public int CssHashRewriteCount { get; set; }
    /// <summary>Resolved path to the generated hash manifest (if written).</summary>
    public string? HashManifestPath { get; set; }
    /// <summary>True when cache headers file was written.</summary>
    public bool CacheHeadersWritten { get; set; }
    /// <summary>Optional path to generated cache headers file.</summary>
    public string? CacheHeadersPath { get; set; }
    /// <summary>Optional path to the optimization report JSON file.</summary>
    public string? ReportPath { get; set; }
}

/// <summary>Maps an original asset path to its hashed output path.</summary>
public sealed class WebOptimizeHashedAssetEntry
{
    /// <summary>Original asset path.</summary>
    public string OriginalPath { get; set; } = string.Empty;
    /// <summary>Hashed asset path.</summary>
    public string HashedPath { get; set; } = string.Empty;
}

/// <summary>Result payload for dotnet build.</summary>
public sealed class WebDotNetBuildResult
{
    /// <summary>Build success status.</summary>
    public bool Success { get; set; }
    /// <summary>Exit code returned by dotnet.</summary>
    public int ExitCode { get; set; }
    /// <summary>Captured stdout.</summary>
    public string Output { get; set; } = string.Empty;
    /// <summary>Captured stderr.</summary>
    public string Error { get; set; } = string.Empty;
}

/// <summary>Result payload for dotnet publish.</summary>
public sealed class WebDotNetPublishResult
{
    /// <summary>Publish success status.</summary>
    public bool Success { get; set; }
    /// <summary>Exit code returned by dotnet.</summary>
    public int ExitCode { get; set; }
    /// <summary>Captured stdout.</summary>
    public string Output { get; set; } = string.Empty;
    /// <summary>Captured stderr.</summary>
    public string Error { get; set; } = string.Empty;
    /// <summary>Publish output path.</summary>
    public string OutputPath { get; set; } = string.Empty;
}

/// <summary>Result payload for static overlay copy.</summary>
public sealed class WebStaticOverlayResult
{
    /// <summary>Number of files copied.</summary>
    public int CopiedCount { get; set; }
}
