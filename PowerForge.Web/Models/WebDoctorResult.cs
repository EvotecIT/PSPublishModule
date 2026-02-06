namespace PowerForge.Web;

/// <summary>Combined health-check result for verify/build/audit diagnostics.</summary>
public sealed class WebDoctorResult
{
    /// <summary>Overall doctor status.</summary>
    public bool Success { get; set; }
    /// <summary>Resolved site configuration path.</summary>
    public string ConfigPath { get; set; } = string.Empty;
    /// <summary>Resolved site output path used by audit.</summary>
    public string SiteRoot { get; set; } = string.Empty;
    /// <summary>When true, site build was executed before checks.</summary>
    public bool BuildExecuted { get; set; }
    /// <summary>When true, verify check was executed.</summary>
    public bool VerifyExecuted { get; set; }
    /// <summary>When true, audit check was executed.</summary>
    public bool AuditExecuted { get; set; }
    /// <summary>Verify result payload when verify was executed.</summary>
    public WebVerifyResult? Verify { get; set; }
    /// <summary>Audit result payload when audit was executed.</summary>
    public WebAuditResult? Audit { get; set; }
    /// <summary>Actionable follow-up recommendations.</summary>
    public string[] Recommendations { get; set; } = Array.Empty<string>();
}
