using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Reusable summary model for repository-wide .NET release results.
/// </summary>
public sealed class DotNetRepositoryReleaseSummary
{
    /// <summary>
    /// Gets or sets the per-project summary rows.
    /// </summary>
    public IReadOnlyList<DotNetRepositoryReleaseProjectSummaryRow> Projects { get; set; } =
        new List<DotNetRepositoryReleaseProjectSummaryRow>();

    /// <summary>
    /// Gets or sets the aggregate totals.
    /// </summary>
    public DotNetRepositoryReleaseSummaryTotals Totals { get; set; } = new();
}

/// <summary>
/// Reusable per-project summary row for repository releases.
/// </summary>
public sealed class DotNetRepositoryReleaseProjectSummaryRow
{
    /// <summary>
    /// Gets or sets the project name.
    /// </summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether the project was packable.
    /// </summary>
    public bool IsPackable { get; set; }

    /// <summary>
    /// Gets or sets the display version transition.
    /// </summary>
    public string VersionDisplay { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of produced packages.
    /// </summary>
    public int PackageCount { get; set; }

    /// <summary>
    /// Gets or sets the status classification used for summary rendering.
    /// </summary>
    public DotNetRepositoryReleaseProjectStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the raw project error message.
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the trimmed error preview used by compact summary renderers.
    /// </summary>
    public string ErrorPreview { get; set; } = string.Empty;
}

/// <summary>
/// Aggregate totals for repository release summaries.
/// </summary>
public sealed class DotNetRepositoryReleaseSummaryTotals
{
    /// <summary>
    /// Gets or sets the total project count.
    /// </summary>
    public int ProjectCount { get; set; }

    /// <summary>
    /// Gets or sets the packable project count.
    /// </summary>
    public int PackableCount { get; set; }

    /// <summary>
    /// Gets or sets the failed project count.
    /// </summary>
    public int FailedProjectCount { get; set; }

    /// <summary>
    /// Gets or sets the total produced package count.
    /// </summary>
    public int PackageCount { get; set; }

    /// <summary>
    /// Gets or sets the published package count.
    /// </summary>
    public int PublishedPackageCount { get; set; }

    /// <summary>
    /// Gets or sets the skipped-duplicate package count.
    /// </summary>
    public int SkippedDuplicatePackageCount { get; set; }

    /// <summary>
    /// Gets or sets the failed publish count.
    /// </summary>
    public int FailedPublishCount { get; set; }

    /// <summary>
    /// Gets or sets the resolved release version.
    /// </summary>
    public string ResolvedVersion { get; set; } = string.Empty;
}

/// <summary>
/// Status bucket used by repository release summaries.
/// </summary>
public enum DotNetRepositoryReleaseProjectStatus
{
    /// <summary>
    /// The project completed successfully.
    /// </summary>
    Ok = 0,

    /// <summary>
    /// The project was skipped because it is not packable.
    /// </summary>
    Skipped = 1,

    /// <summary>
    /// The project failed.
    /// </summary>
    Failed = 2
}
