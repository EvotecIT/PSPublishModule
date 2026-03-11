using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Publishes repository release artefacts to GitHub releases.
/// </summary>
public sealed class ProjectBuildGitHubPublisher
{
    private readonly ILogger _logger;
    private readonly Func<GitHubReleasePublishRequest, GitHubReleasePublishResult> _publishRelease;
    private readonly Func<DateTime> _localNow;
    private readonly Func<DateTime> _utcNow;

    /// <summary>
    /// Creates a new GitHub publisher workflow service.
    /// </summary>
    public ProjectBuildGitHubPublisher(
        ILogger logger,
        Func<GitHubReleasePublishRequest, GitHubReleasePublishResult>? publishRelease = null,
        Func<DateTime>? localNow = null,
        Func<DateTime>? utcNow = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _publishRelease = publishRelease ?? (request => new GitHubReleasePublisher(_logger).PublishRelease(request));
        _localNow = localNow ?? (() => DateTime.Now);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Builds a preflight advisory when GitHub single-release reuse would publish a mixed-version asset set into an existing tag.
    /// </summary>
    public static string? BuildSingleReleaseReuseAdvisory(
        string tag,
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        IReadOnlyCollection<string> plannedAssetNames,
        IReadOnlyCollection<string> existingAssetNames)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        var packable = projects
            .Where(p => p.IsPackable && !string.IsNullOrWhiteSpace(p.NewVersion))
            .ToArray();
        var distinctVersions = packable
            .Select(p => p.NewVersion!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctVersions.Length <= 1)
            return null;

        var planned = new HashSet<string>(
            plannedAssetNames.Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);
        var existing = new HashSet<string>(
            existingAssetNames.Where(name => !string.IsNullOrWhiteSpace(name)),
            StringComparer.OrdinalIgnoreCase);

        if (planned.Count == 0 || planned.SetEquals(existing))
            return null;

        var versionSummary = string.Join(", ",
            packable
                .OrderBy(p => p.ProjectName, StringComparer.OrdinalIgnoreCase)
                .Select(p => $"{p.ProjectName}={p.NewVersion}"));
        var missingAssets = planned
            .Except(existing, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var extraAssets = existing
            .Except(planned, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var details = new List<string>
        {
            $"GitHub preflight blocked publish: tag '{tag}' already exists and Single release mode would reuse it for a mixed-version package set ({versionSummary})."
        };

        if (missingAssets.Length > 0)
            details.Add($"Planned assets missing from the existing release: {string.Join(", ", missingAssets)}.");
        if (extraAssets.Length > 0)
            details.Add($"Existing release contains different assets: {string.Join(", ", extraAssets)}.");

        details.Add("Change one of: set GitHubReleaseMode to 'PerProject'; set GitHubTagConflictPolicy to 'Fail' or 'AppendUtcTimestamp'; or use a unique GitHubTagTemplate such as '{Repo}-v{UtcTimestamp}'. Keep GitHubPrimaryProject explicit when Single mode is intentional.");
        return string.Join(" ", details);
    }

    /// <summary>
    /// Publishes release artefacts to GitHub and returns a summarized result.
    /// </summary>
    public ProjectBuildGitHubPublishSummary Publish(ProjectBuildGitHubPublishRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        if (request.Release is null)
            throw new ArgumentException("Release is required.", nameof(request));

        var summary = new ProjectBuildGitHubPublishSummary();
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            summary.Success = false;
            summary.ErrorMessage = "GitHub access token is required for GitHub publishing.";
            return summary;
        }

        if (string.IsNullOrWhiteSpace(request.Owner) || string.IsNullOrWhiteSpace(request.Repository))
        {
            summary.Success = false;
            summary.ErrorMessage = "GitHub owner and repository are required for GitHub publishing.";
            return summary;
        }

        var releaseMode = string.IsNullOrWhiteSpace(request.ReleaseMode) ? "Single" : request.ReleaseMode.Trim();
        var perProject = string.Equals(releaseMode, "PerProject", StringComparison.OrdinalIgnoreCase);
        summary.PerProject = perProject;

        var conflictPolicy = ParseTagConflictPolicy(request.TagConflictPolicy);
        var reuseExistingReleaseOnConflict = string.Equals(conflictPolicy, "Reuse", StringComparison.OrdinalIgnoreCase);

        var nowLocal = _localNow();
        var nowUtc = _utcNow();
        var dateToken = nowLocal.ToString("yyyy.MM.dd");
        var utcDateToken = nowUtc.ToString("yyyy.MM.dd");
        var dateTimeToken = nowLocal.ToString("yyyy.MM.dd-HH.mm.ss");
        var utcDateTimeToken = nowUtc.ToString("yyyy.MM.dd-HH.mm.ss");
        var timestampToken = nowLocal.ToString("yyyyMMddHHmmss");
        var utcTimestampToken = nowUtc.ToString("yyyyMMddHHmmss");
        var repoName = request.Repository.Trim();

        if (perProject)
        {
            PublishPerProject(
                summary,
                request,
                repoName,
                conflictPolicy,
                reuseExistingReleaseOnConflict,
                dateToken,
                utcDateToken,
                dateTimeToken,
                utcDateTimeToken,
                timestampToken,
                utcTimestampToken);
        }
        else
        {
            PublishSingleRelease(
                summary,
                request,
                repoName,
                conflictPolicy,
                reuseExistingReleaseOnConflict,
                dateToken,
                utcDateToken,
                dateTimeToken,
                utcDateTimeToken,
                timestampToken,
                utcTimestampToken);
        }

        if (summary.ErrorMessage is null)
            summary.Success = summary.Results.Count == 0 || summary.Results.TrueForAll(result => result.Success);

        return summary;
    }

    private void PublishPerProject(
        ProjectBuildGitHubPublishSummary summary,
        ProjectBuildGitHubPublishRequest request,
        string repoName,
        string conflictPolicy,
        bool reuseExistingReleaseOnConflict,
        string dateToken,
        string utcDateToken,
        string dateTimeToken,
        string utcDateTimeToken,
        string timestampToken,
        string utcTimestampToken)
    {
        foreach (var project in request.Release.Projects)
        {
            if (!project.IsPackable)
                continue;

            var result = new ProjectBuildGitHubResult { ProjectName = project.ProjectName };
            var error = ValidateProjectPublishInput(project);
            if (!string.IsNullOrWhiteSpace(error))
            {
                result.Success = false;
                result.ErrorMessage = error;
                summary.Results.Add(result);
                if (request.PublishFailFast)
                {
                    summary.Success = false;
                    summary.ErrorMessage = error;
                    break;
                }

                continue;
            }

            var projectVersion = project.NewVersion ?? project.OldVersion ?? string.Empty;
            var tag = BuildProjectTag(
                request,
                repoName,
                project.ProjectName,
                projectVersion,
                conflictPolicy,
                dateToken,
                utcDateToken,
                dateTimeToken,
                utcDateTimeToken,
                timestampToken,
                utcTimestampToken);

            var releaseName = BuildReleaseName(
                request.ReleaseName,
                repoName,
                project.ProjectName,
                projectVersion,
                request.PrimaryProject ?? project.ProjectName,
                projectVersion,
                tag,
                dateToken,
                utcDateToken,
                dateTimeToken,
                utcDateTimeToken,
                timestampToken,
                utcTimestampToken);

            TryPublishRelease(
                request,
                tag,
                releaseName,
                new[] { project.ReleaseZipPath! },
                reuseExistingReleaseOnConflict,
                result);

            summary.Results.Add(result);
            if (!result.Success)
            {
                summary.Success = false;
                summary.ErrorMessage = result.ErrorMessage ?? "GitHub publish failed.";
                if (request.PublishFailFast)
                    break;
            }
        }
    }

    private void PublishSingleRelease(
        ProjectBuildGitHubPublishSummary summary,
        ProjectBuildGitHubPublishRequest request,
        string repoName,
        string conflictPolicy,
        bool reuseExistingReleaseOnConflict,
        string dateToken,
        string utcDateToken,
        string dateTimeToken,
        string utcDateTimeToken,
        string timestampToken,
        string utcTimestampToken)
    {
        var assets = request.Release.Projects
            .Where(project => project.IsPackable && !string.IsNullOrWhiteSpace(project.ReleaseZipPath) && File.Exists(project.ReleaseZipPath))
            .Select(project => project.ReleaseZipPath!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (assets.Length == 0)
        {
            summary.Success = false;
            summary.ErrorMessage = "No release zips available for GitHub release.";
            return;
        }

        var baseVersion = ResolveGitHubBaseVersion(request.PrimaryProject, request.Release);
        var versionToken = string.IsNullOrWhiteSpace(baseVersion) ? dateToken : baseVersion!;
        var tag = BuildSingleReleaseTag(
            request,
            repoName,
            versionToken,
            conflictPolicy,
            dateToken,
            utcDateToken,
            dateTimeToken,
            utcDateTimeToken,
            timestampToken,
            utcTimestampToken);
        var releaseName = BuildReleaseName(
            request.ReleaseName,
            repoName,
            repoName,
            versionToken,
            request.PrimaryProject ?? repoName,
            versionToken,
            tag,
            dateToken,
            utcDateToken,
            dateTimeToken,
            utcDateTimeToken,
            timestampToken,
            utcTimestampToken);

        var publishResult = new ProjectBuildGitHubResult();
        TryPublishRelease(
            request,
            tag,
            releaseName,
            assets,
            reuseExistingReleaseOnConflict,
            publishResult);

        foreach (var project in request.Release.Projects.Where(project => project.IsPackable))
        {
            summary.Results.Add(new ProjectBuildGitHubResult
            {
                ProjectName = project.ProjectName,
                Success = publishResult.Success,
                TagName = publishResult.TagName,
                ReleaseUrl = publishResult.ReleaseUrl,
                ErrorMessage = publishResult.ErrorMessage
            });
        }

        summary.Success = publishResult.Success;
        summary.ErrorMessage = publishResult.ErrorMessage;
        summary.SummaryTag = tag;
        summary.SummaryReleaseUrl = publishResult.ReleaseUrl;
        summary.SummaryAssetsCount = assets.Length;
    }

    private void TryPublishRelease(
        ProjectBuildGitHubPublishRequest request,
        string tag,
        string releaseName,
        IReadOnlyList<string> assets,
        bool reuseExistingReleaseOnConflict,
        ProjectBuildGitHubResult result)
    {
        try
        {
            var publishResult = _publishRelease(new GitHubReleasePublishRequest
            {
                Owner = request.Owner,
                Repository = request.Repository,
                Token = request.Token,
                TagName = tag,
                ReleaseName = releaseName,
                GenerateReleaseNotes = request.GenerateReleaseNotes,
                IsPreRelease = request.IsPreRelease,
                ReuseExistingReleaseOnConflict = reuseExistingReleaseOnConflict,
                AssetFilePaths = assets
            });

            result.Success = publishResult.Succeeded;
            result.TagName = tag;
            result.ReleaseUrl = publishResult.HtmlUrl;
            result.ErrorMessage = null;
            WriteGitHubPublishNotes(tag, publishResult);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.TagName = tag;
            result.ReleaseUrl = null;
            result.ErrorMessage = ex.Message;
        }
    }

    private void WriteGitHubPublishNotes(string? tag, GitHubReleasePublishResult result)
    {
        if (result.ReusedExistingRelease && !string.IsNullOrWhiteSpace(tag))
            _logger.Info($"GitHub release for tag '{tag}' already exists; reusing existing release.");

        foreach (var asset in result.SkippedExistingAssets
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            _logger.Info($"GitHub release asset '{asset}' already exists; skipping upload.");
        }
    }

    private static string? ValidateProjectPublishInput(DotNetRepositoryProjectResult project)
    {
        if (string.IsNullOrWhiteSpace(project.NewVersion))
            return "Missing project version for GitHub release.";

        if (string.IsNullOrWhiteSpace(project.ReleaseZipPath))
            return "No release zip available for GitHub release.";

        return File.Exists(project.ReleaseZipPath)
            ? null
            : $"Release zip not found: {project.ReleaseZipPath}";
    }

    private static string BuildProjectTag(
        ProjectBuildGitHubPublishRequest request,
        string repoName,
        string projectName,
        string version,
        string conflictPolicy,
        string dateToken,
        string utcDateToken,
        string dateTimeToken,
        string utcDateTimeToken,
        string timestampToken,
        string utcTimestampToken)
    {
        var tag = string.IsNullOrWhiteSpace(request.TagName)
            ? (request.IncludeProjectNameInTag ? $"{projectName}-v{version}" : $"v{version}")
            : request.TagName!;

        if (!string.IsNullOrWhiteSpace(request.TagTemplate))
        {
            tag = ApplyTemplate(
                request.TagTemplate!,
                projectName,
                version,
                request.PrimaryProject ?? projectName,
                version,
                repoName,
                dateToken,
                utcDateToken,
                dateTimeToken,
                utcDateTimeToken,
                timestampToken,
                utcTimestampToken);
        }

        return ApplyTagConflictPolicy(tag, conflictPolicy, utcTimestampToken);
    }

    private static string BuildSingleReleaseTag(
        ProjectBuildGitHubPublishRequest request,
        string repoName,
        string versionToken,
        string conflictPolicy,
        string dateToken,
        string utcDateToken,
        string dateTimeToken,
        string utcDateTimeToken,
        string timestampToken,
        string utcTimestampToken)
    {
        var tag = !string.IsNullOrWhiteSpace(request.TagName)
            ? request.TagName!
            : (!string.IsNullOrWhiteSpace(request.TagTemplate)
                ? ApplyTemplate(
                    request.TagTemplate!,
                    repoName,
                    versionToken,
                    request.PrimaryProject ?? repoName,
                    versionToken,
                    repoName,
                    dateToken,
                    utcDateToken,
                    dateTimeToken,
                    utcDateTimeToken,
                    timestampToken,
                    utcTimestampToken)
                : $"v{versionToken}");

        return ApplyTagConflictPolicy(tag, conflictPolicy, utcTimestampToken);
    }

    private static string BuildReleaseName(
        string? template,
        string repoName,
        string projectName,
        string version,
        string primaryProject,
        string primaryVersion,
        string fallbackTag,
        string dateToken,
        string utcDateToken,
        string dateTimeToken,
        string utcDateTimeToken,
        string timestampToken,
        string utcTimestampToken)
    {
        return string.IsNullOrWhiteSpace(template)
            ? fallbackTag
            : ApplyTemplate(
                template!,
                projectName,
                version,
                primaryProject,
                primaryVersion,
                repoName,
                dateToken,
                utcDateToken,
                dateTimeToken,
                utcDateTimeToken,
                timestampToken,
                utcTimestampToken);
    }

    private static string? ResolveGitHubBaseVersion(string? primaryProject, DotNetRepositoryReleaseResult release)
    {
        if (!string.IsNullOrWhiteSpace(primaryProject))
        {
            var match = release.Projects.FirstOrDefault(project =>
                string.Equals(project.ProjectName, primaryProject, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match.NewVersion ?? match.OldVersion;
        }

        var versions = release.Projects
            .Where(project => project.IsPackable && !string.IsNullOrWhiteSpace(project.NewVersion))
            .Select(project => project.NewVersion!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return versions.Length == 1 ? versions[0] : null;
    }

    private static string ParseTagConflictPolicy(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return "Reuse";

        var resolvedText = text;
        return resolvedText!.Equals("AppendUtcTimestamp", StringComparison.OrdinalIgnoreCase)
            ? "AppendUtcTimestamp"
            : resolvedText.Equals("Fail", StringComparison.OrdinalIgnoreCase)
                ? "Fail"
                : "Reuse";
    }

    private static string ApplyTagConflictPolicy(string tag, string policy, string utcTimestampToken)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return tag;

        return string.Equals(policy, "AppendUtcTimestamp", StringComparison.OrdinalIgnoreCase)
            ? $"{tag}-{utcTimestampToken}"
            : tag;
    }

    private static string ApplyTemplate(
        string template,
        string project,
        string version,
        string primaryProject,
        string primaryVersion,
        string repo,
        string date,
        string utcDate,
        string dateTime,
        string utcDateTime,
        string timestamp,
        string utcTimestamp)
    {
        if (string.IsNullOrWhiteSpace(template))
            return template;

        return template
            .Replace("{Project}", project ?? string.Empty)
            .Replace("{Version}", version ?? string.Empty)
            .Replace("{PrimaryProject}", primaryProject ?? string.Empty)
            .Replace("{PrimaryVersion}", primaryVersion ?? string.Empty)
            .Replace("{Repo}", repo ?? string.Empty)
            .Replace("{Repository}", repo ?? string.Empty)
            .Replace("{Date}", date ?? string.Empty)
            .Replace("{UtcDate}", utcDate ?? string.Empty)
            .Replace("{DateTime}", dateTime ?? string.Empty)
            .Replace("{UtcDateTime}", utcDateTime ?? string.Empty)
            .Replace("{Timestamp}", timestamp ?? string.Empty)
            .Replace("{UtcTimestamp}", utcTimestamp ?? string.Empty);
    }
}
