namespace PowerForge;

/// <summary>State of one GitHub release asset operation.</summary>
public enum GitHubReleaseAssetProgressState
{
    /// <summary>The asset is planned but has not started.</summary>
    Planned,
    /// <summary>An existing asset is being removed before replacement.</summary>
    Replacing,
    /// <summary>The asset bytes are being uploaded.</summary>
    Uploading,
    /// <summary>The asset upload completed successfully.</summary>
    Uploaded,
    /// <summary>The asset already existed and was skipped.</summary>
    Skipped,
    /// <summary>The asset operation failed.</summary>
    Failed
}

/// <summary>Progress snapshot for one GitHub release asset.</summary>
public sealed class GitHubReleaseAssetProgress
{
    /// <summary>Full source path of the asset.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>File name used on the GitHub release.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>One-based position within the release asset plan.</summary>
    public int Position { get; set; }

    /// <summary>Total number of assets in the release plan.</summary>
    public int TotalAssets { get; set; }

    /// <summary>Current operation state.</summary>
    public GitHubReleaseAssetProgressState State { get; set; }

    /// <summary>Number of bytes transferred for the current upload.</summary>
    public long BytesTransferred { get; set; }

    /// <summary>Total asset size in bytes.</summary>
    public long TotalBytes { get; set; }

    /// <summary>Optional failure or status detail.</summary>
    public string? Detail { get; set; }
}

/// <summary>Receives structured progress from GitHub release asset operations.</summary>
public interface IGitHubReleaseProgressReporter
{
    /// <summary>Reports the latest state of one release asset.</summary>
    void Report(GitHubReleaseAssetProgress progress);
}
