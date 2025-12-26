namespace PowerForge;

/// <summary>
/// Single item cleanup result.
/// </summary>
public sealed class ProjectCleanupItemResult
{
    /// <summary>Path relative to the project root.</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Absolute path.</summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>Item type.</summary>
    public ProjectCleanupItemType Type { get; set; }

    /// <summary>Matched pattern.</summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>Status.</summary>
    public ProjectCleanupStatus Status { get; set; }

    /// <summary>File size in bytes (0 for folders).</summary>
    public long Size { get; set; }

    /// <summary>Error message when applicable.</summary>
    public string? Error { get; set; }
}

