namespace PowerForge;

/// <summary>
/// Describes a project-version discovery request.
/// </summary>
public sealed class ProjectVersionQueryRequest
{
    /// <summary>
    /// Gets or sets the repository root to scan.
    /// </summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional module name used to filter <c>.csproj</c> and <c>.psd1</c> files.
    /// </summary>
    public string? ModuleName { get; set; }

    /// <summary>
    /// Gets or sets optional path fragments that should be excluded from scanning.
    /// </summary>
    public string[]? ExcludeFolders { get; set; }
}
