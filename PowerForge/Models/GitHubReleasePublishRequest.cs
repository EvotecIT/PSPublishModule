using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Request describing a GitHub release publish operation.
/// </summary>
public sealed class GitHubReleasePublishRequest
{
    /// <summary>Repository owner or organization.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>Repository name.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>GitHub personal access token.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Release tag name.</summary>
    public string TagName { get; set; } = string.Empty;

    /// <summary>Optional release title. When omitted, <see cref="TagName"/> is used.</summary>
    public string? ReleaseName { get; set; }

    /// <summary>Optional release notes body.</summary>
    public string? ReleaseNotes { get; set; }

    /// <summary>Optional commitish to create the tag from.</summary>
    public string? Commitish { get; set; }

    /// <summary>True to ask GitHub to generate release notes automatically.</summary>
    public bool GenerateReleaseNotes { get; set; }

    /// <summary>True to create a draft release.</summary>
    public bool IsDraft { get; set; }

    /// <summary>True to mark the release as prerelease.</summary>
    public bool IsPreRelease { get; set; }

    /// <summary>True to reuse an existing release when GitHub reports a tag conflict.</summary>
    public bool ReuseExistingReleaseOnConflict { get; set; } = true;

    /// <summary>Asset file paths to upload.</summary>
    public IReadOnlyList<string> AssetFilePaths { get; set; } = System.Array.Empty<string>();
}
