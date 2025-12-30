namespace PowerForge;

/// <summary>
/// Result of executing a dotnet publish plan.
/// </summary>
public sealed class DotNetPublishResult
{
    /// <summary>True when the pipeline completed successfully.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Optional failure message.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Published artefacts.</summary>
    public DotNetPublishArtefactResult[] Artefacts { get; set; } = Array.Empty<DotNetPublishArtefactResult>();

    /// <summary>Path to the JSON manifest written by the pipeline (when enabled).</summary>
    public string? ManifestJsonPath { get; set; }

    /// <summary>Path to the text manifest written by the pipeline (when enabled).</summary>
    public string? ManifestTextPath { get; set; }
}

/// <summary>
/// Published artefact information for one target/runtime.
/// </summary>
public sealed class DotNetPublishArtefactResult
{
    /// <summary>Target name.</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>Target kind.</summary>
    public DotNetPublishTargetKind Kind { get; set; } = DotNetPublishTargetKind.Unknown;

    /// <summary>Runtime identifier.</summary>
    public string Runtime { get; set; } = string.Empty;

    /// <summary>Target framework used for publish.</summary>
    public string Framework { get; set; } = string.Empty;

    /// <summary>Publish style used.</summary>
    public DotNetPublishStyle Style { get; set; }

    /// <summary>Resolved publish directory (staging or final).</summary>
    public string PublishDir { get; set; } = string.Empty;

    /// <summary>Final output directory.</summary>
    public string OutputDir { get; set; } = string.Empty;

    /// <summary>Optional zip file path.</summary>
    public string? ZipPath { get; set; }

    /// <summary>Total number of files in <see cref="OutputDir"/>.</summary>
    public int Files { get; set; }

    /// <summary>Total bytes of all files in <see cref="OutputDir"/>.</summary>
    public long TotalBytes { get; set; }

    /// <summary>Path to the main executable (when detected).</summary>
    public string? ExePath { get; set; }

    /// <summary>Size of <see cref="ExePath"/> in bytes (when detected).</summary>
    public long? ExeBytes { get; set; }

    /// <summary>Cleanup stats (symbols/docs removed).</summary>
    public DotNetPublishCleanupResult Cleanup { get; set; } = new();
}

/// <summary>
/// Cleanup statistics for a published output.
/// </summary>
public sealed class DotNetPublishCleanupResult
{
    /// <summary>Number of .pdb files removed.</summary>
    public int PdbRemoved { get; set; }

    /// <summary>Number of documentation files removed.</summary>
    public int DocsRemoved { get; set; }

    /// <summary>True when the ref/ directory was removed.</summary>
    public bool RefPruned { get; set; }
}

