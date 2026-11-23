namespace PowerForge;

/// <summary>Source, target, and resource selection for checking or preparing an artifact.</summary>
public sealed class PowerShellCompilationRequest
{
    /// <summary>Creates a request for one source file or module folder.</summary>
    public PowerShellCompilationRequest(string path) : this(new[] { path }) { }

    /// <summary>Creates a request for one selection or an ordered set of loose scripts.</summary>
    public PowerShellCompilationRequest(IEnumerable<string> paths)
    {
        if (paths is null) throw new ArgumentNullException(nameof(paths));
        Paths = Array.AsReadOnly(paths.ToArray());
    }

    /// <summary>Source selections in the order supplied by the caller.</summary>
    public IReadOnlyList<string> Paths { get; }
    /// <summary>Entrypoint for a loose set of scripts compiled into an executable.</summary>
    public string? EntryPoint { get; set; }
    /// <summary>Overrides the name inferred from the input.</summary>
    public string? ArtifactName { get; set; }
    /// <summary>Optional override for the artifact kind inferred from the source.</summary>
    public PowerShellCompilationArtifactKind? Kind { get; set; }
    /// <summary>Optional override for the compilation mode inferred from the source.</summary>
    public PowerShellCompilationMode? Mode { get; set; }
    /// <summary>Exact target selection, when supplied.</summary>
    public PowerShellCompilationTargetContract? Target { get; set; }
    /// <summary>Compatibility framework for the selected target.</summary>
    public string? TargetFramework { get; set; }
    /// <summary>Runtime identifier for the selected target.</summary>
    public string? RuntimeIdentifier { get; set; }
    /// <summary>Publishes a self-contained executable for the selected target.</summary>
    public bool? SelfContained { get; set; }
    /// <summary>Publishes the executable as a single file for the selected target.</summary>
    public bool? SingleFile { get; set; }
    /// <summary>Executable optimization for the selected target.</summary>
    public PowerShellCompilationExecutableOptimization? Optimization { get; set; }
    /// <summary>Semantic oracle profile for the selected target.</summary>
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
    /// <summary>Resolved artifact kind, when the source selection could be opened.</summary>
    public PowerShellCompilationArtifactKind? ArtifactKind { get; internal set; }
    /// <summary>Resolved entry script or module, when available.</summary>
    public string? SourcePath { get; internal set; }
    /// <summary>Source files discovered for this selection.</summary>
    public string[] SourceFiles { get; internal set; } = Array.Empty<string>();
    /// <summary>Source directory protected from review-file writes for a module build or module input.</summary>
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
public sealed class PowerShellCompilationEnvironmentResult
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
