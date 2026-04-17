namespace PowerForge.Web;

/// <summary>Result from preparing, verifying, or scanning agent-readiness signals.</summary>
public sealed class WebAgentReadinessResult
{
    /// <summary>Site root used for local checks.</summary>
    public string? SiteRoot { get; set; }
    /// <summary>Base URL used for URL generation or remote scans.</summary>
    public string? BaseUrl { get; set; }
    /// <summary>Operation name: prepare, verify, or scan.</summary>
    public string Operation { get; set; } = string.Empty;
    /// <summary>Whether all required checks passed.</summary>
    public bool Success { get; set; }
    /// <summary>Files written during prepare.</summary>
    public string[] WrittenFiles { get; set; } = Array.Empty<string>();
    /// <summary>Check results.</summary>
    public WebAgentReadinessCheck[] Checks { get; set; } = Array.Empty<WebAgentReadinessCheck>();
    /// <summary>Informational warnings emitted during the operation.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>Single agent-readiness check result.</summary>
public sealed class WebAgentReadinessCheck
{
    /// <summary>Stable check identifier.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Check category.</summary>
    public string Category { get; set; } = string.Empty;
    /// <summary>Human-readable check name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Status: pass, fail, warn, or info.</summary>
    public string Status { get; set; } = string.Empty;
    /// <summary>Short detail message.</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>Relevant local path or URL.</summary>
    public string? Target { get; set; }
}
