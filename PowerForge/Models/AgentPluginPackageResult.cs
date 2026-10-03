namespace PowerForge;

/// <summary>Validated portable agent-plugin identity and optional archive evidence.</summary>
public sealed class AgentPluginPackageResult
{
    /// <summary>Absolute source directory.</summary>
    public string SourcePath { get; internal set; } = string.Empty;
    /// <summary>Portable plugin name.</summary>
    public string Name { get; internal set; } = string.Empty;
    /// <summary>Three-part semantic package version.</summary>
    public string Version { get; internal set; } = string.Empty;
    /// <summary>Number of files in the generated package.</summary>
    public int FileCount { get; internal set; }
    /// <summary>Absolute archive path when packed.</summary>
    public string? ArchivePath { get; internal set; }
    /// <summary>SHA-256 of the archive when packed.</summary>
    public string? Sha256 { get; internal set; }
}
