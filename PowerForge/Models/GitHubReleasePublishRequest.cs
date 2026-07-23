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

    /// <summary>GitHub REST API base URL, including the <c>/api/v3</c> prefix for GitHub Enterprise Server.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.github.com";

    /// <summary>Release tag name.</summary>
    public string TagName { get; set; } = string.Empty;

    /// <summary>Optional release title. When omitted, <see cref="TagName"/> is used.</summary>
    public string? ReleaseName { get; set; }

    /// <summary>
    /// Optional release notes body. GitHub prepends it when
    /// <see cref="GenerateReleaseNotes"/> is enabled.
    /// </summary>
    public string? ReleaseNotes { get; set; }

    /// <summary>Optional commitish to create the tag from.</summary>
    public string? Commitish { get; set; }

    /// <summary>
    /// True to ask GitHub to generate release notes automatically after any supplied
    /// <see cref="ReleaseNotes"/> body.
    /// </summary>
    public bool GenerateReleaseNotes { get; set; }

    /// <summary>True to create a draft release.</summary>
    public bool IsDraft { get; set; }

    /// <summary>True to mark the release as prerelease.</summary>
    public bool IsPreRelease { get; set; }

    /// <summary>True to reuse an existing release when GitHub reports a tag conflict.</summary>
    public bool ReuseExistingReleaseOnConflict { get; set; } = true;

    /// <summary>
    /// When true, a tag-conflict release may be reused only when its identifier matches
    /// <see cref="ExpectedExistingReleaseId"/>. This prevents mutating an unverified release.
    /// </summary>
    public bool RequireExpectedExistingRelease { get; set; }

    /// <summary>Preflight-verified release identifier permitted for idempotent reuse.</summary>
    public long? ExpectedExistingReleaseId { get; set; }

    /// <summary>
    /// Optional marker that must still be present in the release body immediately before assets are mutated.
    /// </summary>
    public string? ExpectedReleaseBodyMarker { get; set; }

    /// <summary>Optional commit SHA that the release tag must still resolve to immediately before assets are mutated.</summary>
    public string? ExpectedTagCommitSha { get; set; }

    /// <summary>True to delete same-named assets from a reused release before uploading new files.</summary>
    public bool ReplaceExistingAssets { get; set; }

    /// <summary>Asset file paths to upload.</summary>
    public IReadOnlyList<string> AssetFilePaths { get; set; } = System.Array.Empty<string>();
}
