using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Result describing a GitHub release publish operation.
/// </summary>
public sealed class GitHubReleasePublishResult
{
    /// <summary>True when the release work completed successfully.</summary>
    public bool Succeeded { get; set; }

    /// <summary>True when release creation or reuse succeeded.</summary>
    public bool ReleaseCreationSucceeded { get; set; }

    /// <summary>True when all asset uploads succeeded; null when no assets were requested.</summary>
    public bool? AllAssetUploadsSucceeded { get; set; }

    /// <summary>GitHub HTML URL for the release.</summary>
    public string? HtmlUrl { get; set; }

    /// <summary>GitHub upload URL returned by the API.</summary>
    public string? UploadUrl { get; set; }

    /// <summary>True when an existing release was reused instead of creating a new one.</summary>
    public bool ReusedExistingRelease { get; set; }

    /// <summary>Assets skipped because they already existed on the release.</summary>
    public List<string> SkippedExistingAssets { get; } = new();
}
