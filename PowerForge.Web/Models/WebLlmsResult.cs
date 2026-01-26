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
}

/// <summary>Result payload for pipeline execution.</summary>
public sealed class WebPipelineResult
{
    /// <summary>Number of executed steps.</summary>
    public int StepCount { get; set; }
    /// <summary>Overall success status.</summary>
    public bool Success { get; set; }
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
    /// <summary>Optional step message.</summary>
    public string? Message { get; set; }
}

/// <summary>Result payload for optimization pass.</summary>
public sealed class WebOptimizeResult
{
    /// <summary>Number of files updated.</summary>
    public int UpdatedCount { get; set; }
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
