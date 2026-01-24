namespace PowerForge.Web;

public sealed class WebLlmsResult
{
    public string LlmsTxtPath { get; set; } = string.Empty;
    public string LlmsJsonPath { get; set; } = string.Empty;
    public string LlmsFullPath { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = "unknown";
    public int? ApiTypeCount { get; set; }
}

public sealed class WebSitemapResult
{
    public string OutputPath { get; set; } = string.Empty;
    public int UrlCount { get; set; }
}

public sealed class WebApiDocsResult
{
    public string OutputPath { get; set; } = string.Empty;
    public string IndexPath { get; set; } = string.Empty;
    public string SearchPath { get; set; } = string.Empty;
    public string TypesPath { get; set; } = string.Empty;
    public int TypeCount { get; set; }
}

public sealed class WebPipelineResult
{
    public int StepCount { get; set; }
    public bool Success { get; set; }
    public List<WebPipelineStepResult> Steps { get; set; } = new();
}

public sealed class WebPublishResult
{
    public bool Success { get; set; }
    public string? BuildOutputPath { get; set; }
    public int? OverlayCopiedCount { get; set; }
    public string? PublishOutputPath { get; set; }
    public int? OptimizeUpdatedCount { get; set; }
}

public sealed class WebPipelineStepResult
{
    public string Task { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public sealed class WebOptimizeResult
{
    public int UpdatedCount { get; set; }
}

public sealed class WebDotNetBuildResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
}

public sealed class WebDotNetPublishResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Output { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
}

public sealed class WebStaticOverlayResult
{
    public int CopiedCount { get; set; }
}
