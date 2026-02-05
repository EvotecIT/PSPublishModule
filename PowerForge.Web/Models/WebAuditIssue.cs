namespace PowerForge.Web;

/// <summary>Structured audit issue entry.</summary>
public sealed class WebAuditIssue
{
    /// <summary>Issue severity (error, warning, info).</summary>
    public string Severity { get; set; } = "warning";
    /// <summary>Issue category (for thresholds/baselines).</summary>
    public string Category { get; set; } = "general";
    /// <summary>Optional page path related to the issue.</summary>
    public string? Path { get; set; }
    /// <summary>Human readable issue message.</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>Stable-ish key used for baseline matching.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>When true, issue is not present in baseline.</summary>
    public bool IsNew { get; set; } = true;
}
