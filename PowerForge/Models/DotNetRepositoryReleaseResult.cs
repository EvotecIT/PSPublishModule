using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Aggregate result for repository-wide .NET release operations.
/// </summary>
public sealed class DotNetRepositoryReleaseResult
{
    /// <summary>Whether the workflow completed without fatal errors.</summary>
    public bool Success { get; set; } = true;

    /// <summary>Optional error message for fatal failures.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Resolved version used for the release.</summary>
    public string? ResolvedVersion { get; set; }

    /// <summary>Project-level results.</summary>
    public List<DotNetRepositoryProjectResult> Projects { get; } = new();

    /// <summary>Packages pushed to the feed (if publishing).</summary>
    public List<string> PublishedPackages { get; } = new();
}

/// <summary>
/// Per-project outcome for repository releases.
/// </summary>
public sealed class DotNetRepositoryProjectResult
{
    /// <summary>Project name (csproj file name without extension).</summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>Resolved csproj path.</summary>
    public string CsprojPath { get; set; } = string.Empty;

    /// <summary>Whether the project is considered packable.</summary>
    public bool IsPackable { get; set; }

    /// <summary>Previous version detected in the project file.</summary>
    public string? OldVersion { get; set; }

    /// <summary>New version applied to the project file.</summary>
    public string? NewVersion { get; set; }

    /// <summary>Packages produced for this project.</summary>
    public List<string> Packages { get; } = new();

    /// <summary>Optional error message for the project.</summary>
    public string? ErrorMessage { get; set; }
}
