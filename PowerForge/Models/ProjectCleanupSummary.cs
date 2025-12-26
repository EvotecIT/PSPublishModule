namespace PowerForge;

/// <summary>
/// Summary returned by <see cref="ProjectCleanupService"/> when producing detailed output.
/// </summary>
public sealed class ProjectCleanupSummary
{
    /// <summary>Project path.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Cleanup type (built-in profile name or "Custom").</summary>
    public string ProjectType { get; set; } = string.Empty;

    /// <summary>Total items processed.</summary>
    public int TotalItems { get; set; }

    /// <summary>Removed items count.</summary>
    public int Removed { get; set; }

    /// <summary>Error items count.</summary>
    public int Errors { get; set; }

    /// <summary>Space freed in bytes (Removed items only).</summary>
    public long SpaceFreed { get; set; }

    /// <summary>Space freed in MB (Removed items only).</summary>
    public double SpaceFreedMB { get; set; }

    /// <summary>Backup directory when enabled.</summary>
    public string? BackupDirectory { get; set; }

    /// <summary>Delete method.</summary>
    public string DeleteMethod { get; set; } = string.Empty;
}

