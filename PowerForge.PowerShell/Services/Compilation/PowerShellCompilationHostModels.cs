namespace PowerForge;

/// <summary>Source and delivery options for checking one artifact selection.</summary>
public sealed class PowerShellCompilationCheckRequest
{
    /// <summary>Creates a request for a file or module folder.</summary>
    public PowerShellCompilationCheckRequest(string path) => Path = path;

    /// <summary>Script, module manifest, script module, or module folder.</summary>
    public string Path { get; }
    /// <summary>Optional override for the artifact kind inferred from the source.</summary>
    public PowerShellCompilationArtifactKind? Kind { get; set; }
    /// <summary>Optional override for the compilation mode inferred from the source.</summary>
    public PowerShellCompilationMode? Mode { get; set; }
    /// <summary>Exact target selection, when supplied.</summary>
    public PowerShellCompilationTargetContract? Target { get; set; }
    /// <summary>Compatibility framework when no exact target is supplied.</summary>
    public string TargetFramework { get; set; } = PowerShellCompilationTargetFrameworkPolicy.Default;
    /// <summary>Semantic oracle profile when no exact target is supplied.</summary>
    public string? SemanticProfileId { get; set; }
    /// <summary>Optional resource selection.</summary>
    public PowerShellCompilationResourceMode ResourceMode { get; set; }
    /// <summary>Additional resource patterns evaluated by the dependency planner.</summary>
    public string[] IncludeResource { get; set; } = Array.Empty<string>();
    /// <summary>Resource patterns excluded by the dependency planner.</summary>
    public string[] ExcludeResource { get; set; } = Array.Empty<string>();
    /// <summary>Output directory used when determining resource inclusion.</summary>
    public string? OutputDirectory { get; set; }
}

/// <summary>The target and dependency graph shown for review before building.</summary>
public sealed class PowerShellCompilationReview
{
    /// <summary>Serialized review schema.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Exact target for which the graph was evaluated.</summary>
    public PowerShellCompilationTargetContract? Target { get; set; }
    /// <summary>Dependency graph covered by the review.</summary>
    public PowerShellCompilationDependencyGraph? DependencyLock { get; set; }
}

/// <summary>Whether a compiler finding prevents the selected build.</summary>
public enum PowerShellCompilationHostSeverity
{
    /// <summary>The finding describes a hosted boundary or other advisory.</summary>
    Warning,
    /// <summary>The requested operation cannot proceed.</summary>
    Error
}

/// <summary>A source or environment finding shown before artifact production.</summary>
public sealed class PowerShellCompilationHostDiagnostic
{
    /// <summary>Severity for the selected target.</summary>
    public PowerShellCompilationHostSeverity Severity { get; set; }
    /// <summary>Stable compiler diagnostic code.</summary>
    public PowerShellCompilationDiagnosticCode Code { get; set; }
    /// <summary>Stable feature identifier.</summary>
    public string FeatureId { get; set; } = string.Empty;
    /// <summary>Problem reported by the compiler.</summary>
    public string Message { get; set; } = string.Empty;
    /// <summary>Source file, when known.</summary>
    public string FilePath { get; set; } = string.Empty;
    /// <summary>One-based line, or zero when unknown.</summary>
    public int Line { get; set; }
    /// <summary>One-based column, or zero when unknown.</summary>
    public int Column { get; set; }
    /// <summary>Suggested corrective action.</summary>
    public string Fix { get; set; } = string.Empty;
}

/// <summary>Source analysis, target recommendation, and dependency review for a selected build.</summary>
public sealed class PowerShellCompilationCheckResult
{
    /// <summary>Serialized result schema.</summary>
    public int SchemaVersion => 1;
    /// <summary>Whether the selected artifact can proceed to a reviewed build.</summary>
    public bool CanProceed => ProposedReview is not null;
    /// <summary>Directory inventoried for a script module input.</summary>
    public string? ModuleSourceDirectory { get; set; }
    /// <summary>Canonical artifact-aware analysis.</summary>
    public PowerShellCompilationExplanation? Explanation { get; set; }
    /// <summary>Action and reasons for this checked target.</summary>
    public PowerShellCompilationProfileRecommendation? Recommendation { get; set; }
    /// <summary>Dependency decisions for the selected build.</summary>
    public PowerShellCompilationDependency[] Dependencies { get; set; } = Array.Empty<PowerShellCompilationDependency>();
    /// <summary>Source and dependency findings in source order.</summary>
    public PowerShellCompilationHostDiagnostic[] Diagnostics { get; set; } = Array.Empty<PowerShellCompilationHostDiagnostic>();
    /// <summary>The proposed dependency graph and target, available when the check can proceed.</summary>
    public PowerShellCompilationReview? ProposedReview { get; set; }
}

/// <summary>The available SDK and environment findings for a build host.</summary>
public sealed class PowerShellCompilationDoctorResult
{
    /// <summary>Serialized result schema.</summary>
    public int SchemaVersion => 1;
    /// <summary>Whether all performed checks passed.</summary>
    public bool Succeeded => Diagnostics.Length == 0;
    /// <summary>The selected .NET SDK version, when discovered.</summary>
    public string? DotNetSdkVersion { get; set; }
    /// <summary>Missing or incompatible prerequisites.</summary>
    public PowerShellCompilationHostDiagnostic[] Diagnostics { get; set; } = Array.Empty<PowerShellCompilationHostDiagnostic>();
}
