namespace PowerForge;

/// <summary>
/// Request for applying generic bundle post-process rules to an existing bundle directory.
/// </summary>
public sealed class PowerForgeBundlePostProcessRequest
{
    /// <summary>Project root used for path-safety checks.</summary>
    public string ProjectRoot { get; set; } = string.Empty;

    /// <summary>When true, bundle paths may resolve outside <see cref="ProjectRoot"/>.</summary>
    public bool AllowOutputOutsideProjectRoot { get; set; }

    /// <summary>Existing bundle root that will be post-processed.</summary>
    public string BundleRoot { get; set; } = string.Empty;

    /// <summary>Optional bundle identifier used for metadata and templates.</summary>
    public string? BundleId { get; set; }

    /// <summary>Optional source target name.</summary>
    public string? TargetName { get; set; }

    /// <summary>Optional runtime identifier.</summary>
    public string? Runtime { get; set; }

    /// <summary>Optional target framework.</summary>
    public string? Framework { get; set; }

    /// <summary>Optional publish style label.</summary>
    public string? Style { get; set; }

    /// <summary>Optional build configuration label.</summary>
    public string? Configuration { get; set; }

    /// <summary>Optional zip path token value.</summary>
    public string? ZipPath { get; set; }

    /// <summary>Optional source output token value.</summary>
    public string? SourceOutputPath { get; set; }

    /// <summary>Post-process rules to apply.</summary>
    public DotNetPublishBundlePostProcessOptions? PostProcess { get; set; }

    /// <summary>When true, skips archive-directory rules from the config.</summary>
    public bool SkipArchiveDirectories { get; set; }

    /// <summary>When true, skips metadata emission from the config.</summary>
    public bool SkipMetadata { get; set; }

    /// <summary>Optional additional delete patterns appended to config-defined post-process rules.</summary>
    public string[] AdditionalDeletePatterns { get; set; } = Array.Empty<string>();

    /// <summary>Optional additional template tokens.</summary>
    public Dictionary<string, string>? Tokens { get; set; }
}

/// <summary>
/// Result of bundle post-processing.
/// </summary>
public sealed class PowerForgeBundlePostProcessResult
{
    /// <summary>Resolved bundle root.</summary>
    public string BundleRoot { get; set; } = string.Empty;

    /// <summary>UTC timestamp used for metadata generation.</summary>
    public string CreatedUtc { get; set; } = string.Empty;

    /// <summary>Archive files created by the post-process step.</summary>
    public string[] ArchivePaths { get; set; } = Array.Empty<string>();

    /// <summary>Deleted files or directories.</summary>
    public string[] DeletedPaths { get; set; } = Array.Empty<string>();

    /// <summary>Optional metadata file written into the bundle.</summary>
    public string? MetadataPath { get; set; }
}
