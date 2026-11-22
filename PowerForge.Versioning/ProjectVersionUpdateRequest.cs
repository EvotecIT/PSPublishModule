namespace PowerForge;

/// <summary>
/// Describes a project-version update request.
/// </summary>
public sealed class ProjectVersionUpdateRequest
{
    /// <summary>
    /// Gets or sets the repository root to scan.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional module name used to filter <c>.csproj</c> and <c>.psd1</c> targets.
    /// </summary>
    public string? ModuleName { get; set; }

    /// <summary>
    /// Gets or sets optional path fragments that should be excluded from scanning.
    /// </summary>
    public string[]? ExcludeFolders { get; set; }

    /// <summary>
    /// Gets or sets a specific version to apply.
    /// </summary>
    public string? NewVersion { get; set; }

    /// <summary>
    /// Gets or sets the increment kind used when <see cref="NewVersion"/> is not provided.
    /// </summary>
    public ProjectVersionIncrementKind? IncrementKind { get; set; }
}
