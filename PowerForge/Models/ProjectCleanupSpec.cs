namespace PowerForge;

/// <summary>
/// Typed specification for project file cleanup.
/// </summary>
public sealed class ProjectCleanupSpec
{
    /// <summary>Path to the project directory to clean.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Built-in cleanup profile used when <see cref="IncludePatterns"/> is empty.</summary>
    public ProjectCleanupType ProjectType { get; set; } = ProjectCleanupType.Build;

    /// <summary>
    /// Custom include patterns. When non-empty, these patterns are used instead of <see cref="ProjectType"/>.
    /// </summary>
    public string[] IncludePatterns { get; set; } = System.Array.Empty<string>();

    /// <summary>Patterns to exclude from deletion.</summary>
    public string[] ExcludePatterns { get; set; } = System.Array.Empty<string>();

    /// <summary>Directory names to completely exclude from processing.</summary>
    public string[] ExcludeDirectories { get; set; } = { ".git", ".svn", ".hg", "node_modules" };

    /// <summary>Deletion method.</summary>
    public ProjectDeleteMethod DeleteMethod { get; set; } = ProjectDeleteMethod.RemoveItem;

    /// <summary>When true, create backup copies of items before deletion.</summary>
    public bool CreateBackups { get; set; }

    /// <summary>Directory where backups should be stored (optional).</summary>
    public string? BackupDirectory { get; set; }

    /// <summary>Number of retry attempts for each deletion.</summary>
    public int Retries { get; set; } = 3;

    /// <summary>Whether to traverse subdirectories recursively.</summary>
    public bool Recurse { get; set; } = true;

    /// <summary>Maximum recursion depth. Default is unlimited (-1).</summary>
    public int MaxDepth { get; set; } = -1;

    /// <summary>When true, do not delete anything and mark results as <see cref="ProjectCleanupStatus.WhatIf"/>.</summary>
    public bool WhatIf { get; set; }
}

