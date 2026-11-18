using System;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Linq;
using System.Threading.Tasks;
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
public sealed class SendGitHubReleaseCommand : AsyncPSCmdlet
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
    /// When true, a 422 tag conflict deliberately reuses the existing release.
    /// The default is false so normal publishing cannot mutate an older release.
    /// </summary>
    [Parameter]
    public bool ReuseExistingReleaseOnConflict { get; set; }

    /// <summary>Creates the release and uploads assets.</summary>
    protected override async Task ProcessRecordAsync()
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
            // The upload runs off the pipeline thread so byte progress and log lines reach the host while it runs.
            var request = new GitHubReleasePublishRequest
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
                AssetFilePaths = assets,
                Progress = new CmdletAssetProgress(this)
            };
            var publishResult = await Task.Run(() => publisher.PublishRelease(request, CancelToken), CancelToken);

            result.ReleaseCreationSucceeded = publishResult.ReleaseCreationSucceeded;
            result.ReusedExistingRelease = publishResult.ReusedExistingRelease;
            result.ReleaseUrl = publishResult.HtmlUrl;
            result.AllAssetUploadsSucceeded = publishResult.AllAssetUploadsSucceeded;
            result.SkippedExistingAssets.AddRange(publishResult.SkippedExistingAssets);
            result.Succeeded = publishResult.Succeeded;
            if (!result.Succeeded)
                result.ErrorMessage = GitHubReleaseAssetWorkflowService.DescribeFailure(TagName, assets.Length, publishResult);
            WriteObject(result);
            if (!result.Succeeded)
            {
                WriteError(new ErrorRecord(
                    new InvalidOperationException(result.ErrorMessage),
                    "SendGitHubReleaseIncomplete",
                    ErrorCategory.InvalidResult,
                    TagName));
            }
        }
        catch (Exception ex) when (ex is not PipelineStoppedException and not ActionPreferenceStopException and not OperationCanceledException)
        {
            if (!result.ReleaseCreationSucceeded)
                result.ReleaseCreationSucceeded = false;

            result.Succeeded = false;
            result.AllAssetUploadsSucceeded = thereAreNoAssetsToInclude ? null : false;
            result.ErrorMessage = ex.Message;
            WriteObject(result);
            WriteError(new ErrorRecord(ex, "SendGitHubReleaseFailed", ErrorCategory.NotSpecified, TagName));
        }
    }

    /// <summary>
    /// Shows asset uploads with Write-Progress: one bar for the asset plan, with the bytes of the current upload.
    /// Byte updates are rate-limited; state changes are always shown.
    /// </summary>
    private sealed class CmdletAssetProgress : IGitHubReleaseProgressReporter
    {
        private const int ActivityId = 0x6748;
        private static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(250);
        private readonly AsyncPSCmdlet _cmdlet;
        private readonly object _gate = new();
        private DateTime _lastUpdate = DateTime.MinValue;

        internal CmdletAssetProgress(AsyncPSCmdlet cmdlet) => _cmdlet = cmdlet;

        public void Report(GitHubReleaseAssetProgress progress)
        {
            if (progress is null || progress.State == GitHubReleaseAssetProgressState.Planned)
                return;

            lock (_gate)
            {
                var now = DateTime.UtcNow;
                if (progress.State == GitHubReleaseAssetProgressState.Uploading &&
                    progress.BytesTransferred < progress.TotalBytes &&
                    now - _lastUpdate < MinimumInterval)
                {
                    return;
                }

                _lastUpdate = now;
                var status = $"Asset {progress.Position}/{progress.TotalAssets}: {progress.FileName} ({Describe(progress)})";
                var record = new ProgressRecord(ActivityId, "Uploading GitHub release assets", status);
                if (progress.TotalAssets > 0)
                {
                    var done = progress.State is GitHubReleaseAssetProgressState.Uploaded
                        or GitHubReleaseAssetProgressState.Skipped
                        or GitHubReleaseAssetProgressState.Failed
                        ? 1d
                        : progress.TotalBytes > 0 ? (double)progress.BytesTransferred / progress.TotalBytes : 0d;
                    record.PercentComplete = (int)Math.Min(100, Math.Max(0, (progress.Position - 1 + done) * 100 / progress.TotalAssets));
                }

                if (progress.Position >= progress.TotalAssets &&
                    progress.State is not GitHubReleaseAssetProgressState.Uploading and not GitHubReleaseAssetProgressState.Replacing)
                {
                    record.RecordType = ProgressRecordType.Completed;
                }

                try { _cmdlet.WriteProgress(record); } catch (Exception ex) when (ex is not PipelineStoppedException) { }
            }
        }

        private static string Describe(GitHubReleaseAssetProgress progress)
            => progress.State switch
            {
                GitHubReleaseAssetProgressState.Uploading when progress.TotalBytes > 0 =>
                    $"{DotNetRepositoryReleaseService.FormatBytes(Math.Max(0, progress.BytesTransferred))} / {DotNetRepositoryReleaseService.FormatBytes(progress.TotalBytes)}",
                GitHubReleaseAssetProgressState.Replacing => "replacing existing asset",
                GitHubReleaseAssetProgressState.Skipped => "already exists",
                GitHubReleaseAssetProgressState.Failed => string.IsNullOrWhiteSpace(progress.Detail) ? "failed" : $"failed: {progress.Detail}",
                _ => progress.State.ToString().ToLowerInvariant()
            };
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
