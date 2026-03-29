namespace PowerForge.Web;

/// <summary>Resolved website runner execution details.</summary>
public sealed class WebWebsiteRunnerResult
{
    /// <summary>Runner mode used for execution.</summary>
    public string EngineMode { get; set; } = string.Empty;

    /// <summary>Resolved website root.</summary>
    public string WebsiteRoot { get; set; } = string.Empty;

    /// <summary>Resolved pipeline config path.</summary>
    public string PipelineConfig { get; set; } = string.Empty;

    /// <summary>Resolved pipeline mode.</summary>
    public string PipelineMode { get; set; } = string.Empty;

    /// <summary>Resolved GitHub repository when source mode is used.</summary>
    public string? Repository { get; set; }

    /// <summary>Resolved git ref when source mode is used.</summary>
    public string? Ref { get; set; }

    /// <summary>Resolved GitHub release tag when binary mode is used.</summary>
    public string? Tag { get; set; }

    /// <summary>Resolved release asset when binary mode is used.</summary>
    public string? Asset { get; set; }

    /// <summary>Resolved executable or project path used to launch the pipeline.</summary>
    public string LaunchedPath { get; set; } = string.Empty;
}
