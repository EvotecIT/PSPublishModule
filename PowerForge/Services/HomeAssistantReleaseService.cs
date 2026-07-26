using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace PowerForge;

/// <summary>
/// Coordinates isolated prepare, build, and publish stages for Home Assistant and HACS releases.
/// Repository-controlled build commands are intentionally confined to <see cref="Build"/>, which
/// requires no GitHub write credential.
/// </summary>
public sealed class HomeAssistantReleaseService {
    private readonly ILogger _logger;
    private readonly HomeAssistantRepositoryService _repository;
    private readonly HomeAssistantReleaseGitService _git;
    private readonly IHomeAssistantGitHubClient? _github;
    private readonly IHomeAssistantReleasePublisher? _publisher;

    /// <summary>Creates a release service using production GitHub, git, and process clients.</summary>
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

    /// <summary>Plans or prepares immutable release metadata for a merged pull request.</summary>
    /// <param name="spec">Repository, pull request, policy, and authenticated push settings.</param>
    /// <returns>A plan or prepared commit for the isolated build stage.</returns>
    public HomeAssistantReleaseResult Prepare(HomeAssistantReleasePrepareSpec spec) {
        ValidatePrepare(spec);
        var root = Path.GetFullPath(spec.RepositoryRoot);
        var snapshot = _repository.Inspect(root);
        var github = _github ?? new HomeAssistantGitHubClient(spec.Owner, spec.Repository, spec.Token, spec.ApiBaseUrl);
        var pullRequest = GetValidatedPullRequest(github, spec.PullRequestNumber, spec.MergeCommitSha);
        var mergeCommitSha = pullRequest.MergeCommitSha;
        var marker = HomeAssistantReleasePolicy.BuildMarker(pullRequest.Number, mergeCommitSha);
        var existingSourceRelease = github.FindReleaseByMarker(marker);
        HomeAssistantGitHubRelease? incompleteSourceRelease = null;

        if (existingSourceRelease is not null) {
            VerifyReleaseProvenance(github, existingSourceRelease, expectedCommitSha: null, marker);
            try {
                VerifyReleaseAsset(existingSourceRelease);
                return CreateResult(
                    snapshot,
                    HomeAssistantReleaseAction.Reused,
                    HomeAssistantVersionIncrement.None,
                    HomeAssistantSemanticVersion.Parse(existingSourceRelease.TagName).ToString(),
                    existingSourceRelease.TagName,
                    github.GetTagCommitSha(existingSourceRelease.TagName),
                    existingSourceRelease.HtmlUrl,
                    HomeAssistantReleasePolicy.ReadRequiredAsset(existingSourceRelease.Body),
                    "The source pull request already has a verified PowerForge release.");
            } catch (HomeAssistantReleaseAssetException ex) {
                incompleteSourceRelease = existingSourceRelease;
                _logger.Warn($"{ex.Message} PowerForge will rebuild the exact tagged commit before replacing the asset.");
            }
        }

        ValidateChecks(github, pullRequest, spec.WorkflowRunId);
        _git.EnsureContainsMerge(root, mergeCommitSha);
        var preparedReleaseCommit = _git.FindPreparedReleaseCommit(
            root,
            pullRequest.Number,
            mergeCommitSha,
            _repository.GetVersionMetadataFiles(snapshot, root));
        var increment = HomeAssistantReleasePolicy.Resolve(pullRequest.Labels, pullRequest.ChangedFiles, spec.Increment);
        var currentVersion = HomeAssistantSemanticVersion.Parse(snapshot.Version);
        var latestRelease = github.GetLatestRelease();
        var releaseVersion = currentVersion;
        var recoveringPreparedVersion = false;
        string? releaseCommit = null;

        if (incompleteSourceRelease is not null) {
            releaseVersion = HomeAssistantSemanticVersion.Parse(incompleteSourceRelease.TagName);
            releaseCommit = github.GetTagCommitSha(incompleteSourceRelease.TagName)
                ?? throw new InvalidOperationException($"Unable to resolve incomplete release tag {incompleteSourceRelease.TagName}.");
            recoveringPreparedVersion = true;
        } else if (latestRelease is not null) {
            var latestVersion = HomeAssistantSemanticVersion.Parse(latestRelease.TagName);
            var comparison = currentVersion.CompareTo(latestVersion);
            if (comparison < 0)
                throw new InvalidOperationException($"Repository metadata version {currentVersion} is behind latest GitHub release {latestRelease.TagName}.");
            if (comparison > 0) {
                if (string.IsNullOrWhiteSpace(preparedReleaseCommit)) {
                    throw new InvalidOperationException(
                        $"Repository version {currentVersion} is ahead of {latestRelease.TagName}, but no PowerForge release commit belongs to pull request #{pullRequest.Number}. " +
                        "Finish or rerun the pull request that prepared the ahead version before releasing this pull request.");
                }

                releaseCommit = preparedReleaseCommit;
                recoveringPreparedVersion = true;
                _logger.Info($"Repository version {currentVersion} is ahead of {latestRelease.TagName}; resuming pull request #{pullRequest.Number} from release commit {preparedReleaseCommit}.");
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
                null,
                "Only documentation, tests, workflow, or maintainer metadata changed; no product release is required.");
        }

        var tagName = "v" + releaseVersion;
        PreflightTargetTag(github, tagName, releaseCommit, marker);
        if (!spec.Apply) {
            return CreateResult(
                snapshot,
                HomeAssistantReleaseAction.Planned,
                increment,
                releaseVersion.ToString(),
                tagName,
                releaseCommit,
                null,
                null,
                recoveringPreparedVersion ? "A prepared version will be resumed." : "A version increment and release are planned.");
        }

        _git.EnsureClean(root);
        var result = CreateResult(
            snapshot,
            HomeAssistantReleaseAction.Prepared,
            increment,
            releaseVersion.ToString(),
            tagName,
            releaseCommit,
            null,
            null,
            string.Empty);

        if (!recoveringPreparedVersion && !string.Equals(snapshot.Version, releaseVersion.ToString(), StringComparison.Ordinal))
            result.ChangedFiles.AddRange(_repository.UpdateVersion(snapshot, root, releaseVersion.ToString()));

        if (!recoveringPreparedVersion) {
            releaseCommit = _git.CommitRelease(root, result.ChangedFiles, releaseVersion.ToString(), pullRequest.Number, mergeCommitSha);
            result.ReleaseCommitSha = releaseCommit;
            if (result.ChangedFiles.Count > 0)
                _git.Push(root, spec.DefaultBranch, spec.Token, spec.Owner, spec.Repository, spec.ServerUrl);
        }

        result.ReleaseCommitSha = releaseCommit ?? _git.GetHeadSha(root);
        result.Message = recoveringPreparedVersion
            ? "An existing immutable release commit is ready for an isolated rebuild."
            : "Release metadata was committed and pushed; repository build code has not executed in this privileged stage.";
        return result;
    }

    /// <summary>Builds release assets from an exact commit without GitHub write credentials.</summary>
    /// <param name="spec">Exact checkout commit and expected release version.</param>
    /// <returns>Built assets and repository metadata for the publish stage.</returns>
    public HomeAssistantReleaseResult Build(HomeAssistantReleaseBuildSpec spec) {
        ValidateBuild(spec);
        var root = Path.GetFullPath(spec.RepositoryRoot);
        var expectedCommit = spec.ReleaseCommitSha.Trim();
        var actualCommit = _git.GetHeadSha(root);
        if (!string.Equals(actualCommit, expectedCommit, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Release build checkout is at {actualCommit}, not expected commit {expectedCommit}.");

        _git.EnsureClean(root);
        var snapshot = _repository.Inspect(root);
        if (!string.Equals(snapshot.Version, spec.ReleaseVersion, StringComparison.Ordinal)) {
            throw new InvalidOperationException(
                $"Release commit {expectedCommit} contains repository version {snapshot.Version}, not expected version {spec.ReleaseVersion}.");
        }

        var result = CreateResult(
            snapshot,
            HomeAssistantReleaseAction.Built,
            HomeAssistantVersionIncrement.None,
            spec.ReleaseVersion,
            "v" + spec.ReleaseVersion,
            expectedCommit,
            null,
            GetRequiredAssetName(snapshot),
            string.Empty);
        result.AssetFiles.AddRange(_repository.BuildAssets(snapshot, root, spec.ReleaseVersion));
        _git.EnsureNoTrackedChanges(root);

        if (!string.IsNullOrWhiteSpace(result.RequiredAssetName)) {
            var asset = result.AssetFiles.SingleOrDefault(path =>
                string.Equals(Path.GetFileName(path), result.RequiredAssetName, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(asset) || !File.Exists(asset) || new FileInfo(asset).Length <= 0)
                throw new InvalidOperationException($"The isolated build did not produce non-empty required HACS asset '{result.RequiredAssetName}'.");
        } else if (result.AssetFiles.Count > 0) {
            throw new InvalidOperationException("The repository produced release assets without declaring a required HACS asset name.");
        }

        result.Message = "Release assets were built from the exact prepared commit without GitHub write credentials.";
        return result;
    }

    /// <summary>Publishes and verifies a prepared release without executing repository-controlled code.</summary>
    /// <param name="spec">Immutable release identity and optional downloaded HACS asset.</param>
    /// <returns>Published or safely reused release details.</returns>
    public HomeAssistantReleaseResult Publish(HomeAssistantReleasePublishSpec spec) {
        ValidatePublish(spec);
        var releaseVersion = HomeAssistantSemanticVersion.Parse(spec.ReleaseVersion).ToString();
        var tagName = "v" + releaseVersion;
        var github = _github ?? new HomeAssistantGitHubClient(spec.Owner, spec.Repository, spec.Token, spec.ApiBaseUrl);
        var publisher = _publisher ?? new HomeAssistantReleasePublisher(_logger);
        var pullRequest = GetValidatedPullRequest(github, spec.PullRequestNumber, spec.MergeCommitSha);
        var marker = HomeAssistantReleasePolicy.BuildMarker(pullRequest.Number, pullRequest.MergeCommitSha);
        var existing = github.GetReleaseByTag(tagName);

        if (existing is not null) {
            VerifyReleaseProvenance(github, existing, spec.ReleaseCommitSha, marker);
            var recordedAsset = HomeAssistantReleasePolicy.ReadRequiredAsset(existing.Body) ?? string.Empty;
            if (!string.Equals(recordedAsset, spec.RequiredAssetName, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException(
                    $"GitHub release {tagName} records required asset '{recordedAsset}', not expected '{spec.RequiredAssetName}'.");
            }

            try {
                VerifyReleaseAsset(existing);
                return CreatePublishResult(
                    HomeAssistantReleaseAction.Reused,
                    releaseVersion,
                    tagName,
                    spec.ReleaseCommitSha,
                    existing.HtmlUrl,
                    spec.RequiredAssetName,
                    "The exact PowerForge release and required asset already exist and were verified.");
            } catch (HomeAssistantReleaseAssetException ex) {
                _logger.Warn($"{ex.Message} The verified release asset will be replaced.");
            }
        } else {
            var tagCommit = github.GetTagCommitSha(tagName);
            if (!string.IsNullOrWhiteSpace(tagCommit) &&
                !string.Equals(tagCommit, spec.ReleaseCommitSha, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidOperationException($"Git tag {tagName} already points to {tagCommit}, not expected release commit {spec.ReleaseCommitSha}.");
            }
        }

        var assets = string.IsNullOrWhiteSpace(spec.RequiredAssetName)
            ? Array.Empty<string>()
            : new[] { Path.GetFullPath(spec.AssetFilePath) };
        var notes = HomeAssistantReleasePolicy.BuildReleaseMetadata(
            marker,
            spec.ReleaseCommitSha,
            string.IsNullOrWhiteSpace(spec.RequiredAssetName) ? null : spec.RequiredAssetName);
        var publishResult = publisher.Publish(new GitHubReleasePublishRequest {
            Owner = spec.Owner,
            Repository = spec.Repository,
            Token = spec.Token,
            ApiBaseUrl = spec.ApiBaseUrl,
            TagName = tagName,
            ReleaseName = tagName,
            ReleaseNotes = notes,
            Commitish = spec.ReleaseCommitSha,
            GenerateReleaseNotes = true,
            ReuseExistingReleaseOnConflict = true,
            ReplaceExistingAssets = true,
            RequireExpectedExistingRelease = true,
            ExpectedExistingReleaseId = existing?.Id,
            ExpectedReleaseBodyMarker = marker,
            ExpectedTagCommitSha = spec.ReleaseCommitSha,
            AssetFilePaths = assets
        });
        if (!publishResult.Succeeded)
            throw new InvalidOperationException($"GitHub release publication did not succeed for {tagName}.");

        var published = WaitForRelease(github, tagName);
        VerifyReleaseProvenance(github, published, spec.ReleaseCommitSha, marker);
        VerifyReleaseAsset(published);
        return CreatePublishResult(
            HomeAssistantReleaseAction.Published,
            releaseVersion,
            tagName,
            spec.ReleaseCommitSha,
            published.HtmlUrl,
            spec.RequiredAssetName,
            publishResult.ReusedExistingRelease
                ? "The verified existing GitHub release asset was replaced and the release was reverified."
                : "The GitHub release was published and verified.");
    }

    private static HomeAssistantPullRequest GetValidatedPullRequest(
        IHomeAssistantGitHubClient github,
        int pullRequestNumber,
        string? expectedMergeCommitSha) {
        var pullRequest = github.GetPullRequest(pullRequestNumber);
        if (!pullRequest.Merged)
            throw new InvalidOperationException($"Pull request #{pullRequest.Number} is not merged.");
        if (!string.IsNullOrWhiteSpace(expectedMergeCommitSha) &&
            !string.Equals(expectedMergeCommitSha, pullRequest.MergeCommitSha, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException(
                $"Workflow merge SHA '{expectedMergeCommitSha}' does not match GitHub pull request merge SHA '{pullRequest.MergeCommitSha}'.");
        }

        return pullRequest;
    }

    private static void ValidateChecks(
        IHomeAssistantGitHubClient github,
        HomeAssistantPullRequest pullRequest,
        long? excludedWorkflowRunId) {
        var checks = github.GetCheckSummary(pullRequest.HeadSha, excludedWorkflowRunId);
        if (checks.Total == 0)
            throw new InvalidOperationException($"No check runs were found for pull request head {pullRequest.HeadSha}; refusing an unvalidated release.");
        if (checks.BlockingChecks.Count > 0)
            throw new InvalidOperationException("Pull request checks are not settled successfully: " + string.Join(", ", checks.BlockingChecks));
    }

    private static void PreflightTargetTag(
        IHomeAssistantGitHubClient github,
        string tagName,
        string? expectedCommitSha,
        string marker) {
        var targetRelease = github.GetReleaseByTag(tagName);
        if (targetRelease is not null) {
            VerifyReleaseProvenance(github, targetRelease, expectedCommitSha, marker);
            return;
        }

        var tagCommit = github.GetTagCommitSha(tagName);
        if (string.IsNullOrWhiteSpace(tagCommit)) return;
        if (string.IsNullOrWhiteSpace(expectedCommitSha) ||
            !string.Equals(tagCommit, expectedCommitSha, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Git tag {tagName} already exists at {tagCommit} and does not belong to this prepared release.");
        }
    }

    private static string? GetRequiredAssetName(HomeAssistantRepositorySnapshot snapshot)
        => snapshot.Kind == HomeAssistantRepositoryKind.LovelacePlugin || snapshot.ZipRelease
            ? snapshot.HacsFileName
            : null;

    private static void ValidatePrepare(HomeAssistantReleasePrepareSpec spec) {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.RepositoryRoot)) throw new ArgumentException("RepositoryRoot is required.", nameof(spec));
        ValidateGitHubIdentity(spec.Owner, spec.Repository, spec.Token, spec.PullRequestNumber);
        if (spec.WorkflowRunId.HasValue && spec.WorkflowRunId.Value <= 0)
            throw new ArgumentException("WorkflowRunId must be positive when supplied.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.DefaultBranch)) throw new ArgumentException("DefaultBranch is required.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.ServerUrl)) throw new ArgumentException("ServerUrl is required.", nameof(spec));
    }

    private static void ValidateBuild(HomeAssistantReleaseBuildSpec spec) {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.RepositoryRoot)) throw new ArgumentException("RepositoryRoot is required.", nameof(spec));
        _ = HomeAssistantSemanticVersion.Parse(spec.ReleaseVersion);
        if (string.IsNullOrWhiteSpace(spec.ReleaseCommitSha)) throw new ArgumentException("ReleaseCommitSha is required.", nameof(spec));
    }

    private static void ValidatePublish(HomeAssistantReleasePublishSpec spec) {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        ValidateGitHubIdentity(spec.Owner, spec.Repository, spec.Token, spec.PullRequestNumber);
        _ = HomeAssistantSemanticVersion.Parse(spec.ReleaseVersion);
        if (string.IsNullOrWhiteSpace(spec.ReleaseCommitSha)) throw new ArgumentException("ReleaseCommitSha is required.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.RequiredAssetName)) {
            if (!string.IsNullOrWhiteSpace(spec.AssetFilePath))
                throw new ArgumentException("AssetFilePath cannot be supplied when RequiredAssetName is empty.", nameof(spec));
            return;
        }

        if (!string.Equals(Path.GetFileName(spec.RequiredAssetName), spec.RequiredAssetName, StringComparison.Ordinal) ||
            spec.RequiredAssetName.IndexOf('/') >= 0 || spec.RequiredAssetName.IndexOf('\\') >= 0) {
            throw new ArgumentException("RequiredAssetName must be a file name without a directory.", nameof(spec));
        }
        if (string.IsNullOrWhiteSpace(spec.AssetFilePath) || !File.Exists(spec.AssetFilePath))
            throw new FileNotFoundException($"Required release asset was not found: {spec.AssetFilePath}");
        if (!string.Equals(Path.GetFileName(spec.AssetFilePath), spec.RequiredAssetName, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Asset file name must be '{spec.RequiredAssetName}'.", nameof(spec));
        if (new FileInfo(spec.AssetFilePath).Length <= 0)
            throw new InvalidOperationException($"Required release asset '{spec.AssetFilePath}' is empty.");
    }

    private static void ValidateGitHubIdentity(string owner, string repository, string token, int pullRequestNumber) {
        if (string.IsNullOrWhiteSpace(owner)) throw new ArgumentException("Owner is required.");
        if (string.IsNullOrWhiteSpace(repository)) throw new ArgumentException("Repository is required.");
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Token is required.");
        if (pullRequestNumber <= 0) throw new ArgumentOutOfRangeException(nameof(pullRequestNumber), "PullRequestNumber must be positive.");
    }

    private static HomeAssistantGitHubRelease WaitForRelease(IHomeAssistantGitHubClient github, string tagName) {
        for (var attempt = 0; attempt < 6; attempt++) {
            var release = github.GetReleaseByTag(tagName);
            if (release is not null) return release;
            if (attempt < 5) Thread.Sleep(TimeSpan.FromSeconds(2));
        }

        throw new InvalidOperationException($"GitHub release {tagName} was not observable after publication.");
    }

    private static void VerifyReleaseProvenance(
        IHomeAssistantGitHubClient github,
        HomeAssistantGitHubRelease release,
        string? expectedCommitSha,
        string marker) {
        if (release.Body.IndexOf(marker, StringComparison.Ordinal) < 0)
            throw new InvalidOperationException($"GitHub release {release.TagName} does not contain the expected PowerForge source marker.");
        if (release.IsDraft || release.IsPrerelease)
            throw new InvalidOperationException($"GitHub release {release.TagName} must be a published stable release, not a draft or prerelease.");
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
    }

    private static void VerifyReleaseAsset(HomeAssistantGitHubRelease release) {
        var requiredAsset = HomeAssistantReleasePolicy.ReadRequiredAsset(release.Body);
        if (!string.IsNullOrWhiteSpace(requiredAsset) &&
            !release.AssetNames.Contains(requiredAsset!, StringComparer.OrdinalIgnoreCase)) {
            throw new HomeAssistantReleaseAssetException($"GitHub release {release.TagName} is missing required HACS asset '{requiredAsset}'.");
        }
        if (!string.IsNullOrWhiteSpace(requiredAsset) &&
            (!release.AssetSizes.TryGetValue(requiredAsset!, out var size) || size <= 0)) {
            throw new HomeAssistantReleaseAssetException($"GitHub release {release.TagName} has an empty or unobservable HACS asset '{requiredAsset}'.");
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
        string? requiredAssetName,
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
            RequiredAssetName = requiredAssetName ?? string.Empty,
            Message = message
        };

    private static HomeAssistantReleaseResult CreatePublishResult(
        HomeAssistantReleaseAction action,
        string releaseVersion,
        string tagName,
        string releaseCommitSha,
        string? releaseUrl,
        string requiredAssetName,
        string message)
        => new() {
            Success = true,
            Action = action,
            RepositoryKind = HomeAssistantRepositoryKind.Unknown,
            Increment = HomeAssistantVersionIncrement.None,
            CurrentVersion = releaseVersion,
            ReleaseVersion = releaseVersion,
            TagName = tagName,
            ReleaseCommitSha = releaseCommitSha,
            ReleaseUrl = releaseUrl,
            RequiredAssetName = requiredAssetName,
            Message = message
        };
}
