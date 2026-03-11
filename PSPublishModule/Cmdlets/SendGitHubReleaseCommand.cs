using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Linq;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a new release for the given GitHub repository and optionally uploads assets.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet uses the GitHub REST API to create a release and upload assets. It is a lower-level building block used by
/// higher-level helpers (such as <c>Publish-GitHubReleaseAsset</c>) and can also be used directly in CI pipelines.
/// </para>
/// <para>
/// Provide the token via an environment variable to avoid leaking secrets into logs or history.
/// </para>
/// </remarks>
/// <example>
/// <summary>Create a release and upload a ZIP asset</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Send-GitHubRelease -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'MyProject' -GitHubAccessToken $env:GITHUB_TOKEN -TagName 'v1.2.3' -ReleaseNotes 'Bug fixes' -AssetFilePaths 'C:\Artifacts\MyProject.zip'</code>
/// <para>Creates the release and uploads the specified asset file.</para>
/// </example>
/// <example>
/// <summary>Create a prerelease as a draft</summary>
/// <prefix>PS&gt; </prefix>
/// <code>Send-GitHubRelease -GitHubUsername 'EvotecIT' -GitHubRepositoryName 'MyProject' -GitHubAccessToken $env:GITHUB_TOKEN -TagName 'v1.2.3-preview.1' -IsDraft $true -IsPreRelease $true</code>
/// <para>Creates a draft prerelease that can be reviewed before publishing.</para>
/// </example>
[Cmdlet(VerbsCommunications.Send, "GitHubRelease")]
public sealed class SendGitHubReleaseCommand : PSCmdlet
{
    /// <summary>GitHub username owning the repository.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string GitHubUsername { get; set; } = string.Empty;

    /// <summary>GitHub repository name.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string GitHubRepositoryName { get; set; } = string.Empty;

    /// <summary>GitHub personal access token used for authentication.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string GitHubAccessToken { get; set; } = string.Empty;

    /// <summary>The tag name used for the release.</summary>
    [Parameter(Mandatory = true)]
    [ValidateNotNullOrEmpty]
    public string TagName { get; set; } = string.Empty;

    /// <summary>The name of the release. If omitted, TagName is used.</summary>
    [Parameter]
    public string? ReleaseName { get; set; }

    /// <summary>The text describing the contents of the release.</summary>
    [Parameter]
    public string? ReleaseNotes { get; set; }

    /// <summary>When set, asks GitHub to generate release notes automatically (cannot be used with ReleaseNotes).</summary>
    [Parameter]
    public SwitchParameter GenerateReleaseNotes { get; set; }

    /// <summary>The full paths of the files to include as release assets.</summary>
    [Parameter]
    public string[]? AssetFilePaths { get; set; }

    /// <summary>Commitish value that determines where the Git tag is created from.</summary>
    [Parameter]
    public string? Commitish { get; set; }

    /// <summary>True to create a draft (unpublished) release.</summary>
    [Parameter]
    public bool IsDraft { get; set; }

    /// <summary>True to identify the release as a prerelease.</summary>
    [Parameter]
    public bool IsPreRelease { get; set; }

    /// <summary>
    /// When true (default), a 422 tag conflict reuses the existing release.
    /// When false, the cmdlet fails on existing tags.
    /// </summary>
    [Parameter]
    public bool ReuseExistingReleaseOnConflict { get; set; } = true;

    /// <summary>Creates the release and uploads assets.</summary>
    protected override void ProcessRecord()
    {
        var isVerbose = MyInvocation?.BoundParameters.ContainsKey("Verbose") == true;
        var logger = new CmdletLogger(this, isVerbose);
        var publisher = new GitHubReleasePublisher(logger);
        var result = new GitHubReleaseResult
        {
            Succeeded = false,
            ReleaseCreationSucceeded = false,
            AllAssetUploadsSucceeded = false,
            ReleaseUrl = null,
            ErrorMessage = null
        };

        var assets = (AssetFilePaths ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        var thereAreNoAssetsToInclude = assets.Length == 0;
        if (thereAreNoAssetsToInclude)
        {
            result.AllAssetUploadsSucceeded = null;
        }

        if (string.IsNullOrWhiteSpace(ReleaseName))
        {
            ReleaseName = TagName;
        }

        ValidateAssetFilePathsOrThrow(assets);

        if (GenerateReleaseNotes.IsPresent && !string.IsNullOrWhiteSpace(ReleaseNotes))
        {
            throw new PSArgumentException($"{nameof(ReleaseNotes)} cannot be used when {nameof(GenerateReleaseNotes)} is set.");
        }

        try
        {
            var publishResult = publisher.PublishRelease(new GitHubReleasePublishRequest
            {
                Owner = GitHubUsername,
                Repository = GitHubRepositoryName,
                Token = GitHubAccessToken,
                TagName = TagName,
                ReleaseName = ReleaseName,
                ReleaseNotes = ReleaseNotes,
                Commitish = Commitish,
                GenerateReleaseNotes = GenerateReleaseNotes.IsPresent,
                IsDraft = IsDraft,
                IsPreRelease = IsPreRelease,
                ReuseExistingReleaseOnConflict = ReuseExistingReleaseOnConflict,
                AssetFilePaths = assets
            });

            result.ReleaseCreationSucceeded = publishResult.ReleaseCreationSucceeded;
            result.ReusedExistingRelease = publishResult.ReusedExistingRelease;
            result.ReleaseUrl = publishResult.HtmlUrl;
            result.AllAssetUploadsSucceeded = publishResult.AllAssetUploadsSucceeded;
            result.SkippedExistingAssets.AddRange(publishResult.SkippedExistingAssets);
            result.Succeeded = publishResult.Succeeded;
            WriteObject(result);
        }
        catch (Exception ex)
        {
            if (!result.ReleaseCreationSucceeded)
                result.ReleaseCreationSucceeded = false;

            result.Succeeded = false;
            result.AllAssetUploadsSucceeded = thereAreNoAssetsToInclude ? null : false;
            result.ErrorMessage = ex.Message;
            WriteObject(result);
        }
    }

    private static void ValidateAssetFilePathsOrThrow(string[] assets)
    {
        foreach (var filePath in assets)
        {
            var missing = string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath);
            if (missing)
            {
                throw new InvalidOperationException($"There is no file at the specified path, '{filePath}'.");
            }
        }
    }

    /// <summary>
    /// Result returned by <c>Send-GitHubRelease</c>.
    /// </summary>
    public sealed class GitHubReleaseResult
    {
        /// <summary>True if the release was created successfully and all assets were uploaded.</summary>
        public bool Succeeded { get; set; }

        /// <summary>True if the release was created successfully (does not include asset uploads).</summary>
        public bool ReleaseCreationSucceeded { get; set; }

        /// <summary>True if all assets were uploaded; false if any failed; null if no assets were provided.</summary>
        public bool? AllAssetUploadsSucceeded { get; set; }

        /// <summary>The URL of the created release.</summary>
        public string? ReleaseUrl { get; set; }

        /// <summary>True when an existing release/tag was reused instead of creating a new release.</summary>
        public bool ReusedExistingRelease { get; set; }

        /// <summary>Assets skipped because they already existed on the release.</summary>
        public List<string> SkippedExistingAssets { get; } = new();

        /// <summary>Error message describing what went wrong when <see cref="Succeeded"/> is false.</summary>
        public string? ErrorMessage { get; set; }
    }
}
