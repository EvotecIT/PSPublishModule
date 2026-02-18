namespace PowerForge.Web;

/// <summary>Structured SEO doctor issue entry.</summary>
public sealed class WebSeoDoctorIssue
{
    /// <summary>Issue severity (error, warning, info).</summary>
    public string Severity { get; set; } = "warning";
    /// <summary>Issue category (for thresholds/baselines).</summary>
    public string Category { get; set; } = "general";
    /// <summary>Issue code (for example <c>PFSEO.TITLE.TITLE-SHORT</c>).</summary>
    public string Code { get; set; } = string.Empty;
    /// <summary>Normalized issue hint token used by gates.</summary>
    public string Hint { get; set; } = string.Empty;
    /// <summary>Optional page path related to the issue.</summary>
    public string? Path { get; set; }
    /// <summary>Human readable issue message.</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>Stable-ish key used for baseline matching.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>When true, issue is not present in baseline.</summary>
    public bool IsNew { get; set; } = true;
}

