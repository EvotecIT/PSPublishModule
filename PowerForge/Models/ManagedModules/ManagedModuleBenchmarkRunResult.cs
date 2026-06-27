namespace PowerForge;

/// <summary>
/// One measured managed module benchmark run.
/// </summary>
public sealed class ManagedModuleBenchmarkRunResult
{
    /// <summary>
    /// Stable scenario identifier.
    /// </summary>
    public string ScenarioId { get; set; } = string.Empty;

    /// <summary>
    /// Lifecycle operation measured.
    /// </summary>
    public ManagedModuleBenchmarkOperation Operation { get; set; }

    /// <summary>
    /// Measured engine name.
    /// </summary>
    public string Engine { get; set; } = "Managed";

    /// <summary>
    /// One-based iteration number for this scenario.
    /// </summary>
    public int Iteration { get; set; }

    /// <summary>
    /// True when the measured operation completed successfully.
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// Engine-specific status.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Module or package id measured by the scenario.
    /// </summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>
    /// Selected or installed version.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Previous version for update scenarios.
    /// </summary>
    public string? PreviousVersion { get; set; }

    /// <summary>
    /// Module root used by the operation.
    /// </summary>
    public string? ModuleRoot { get; set; }

    /// <summary>
    /// Versioned module path selected by the operation.
    /// </summary>
    public string? ModulePath { get; set; }

    /// <summary>
    /// Outer benchmark elapsed time.
    /// </summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>
    /// Inner service elapsed time when the engine reports it.
    /// </summary>
    public TimeSpan? ServiceElapsed { get; set; }

    /// <summary>
    /// Package bytes downloaded or copied into the package cache.
    /// </summary>
    public long PackageBytes { get; set; }

    /// <summary>
    /// Bytes extracted into the module directory.
    /// </summary>
    public long ExtractedBytes { get; set; }

    /// <summary>
    /// Elapsed time spent extracting the direct package archive.
    /// </summary>
    public TimeSpan? ExtractionElapsed { get; set; }

    /// <summary>
    /// Number of files extracted into the module directory.
    /// </summary>
    public int FileCount { get; set; }

    /// <summary>
    /// Number of dependency results returned by the operation.
    /// </summary>
    public int DependencyCount { get; set; }

    /// <summary>
    /// Number of packages delivered by the operation, including dependencies and family members.
    /// </summary>
    public int PackageCount { get; set; }

    /// <summary>
    /// Package bytes delivered by the operation, including dependencies and family members.
    /// </summary>
    public long TotalPackageBytes { get; set; }

    /// <summary>
    /// Extracted bytes delivered by the operation, including dependencies and family members.
    /// </summary>
    public long TotalExtractedBytes { get; set; }

    /// <summary>
    /// Extracted file count delivered by the operation, including dependencies and family members.
    /// </summary>
    public int TotalFileCount { get; set; }

    /// <summary>
    /// Elapsed time spent extracting packages, including dependencies and family members.
    /// </summary>
    public TimeSpan? TotalExtractionElapsed { get; set; }

    /// <summary>
    /// Repository HTTP request attempts observed during the run.
    /// </summary>
    public long RepositoryRequestCount { get; set; }

    /// <summary>
    /// Final disk size of the selected module directory after the operation.
    /// </summary>
    public long FinalDiskBytes { get; set; }

    /// <summary>
    /// True when the package came from the package cache.
    /// </summary>
    public bool FromCache { get; set; }

    /// <summary>
    /// Version read back from the installed module directory, when validation could inspect it.
    /// </summary>
    public string? ValidatedVersion { get; set; }

    /// <summary>
    /// True when the installed module directory reports the expected version.
    /// </summary>
    public bool? VersionValidationSucceeded { get; set; }

    /// <summary>
    /// Human-readable version validation evidence.
    /// </summary>
    public string? VersionValidationMessage { get; set; }

    /// <summary>
    /// Optional out-of-process import validation results for this run.
    /// </summary>
    public IReadOnlyList<ManagedModuleImportValidationResult> ImportValidations { get; set; } = Array.Empty<ManagedModuleImportValidationResult>();

    /// <summary>
    /// Failure message when the operation failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
