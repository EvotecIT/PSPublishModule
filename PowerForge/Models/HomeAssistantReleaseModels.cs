using System;
using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Semantic version increment requested for a Home Assistant or HACS release.
/// </summary>
public enum HomeAssistantVersionIncrement {
    /// <summary>Do not publish a release.</summary>
    None,

    /// <summary>Increment the patch component.</summary>
    Patch,

    /// <summary>Increment the minor component and reset the patch component.</summary>
    Minor,

    /// <summary>Increment the major component and reset the minor and patch components.</summary>
    Major
}


/// <summary>
/// Supported Home Assistant Community Store repository layouts.
/// </summary>
public enum HomeAssistantRepositoryKind {
    /// <summary>The repository layout has not been resolved.</summary>
    Unknown,

    /// <summary>A Home Assistant custom integration under <c>custom_components</c>.</summary>
    Integration,

    /// <summary>A HACS Lovelace plugin backed by an npm project.</summary>
    LovelacePlugin
}

/// <summary>
/// Describes the outcome of a Home Assistant release run.
/// </summary>
public enum HomeAssistantReleaseAction {
    /// <summary>No release was required by policy.</summary>
    None,

    /// <summary>A release was planned without changing the repository.</summary>
    Planned,

    /// <summary>Release metadata was committed and pushed for an isolated build.</summary>
    Prepared,

    /// <summary>Release assets were built from the exact prepared commit.</summary>
    Built,

    /// <summary>A new release was published.</summary>
    Published,

    /// <summary>An existing release for the source pull request was verified and reused.</summary>
    Reused
}

/// <summary>
/// Input for planning or preparing a PowerForge Home Assistant release.
/// </summary>
public sealed class HomeAssistantReleasePrepareSpec {
    /// <summary>Local checkout containing the Home Assistant integration or HACS plugin.</summary>
    public string RepositoryRoot { get; set; } = string.Empty;

    /// <summary>GitHub repository owner.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>GitHub repository name.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>GitHub token used to inspect the merged pull request and push release metadata.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Number of the merged pull request that initiated the release run.</summary>
    public int PullRequestNumber { get; set; }

    /// <summary>Expected merge commit SHA supplied by the workflow event.</summary>
    public string? MergeCommitSha { get; set; }

    /// <summary>Default branch to receive the version-only release commit.</summary>
    public string DefaultBranch { get; set; } = "main";

    /// <summary>Optional explicit increment, normally inferred from release labels and changed files.</summary>
    public HomeAssistantVersionIncrement? Increment { get; set; }

    /// <summary>When true, update metadata, commit, and push the release version.</summary>
    public bool Apply { get; set; }

    /// <summary>Optional GitHub API base address used by GitHub Enterprise or contract tests.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.github.com";

    /// <summary>GitHub server address used to pin the authenticated push destination.</summary>
    public string ServerUrl { get; set; } = "https://github.com";
}

/// <summary>
/// Input for building Home Assistant release assets without write credentials.
/// </summary>
public sealed class HomeAssistantReleaseBuildSpec {
    /// <summary>Checkout rooted at the exact prepared release commit.</summary>
    public string RepositoryRoot { get; set; } = string.Empty;

    /// <summary>Expected semantic version in repository metadata.</summary>
    public string ReleaseVersion { get; set; } = string.Empty;

    /// <summary>Expected checkout commit SHA.</summary>
    public string ReleaseCommitSha { get; set; } = string.Empty;
}

/// <summary>
/// Input for publishing a previously prepared and independently built release.
/// </summary>
public sealed class HomeAssistantReleasePublishSpec {
    /// <summary>GitHub repository owner.</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>GitHub repository name.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>GitHub token used only for release publication and verification.</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Number of the merged pull request that initiated the release.</summary>
    public int PullRequestNumber { get; set; }

    /// <summary>Expected merge commit SHA supplied by the workflow event.</summary>
    public string MergeCommitSha { get; set; } = string.Empty;

    /// <summary>Prepared semantic release version.</summary>
    public string ReleaseVersion { get; set; } = string.Empty;

    /// <summary>Prepared immutable release commit.</summary>
    public string ReleaseCommitSha { get; set; } = string.Empty;

    /// <summary>Required HACS asset name, or empty when the release has no binary asset.</summary>
    public string RequiredAssetName { get; set; } = string.Empty;

    /// <summary>Downloaded asset path matching <see cref="RequiredAssetName"/>, when required.</summary>
    public string AssetFilePath { get; set; } = string.Empty;

    /// <summary>Optional GitHub API base address used by GitHub Enterprise or contract tests.</summary>
    public string ApiBaseUrl { get; set; } = "https://api.github.com";
}

/// <summary>
/// Result returned by the PowerForge Home Assistant release orchestrator.
/// </summary>
public sealed class HomeAssistantReleaseResult {
    /// <summary>True when the requested work completed successfully.</summary>
    public bool Success { get; set; }

    /// <summary>Action taken by the release run.</summary>
    public HomeAssistantReleaseAction Action { get; set; }

    /// <summary>Detected repository layout.</summary>
    public HomeAssistantRepositoryKind RepositoryKind { get; set; }

    /// <summary>Resolved semantic version increment.</summary>
    public HomeAssistantVersionIncrement Increment { get; set; }

    /// <summary>Version found in repository metadata before the run.</summary>
    public string CurrentVersion { get; set; } = string.Empty;

    /// <summary>Version selected for the release.</summary>
    public string ReleaseVersion { get; set; } = string.Empty;

    /// <summary>Release tag, including the <c>v</c> prefix.</summary>
    public string TagName { get; set; } = string.Empty;

    /// <summary>Commit containing the synchronized release version.</summary>
    public string? ReleaseCommitSha { get; set; }

    /// <summary>GitHub URL for a published or reused release.</summary>
    public string? ReleaseUrl { get; set; }

    /// <summary>Required HACS asset name, or empty when no release asset is required.</summary>
    public string RequiredAssetName { get; set; } = string.Empty;

    /// <summary>Human-readable policy or recovery explanation.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Version metadata files changed by the run.</summary>
    public List<string> ChangedFiles { get; } = new();

    /// <summary>Release assets built and uploaded by the run.</summary>
    public List<string> AssetFiles { get; } = new();
}
