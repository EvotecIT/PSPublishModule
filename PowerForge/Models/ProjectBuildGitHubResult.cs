namespace PowerForge;

/// <summary>
/// GitHub publish result per project.
/// </summary>
public sealed class ProjectBuildGitHubResult
{
    /// <summary>Project name.</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>GitHub repository owner.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>GitHub repository name.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>True when publishing succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Computed tag name.</summary>
    public string? TagName { get; set; }

    /// <summary>Numeric GitHub release identifier returned by the API.</summary>
    public long ReleaseId { get; set; }

    /// <summary>Release URL when publishing succeeded.</summary>
    public string? ReleaseUrl { get; set; }

    /// <summary>Error message when publishing failed.</summary>
    public string? ErrorMessage { get; set; }
}
