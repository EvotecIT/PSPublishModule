using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace PowerForge;

/// <summary>
/// Coordinates version policy, metadata synchronization, artifact creation, Git operations,
/// GitHub release publication, and idempotent recovery for Home Assistant and HACS repositories.
/// </summary>
public sealed class HomeAssistantReleaseService {
    private readonly ILogger _logger;
    private readonly HomeAssistantRepositoryService _repository;
    private readonly HomeAssistantReleaseGitService _git;
    private readonly IHomeAssistantGitHubClient? _github;
    private readonly IHomeAssistantReleasePublisher? _publisher;

    /// <summary>
    /// Creates a Home Assistant release service using production GitHub, git, and process clients.
    /// </summary>
    /// <param name="logger">Logger receiving progress and recovery messages.</param>
    public HomeAssistantReleaseService(ILogger logger)
        : this(logger, new HomeAssistantRepositoryService(), new HomeAssistantReleaseGitService(), null, null) {
    }

    internal HomeAssistantReleaseService(
        ILogger logger,
        HomeAssistantRepositoryService repository,
        HomeAssistantReleaseGitService git,
        IHomeAssistantGitHubClient? github,
        IHomeAssistantReleasePublisher? publisher) {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _git = git ?? throw new ArgumentNullException(nameof(git));
        _github = github;
        _publisher = publisher;
    }

    /// <summary>
    /// Plans or executes a release initiated by a merged pull request.
    /// </summary>
    /// <param name="spec">Repository, pull request, policy, and publication settings.</param>
    /// <returns>A structured release result suitable for CLI JSON output and workflow summaries.</returns>
    public HomeAssistantReleaseResult Run(HomeAssistantReleaseSpec spec) {
        Validate(spec);
        var root = Path.GetFullPath(spec.RepositoryRoot);
        var snapshot = _repository.Inspect(root);
        var github = _github ?? new HomeAssistantGitHubClient(spec.Owner, spec.Repository, spec.Token, spec.ApiBaseUrl);
        var publisher = _publisher ?? new HomeAssistantReleasePublisher(_logger);
        var pullRequest = github.GetPullRequest(spec.PullRequestNumber);

        if (!pullRequest.Merged)
            throw new InvalidOperationException($"Pull request #{pullRequest.Number} is not merged.");
        if (!string.IsNullOrWhiteSpace(spec.MergeCommitSha) &&
            !string.Equals(spec.MergeCommitSha, pullRequest.MergeCommitSha, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Workflow merge SHA '{spec.MergeCommitSha}' does not match GitHub pull request merge SHA '{pullRequest.MergeCommitSha}'.");
        }

        var mergeCommitSha = pullRequest.MergeCommitSha;
        var marker = HomeAssistantReleasePolicy.BuildMarker(pullRequest.Number, mergeCommitSha);
        var existingSourceRelease = github.FindReleaseByMarker(marker);
        HomeAssistantGitHubRelease? incompleteSourceRelease = null;
        if (existingSourceRelease is not null) {
            try {
                VerifyExistingRelease(github, existingSourceRelease, snapshot, expectedCommitSha: null, marker);
                return CreateResult(
                    snapshot,
                    HomeAssistantReleaseAction.Reused,
                    HomeAssistantVersionIncrement.None,
                    snapshot.Version,
                    existingSourceRelease.TagName,
                    github.GetTagCommitSha(existingSourceRelease.TagName),
                    existingSourceRelease.HtmlUrl,
                    "The source pull request already has a verified PowerForge release.");
            } catch (HomeAssistantReleaseAssetException ex) when (spec.Apply && spec.Publish) {
                incompleteSourceRelease = existingSourceRelease;
                _logger.Warn($"{ex.Message} PowerForge will rebuild and replace the missing asset.");
            }
        }

        var checks = github.GetCheckSummary(pullRequest.HeadSha);
        if (checks.Total == 0)
            throw new InvalidOperationException($"No check runs were found for pull request head {pullRequest.HeadSha}; refusing an unvalidated release.");
        if (checks.BlockingChecks.Count > 0)
            throw new InvalidOperationException("Pull request checks are not settled successfully: " + string.Join(", ", checks.BlockingChecks));

        _git.EnsureContainsMerge(root, mergeCommitSha);
        var increment = HomeAssistantReleasePolicy.Resolve(pullRequest.Labels, pullRequest.ChangedFiles, spec.Increment);
        var currentVersion = HomeAssistantSemanticVersion.Parse(snapshot.Version);
        var latestRelease = github.GetLatestRelease();
        var releaseVersion = currentVersion;
        var recoveringPreparedVersion = false;

        if (incompleteSourceRelease is not null) {
            releaseVersion = HomeAssistantSemanticVersion.Parse(incompleteSourceRelease.TagName);
            if (currentVersion.CompareTo(releaseVersion) != 0)
                throw new InvalidOperationException($"Incomplete source release {incompleteSourceRelease.TagName} does not match repository metadata version {currentVersion}.");
            recoveringPreparedVersion = true;
        } else if (latestRelease is not null) {
            var latestVersion = HomeAssistantSemanticVersion.Parse(latestRelease.TagName);
            var comparison = currentVersion.CompareTo(latestVersion);
            if (comparison < 0)
                throw new InvalidOperationException($"Repository metadata version {currentVersion} is behind latest GitHub release {latestRelease.TagName}.");
            if (comparison > 0) {
                recoveringPreparedVersion = true;
                _logger.Info($"Repository version {currentVersion} is ahead of {latestRelease.TagName}; resuming publication without another increment.");
            } else if (increment != HomeAssistantVersionIncrement.None) {
                releaseVersion = currentVersion.Increment(increment);
            }
        }

        if (!recoveringPreparedVersion && increment == HomeAssistantVersionIncrement.None) {
            return CreateResult(
                snapshot,
                HomeAssistantReleaseAction.None,
                increment,
                snapshot.Version,
                string.Empty,
                null,
                null,
                "Only documentation, tests, workflow, or maintainer metadata changed; no product release is required.");
        }

        var tagName = "v" + releaseVersion;
        if (!spec.Apply) {
            return CreateResult(
                snapshot,
                HomeAssistantReleaseAction.Planned,
                increment,
                releaseVersion.ToString(),
                tagName,
                null,
                null,
                recoveringPreparedVersion ? "A prepared version will be resumed." : "A version increment and release are planned.");
        }

        _git.EnsureClean(root);
        var result = CreateResult(
            snapshot,
            spec.Publish ? HomeAssistantReleaseAction.Published : HomeAssistantReleaseAction.Planned,
            increment,
            releaseVersion.ToString(),
            tagName,
            null,
            null,
            string.Empty);

        if (!recoveringPreparedVersion && !string.Equals(snapshot.Version, releaseVersion.ToString(), StringComparison.Ordinal)) {
            result.ChangedFiles.AddRange(_repository.UpdateVersion(snapshot, root, releaseVersion.ToString()));
        }

        result.AssetFiles.AddRange(_repository.BuildAssets(snapshot, root, releaseVersion.ToString()));
        var releaseCommit = recoveringPreparedVersion
            ? incompleteSourceRelease is not null
                ? github.GetTagCommitSha(incompleteSourceRelease.TagName)
                    ?? throw new InvalidOperationException($"Unable to resolve incomplete release tag {incompleteSourceRelease.TagName}.")
                : _git.FindCommitForSourcePullRequest(root, pullRequest.Number) ?? _git.GetHeadSha(root)
            : _git.CommitAndPush(root, result.ChangedFiles, releaseVersion.ToString(), pullRequest.Number, mergeCommitSha, spec.DefaultBranch);
        result.ReleaseCommitSha = releaseCommit;

        if (!spec.Publish) {
            result.Message = "Release metadata and artifacts were prepared without publishing a GitHub release.";
            return result;
        }

        var notes = HomeAssistantReleasePolicy.BuildReleaseNotes(pullRequest, marker, releaseCommit);
        var publishResult = publisher.Publish(new GitHubReleasePublishRequest {
            Owner = spec.Owner,
            Repository = spec.Repository,
            Token = spec.Token,
            TagName = tagName,
            ReleaseName = tagName,
            ReleaseNotes = notes,
            Commitish = releaseCommit,
            GenerateReleaseNotes = false,
            ReuseExistingReleaseOnConflict = true,
            ReplaceExistingAssets = true,
            AssetFilePaths = result.AssetFiles
        });
        if (!publishResult.Succeeded)
            throw new InvalidOperationException($"GitHub release publication did not succeed for {tagName}.");

        var published = WaitForRelease(github, tagName);
        VerifyExistingRelease(github, published, snapshot, releaseCommit, marker);
        result.ReleaseUrl = published.HtmlUrl;
        result.Message = publishResult.ReusedExistingRelease
            ? "The existing GitHub release was resumed and verified."
            : "The GitHub release was published and verified.";
        return result;
    }

    private static void Validate(HomeAssistantReleaseSpec spec) {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.RepositoryRoot)) throw new ArgumentException("RepositoryRoot is required.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.Owner)) throw new ArgumentException("Owner is required.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.Repository)) throw new ArgumentException("Repository is required.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.Token)) throw new ArgumentException("Token is required.", nameof(spec));
        if (spec.PullRequestNumber <= 0) throw new ArgumentOutOfRangeException(nameof(spec), "PullRequestNumber must be positive.");
        if (string.IsNullOrWhiteSpace(spec.DefaultBranch)) throw new ArgumentException("DefaultBranch is required.", nameof(spec));
        if (spec.Publish && !spec.Apply) throw new ArgumentException("Publish requires Apply so the tagged version is reproducible.", nameof(spec));
    }

    private static HomeAssistantGitHubRelease WaitForRelease(IHomeAssistantGitHubClient github, string tagName) {
        for (var attempt = 0; attempt < 6; attempt++) {
            var release = github.GetReleaseByTag(tagName);
            if (release is not null) return release;
            if (attempt < 5) Thread.Sleep(TimeSpan.FromSeconds(2));
        }

        throw new InvalidOperationException($"GitHub release {tagName} was not observable after publication.");
    }

    private static void VerifyExistingRelease(
        IHomeAssistantGitHubClient github,
        HomeAssistantGitHubRelease release,
        HomeAssistantRepositorySnapshot snapshot,
        string? expectedCommitSha,
        string marker) {
        if (release.Body.IndexOf(marker, StringComparison.Ordinal) < 0)
            throw new InvalidOperationException($"GitHub release {release.TagName} does not contain the expected PowerForge source marker.");
        _ = HomeAssistantSemanticVersion.Parse(release.TagName);
        var tagCommitSha = github.GetTagCommitSha(release.TagName);
        if (string.IsNullOrWhiteSpace(tagCommitSha))
            throw new InvalidOperationException($"Git tag {release.TagName} could not be resolved to a commit.");
        var recordedCommitSha = HomeAssistantReleasePolicy.ReadReleaseCommit(release.Body);
        if (string.IsNullOrWhiteSpace(recordedCommitSha))
            throw new InvalidOperationException($"GitHub release {release.TagName} does not record its PowerForge release commit.");
        if (!string.Equals(recordedCommitSha, tagCommitSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Git tag {release.TagName} points to {tagCommitSha}, not recorded release commit {recordedCommitSha}.");
        if (!string.IsNullOrWhiteSpace(expectedCommitSha) &&
            !string.Equals(expectedCommitSha, recordedCommitSha, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Git tag {release.TagName} points to {tagCommitSha}, not expected release commit {expectedCommitSha}.");
        }

        var requiredAsset = snapshot.Kind == HomeAssistantRepositoryKind.LovelacePlugin || snapshot.ZipRelease
            ? snapshot.HacsFileName
            : null;
        if (!string.IsNullOrWhiteSpace(requiredAsset) &&
            !release.AssetNames.Contains(requiredAsset!, StringComparer.OrdinalIgnoreCase)) {
            throw new HomeAssistantReleaseAssetException($"GitHub release {release.TagName} is missing required HACS asset '{requiredAsset}'.");
        }
    }

    private static HomeAssistantReleaseResult CreateResult(
        HomeAssistantRepositorySnapshot snapshot,
        HomeAssistantReleaseAction action,
        HomeAssistantVersionIncrement increment,
        string releaseVersion,
        string tagName,
        string? releaseCommitSha,
        string? releaseUrl,
        string message)
        => new() {
            Success = true,
            Action = action,
            RepositoryKind = snapshot.Kind,
            Increment = increment,
            CurrentVersion = snapshot.Version,
            ReleaseVersion = releaseVersion,
            TagName = tagName,
            ReleaseCommitSha = releaseCommitSha,
            ReleaseUrl = releaseUrl,
            Message = message
        };
}