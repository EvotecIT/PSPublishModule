namespace PowerForge;

/// <summary>
/// GitHub publish result per project.
/// </summary>
public sealed class ProjectBuildGitHubResult
{
    /// <summary>Project name.</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>True when publishing succeeded.</summary>
    public bool Success { get; set; }

    /// <summary>Computed tag name.</summary>
    public string? TagName { get; set; }

    /// <summary>Release URL when publishing succeeded.</summary>
    public string? ReleaseUrl { get; set; }

    /// <summary>Error message when publishing failed.</summary>
    public string? ErrorMessage { get; set; }
}
