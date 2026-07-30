namespace PowerForge;

/// <summary>
/// In-memory token that binds a fresh benchmark output directory to source state captured before measurement.
/// </summary>
public sealed class BenchmarkProvenanceCaptureSession
{
    /// <summary>Repository root whose source state is being measured.</summary>
    public string SourceRoot { get; internal set; } = string.Empty;

    /// <summary>Fresh output directory reserved for this measurement.</summary>
    public string ArtifactRoot { get; internal set; } = string.Empty;

    /// <summary>UTC time at which the capture began.</summary>
    public DateTimeOffset StartedUtc { get; internal set; }

    /// <summary>Exact source commit captured before measurement.</summary>
    public string SourceCommit { get; internal set; } = string.Empty;

    /// <summary>Source branch captured before measurement.</summary>
    public string SourceBranch { get; internal set; } = string.Empty;
}

internal sealed class BenchmarkArtifactProvenanceDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string SourceCommit { get; set; } = string.Empty;
    public string SourceBranch { get; set; } = string.Empty;
    public bool GitWorktreeClean { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset FinishedUtc { get; set; }
    public BenchmarkProducedArtifact[] Artifacts { get; set; } = Array.Empty<BenchmarkProducedArtifact>();
}

internal sealed class BenchmarkProducedArtifact
{
    public string Path { get; set; } = string.Empty;
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}
