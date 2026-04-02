namespace PowerForge;

/// <summary>
/// Result of executing a <see cref="ModulePipelineSpec"/>.
/// </summary>
public sealed class ModulePipelineResult
{
    /// <summary>
    /// Planned execution details.
    /// </summary>
    public ModulePipelinePlan Plan { get; }

    /// <summary>
    /// Build result produced by the module build pipeline staging step.
    /// </summary>
    public ModuleBuildResult BuildResult { get; }

    /// <summary>
    /// Install result when install was enabled; otherwise null.
    /// </summary>
    public ModuleInstallerResult? InstallResult { get; }

    /// <summary>
    /// Documentation result when documentation generation was enabled; otherwise null.
    /// </summary>
    public DocumentationBuildResult? DocumentationResult { get; }

    /// <summary>
    /// File consistency report when enabled; otherwise null.
    /// </summary>
    public ProjectConsistencyReport? FileConsistencyReport { get; }

    /// <summary>
    /// File consistency status computed by the pipeline (pass/warn/fail).
    /// </summary>
    public CheckStatus? FileConsistencyStatus { get; }

    /// <summary>
    /// Encoding conversion result when AutoFix was enabled; otherwise null.
    /// </summary>
    public ProjectConversionResult? FileConsistencyEncodingFix { get; }

    /// <summary>
    /// Line ending conversion result when AutoFix was enabled; otherwise null.
    /// </summary>
    public ProjectConversionResult? FileConsistencyLineEndingFix { get; }

    /// <summary>
    /// Project-root file consistency report when enabled; otherwise null.
    /// </summary>
    public ProjectConsistencyReport? ProjectRootFileConsistencyReport { get; }

    /// <summary>
    /// Project-root file consistency status computed by the pipeline (pass/warn/fail).
    /// </summary>
    public CheckStatus? ProjectRootFileConsistencyStatus { get; }

    /// <summary>
    /// Project-root encoding conversion result when AutoFix was enabled; otherwise null.
    /// </summary>
    public ProjectConversionResult? ProjectRootFileConsistencyEncodingFix { get; }

    /// <summary>
    /// Project-root line ending conversion result when AutoFix was enabled; otherwise null.
    /// </summary>
    public ProjectConversionResult? ProjectRootFileConsistencyLineEndingFix { get; }

    /// <summary>
    /// Compatibility report when enabled; otherwise null.
    /// </summary>
    public PowerShellCompatibilityReport? CompatibilityReport { get; }

    /// <summary>
    /// Module validation report when enabled; otherwise null.
    /// </summary>
    public ModuleValidationReport? ValidationReport { get; }

    /// <summary>
    /// Unified structured diagnostics produced by the pipeline.
    /// </summary>
    public BuildDiagnostic[] Diagnostics { get; }

    /// <summary>
    /// Baseline comparison details for diagnostics when baseline options were used.
    /// </summary>
    public BuildDiagnosticsBaselineComparison? DiagnosticsBaseline { get; }

    /// <summary>
    /// Diagnostics policy evaluation for the run when policy options were used.
    /// </summary>
    public BuildDiagnosticsPolicyEvaluation? DiagnosticsPolicy { get; }

    /// <summary>
    /// Formatting results for the staging output (empty when formatting was disabled).
    /// </summary>
    public FormatterResult[] FormattingStagingResults { get; }

    /// <summary>
    /// Formatting results for the project root (empty when formatting was disabled).
    /// </summary>
    public FormatterResult[] FormattingProjectResults { get; }

    /// <summary>
    /// Publish results produced during the run.
    /// </summary>
    public ModulePublishResult[] PublishResults { get; }

    /// <summary>
    /// Signing result when signing was enabled; otherwise null.
    /// </summary>
    public ModuleSigningResult? SigningResult { get; }

    /// <summary>
    /// Artefact results produced during the run.
    /// </summary>
    public ArtefactBuildResult[] ArtefactResults { get; }

    /// <summary>
    /// Owner-facing notes captured during the run for the final summary.
    /// </summary>
    public ModuleOwnerNote[] OwnerNotes { get; }

    /// <summary>
    /// Creates a new result instance.
    /// </summary>
    public ModulePipelineResult(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        ModuleInstallerResult? installResult,
        DocumentationBuildResult? documentationResult,
        ProjectConsistencyReport? fileConsistencyReport,
        CheckStatus? fileConsistencyStatus,
        ProjectConversionResult? fileConsistencyEncodingFix,
        ProjectConversionResult? fileConsistencyLineEndingFix,
        PowerShellCompatibilityReport? compatibilityReport,
        ModuleValidationReport? validationReport,
        BuildDiagnostic[]? diagnostics,
        BuildDiagnosticsBaselineComparison? diagnosticsBaseline,
        BuildDiagnosticsPolicyEvaluation? diagnosticsPolicy,
        ModulePublishResult[] publishResults,
        ArtefactBuildResult[] artefactResults,
        FormatterResult[]? formattingStagingResults = null,
        FormatterResult[]? formattingProjectResults = null,
        ProjectConsistencyReport? projectRootFileConsistencyReport = null,      
        CheckStatus? projectRootFileConsistencyStatus = null,
        ProjectConversionResult? projectRootFileConsistencyEncodingFix = null,  
        ProjectConversionResult? projectRootFileConsistencyLineEndingFix = null,
        ModuleSigningResult? signingResult = null,
        ModuleOwnerNote[]? ownerNotes = null)
    {
        Plan = plan;
        BuildResult = buildResult;
        InstallResult = installResult;
        DocumentationResult = documentationResult;
        FileConsistencyReport = fileConsistencyReport;
        FileConsistencyStatus = fileConsistencyStatus;
        FileConsistencyEncodingFix = fileConsistencyEncodingFix;
        FileConsistencyLineEndingFix = fileConsistencyLineEndingFix;
        ProjectRootFileConsistencyReport = projectRootFileConsistencyReport;
        ProjectRootFileConsistencyStatus = projectRootFileConsistencyStatus;
        ProjectRootFileConsistencyEncodingFix = projectRootFileConsistencyEncodingFix;
        ProjectRootFileConsistencyLineEndingFix = projectRootFileConsistencyLineEndingFix;
        CompatibilityReport = compatibilityReport;
        ValidationReport = validationReport;
        Diagnostics = diagnostics ?? Array.Empty<BuildDiagnostic>();
        DiagnosticsBaseline = diagnosticsBaseline;
        DiagnosticsPolicy = diagnosticsPolicy;
        FormattingStagingResults = formattingStagingResults ?? Array.Empty<FormatterResult>();
        FormattingProjectResults = formattingProjectResults ?? Array.Empty<FormatterResult>();
        PublishResults = publishResults ?? Array.Empty<ModulePublishResult>();  
        ArtefactResults = artefactResults ?? Array.Empty<ArtefactBuildResult>();
        SigningResult = signingResult;
        OwnerNotes = ownerNotes ?? Array.Empty<ModuleOwnerNote>();
    }
}
