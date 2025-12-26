namespace PowerForge;

/// <summary>
/// Output object returned by <see cref="ProjectCleanupService"/> when detailed results are requested.
/// </summary>
public sealed class ProjectCleanupOutput
{
    /// <summary>Summary object.</summary>
    public ProjectCleanupSummary Summary { get; set; } = new();

    /// <summary>Per-item results.</summary>
    public ProjectCleanupItemResult[] Results { get; set; } = System.Array.Empty<ProjectCleanupItemResult>();
}

