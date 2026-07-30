using System.Collections.ObjectModel;

namespace PowerForge;

/// <summary>
/// In-memory token that binds a fresh benchmark output directory to source state captured before measurement.
/// </summary>
public sealed class BenchmarkProvenanceCaptureSession : IDisposable
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

    /// <summary>Workload metadata declared before measurement and bound into the completed sidecar.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; internal set; } =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Run mode declared before measurement, such as <c>quick</c> or <c>full</c>.</summary>
    public string RunMode { get; internal set; } = string.Empty;

    internal IDisposable? ArtifactRootLease { get; set; }

    /// <summary>Releases the exclusive artifact-root reservation.</summary>
    public void Dispose()
    {
        ArtifactRootLease?.Dispose();
        ArtifactRootLease = null;
    }
}

internal sealed class BenchmarkArtifactProvenanceDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string SourceCommit { get; set; } = string.Empty;
    public string SourceBranch { get; set; } = string.Empty;
    public bool GitWorktreeClean { get; set; }
    public DateTimeOffset StartedUtc { get; set; }
    public DateTimeOffset FinishedUtc { get; set; }
    public Dictionary<string, string> Metadata { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public string RunMode { get; set; } = string.Empty;
    public BenchmarkProducedArtifact[] Artifacts { get; set; } = Array.Empty<BenchmarkProducedArtifact>();
}

internal sealed class BenchmarkProducedArtifact
{
    public string Path { get; set; } = string.Empty;
    public long Length { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}
