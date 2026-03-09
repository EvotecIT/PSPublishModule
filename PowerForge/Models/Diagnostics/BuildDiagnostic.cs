#pragma warning disable CS1591
namespace PowerForge;

/// <summary>
/// High-level diagnostic area used by build reporting and policy.
/// </summary>
public enum BuildDiagnosticArea
{
    /// <summary>Encoding, line ending, and related file normalization issues.</summary>
    FileConsistency,
    /// <summary>Cross-version or cross-edition PowerShell compatibility issues.</summary>
    Compatibility,
    /// <summary>Module structure, docs, analyzer, or integrity validation issues.</summary>
    Validation,
    /// <summary>Formatting failures or skipped formatting work.</summary>
    Formatting,
    /// <summary>Authenticode or other signing issues.</summary>
    Signing,
    /// <summary>Artefact creation and packaging issues.</summary>
    Packaging,
    /// <summary>Generated delivery command or bundled delivery content issues.</summary>
    Delivery,
    /// <summary>General build, staging, or orchestration issues.</summary>
    Build
}

/// <summary>
/// Scope where a diagnostic was observed.
/// </summary>
public enum BuildDiagnosticScope
{
    /// <summary>The issue exists in repository/source-controlled files.</summary>
    Project,
    /// <summary>The issue exists in staged output produced during the build.</summary>
    Staging,
    /// <summary>The issue was introduced by generated content.</summary>
    Generated,
    /// <summary>The issue is caused by build configuration or policy.</summary>
    BuildConfig
}

/// <summary>
/// Primary owner responsible for addressing a diagnostic.
/// </summary>
public enum BuildDiagnosticOwner
{
    /// <summary>The module author or maintainer should fix the issue in source.</summary>
    ModuleAuthor,
    /// <summary>The build script or build-policy author should adjust configuration.</summary>
    BuildAuthor,
    /// <summary>The PSPublishModule/PowerForge engine should be changed.</summary>
    EngineAuthor
}

/// <summary>
/// Expected remediation path for a diagnostic.
/// </summary>
public enum BuildDiagnosticRemediationKind
{
    /// <summary>The issue can be corrected automatically by the tool.</summary>
    AutoFix,
    /// <summary>The issue requires a manual code or content change.</summary>
    ManualFix,
    /// <summary>The issue should be addressed by changing build configuration.</summary>
    ConfigChange,
    /// <summary>The issue likely reflects a bug or gap in the engine.</summary>
    EngineBug
}

/// <summary>
/// Severity used by individual build diagnostics.
/// </summary>
public enum BuildDiagnosticSeverity
{
    /// <summary>Informational guidance that does not represent a failure.</summary>
    Info,
    /// <summary>A warning-level issue that should be reviewed.</summary>
    Warning,
    /// <summary>An error-level issue that represents a failing condition.</summary>
    Error
}

/// <summary>
/// Structured diagnostic emitted by the build pipeline.
/// </summary>
public sealed class BuildDiagnostic
{
    /// <summary>Diagnostic identifier stable enough for policies/baselines.</summary>
    public string RuleId { get; }
    /// <summary>Diagnostic area.</summary>
    public BuildDiagnosticArea Area { get; }
    /// <summary>Severity of the diagnostic.</summary>
    public BuildDiagnosticSeverity Severity { get; }
    /// <summary>Scope where the issue was detected.</summary>
    public BuildDiagnosticScope Scope { get; }
    /// <summary>Who should generally address the issue.</summary>
    public BuildDiagnosticOwner Owner { get; }
    /// <summary>Recommended remediation style.</summary>
    public BuildDiagnosticRemediationKind RemediationKind { get; }
    /// <summary>True when the issue can be fixed automatically.</summary>
    public bool CanAutoFix { get; }
    /// <summary>Short summary used in UI/reporting.</summary>
    public string Summary { get; }
    /// <summary>Longer detail or rationale.</summary>
    public string Details { get; }
    /// <summary>Recommended next action.</summary>
    public string RecommendedAction { get; }
    /// <summary>Optional suggested command for the user.</summary>
    public string SuggestedCommand { get; }
    /// <summary>Relevant file or logical source path.</summary>
    public string SourcePath { get; }
    /// <summary>Optional build component that introduced/generated the issue.</summary>
    public string GeneratedBy { get; }

    /// <summary>
    /// Creates a new structured build diagnostic.
    /// </summary>
    public BuildDiagnostic(
        string ruleId,
        BuildDiagnosticArea area,
        BuildDiagnosticSeverity severity,
        BuildDiagnosticScope scope,
        BuildDiagnosticOwner owner,
        BuildDiagnosticRemediationKind remediationKind,
        bool canAutoFix,
        string summary,
        string details,
        string recommendedAction,
        string sourcePath = "",
        string suggestedCommand = "",
        string generatedBy = "")
    {
        RuleId = ruleId ?? string.Empty;
        Area = area;
        Severity = severity;
        Scope = scope;
        Owner = owner;
        RemediationKind = remediationKind;
        CanAutoFix = canAutoFix;
        Summary = summary ?? string.Empty;
        Details = details ?? string.Empty;
        RecommendedAction = recommendedAction ?? string.Empty;
        SourcePath = sourcePath ?? string.Empty;
        SuggestedCommand = suggestedCommand ?? string.Empty;
        GeneratedBy = generatedBy ?? string.Empty;
    }
}
#pragma warning restore CS1591
