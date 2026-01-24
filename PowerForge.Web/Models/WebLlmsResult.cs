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
