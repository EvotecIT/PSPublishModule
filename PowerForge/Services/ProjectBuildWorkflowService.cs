using System.Diagnostics;

namespace PowerForge;

internal sealed class ProjectBuildWorkflowService
{
    private readonly ILogger _logger;
    private readonly ProjectBuildSupportService _support;
    private readonly Func<DotNetRepositoryReleaseSpec, Action<DotNetReleaseBuildAssemblySigningRequest>?, Action<DotNetReleaseBuildAssemblySigningPreflightRequest>?, IProjectBuildProgressReporter?, CancellationToken, DotNetRepositoryReleaseResult> _executeRelease;
    private readonly Func<ProjectBuildGitHubPublishRequest, ProjectBuildGitHubPublishSummary> _publishGitHub;
    private readonly Func<ProjectBuildConfiguration, DotNetRepositoryReleaseResult, string, string?> _validateGitHubPreflight;
    private readonly Action<DotNetReleaseBuildAssemblySigningRequest>? _signAssemblies;
    private readonly Action<DotNetReleaseBuildAssemblySigningPreflightRequest>? _validateAssemblySigning;

    public ProjectBuildWorkflowService(
        ILogger logger,
        Func<DotNetRepositoryReleaseSpec, DotNetRepositoryReleaseResult>? executeRelease = null,
        Func<ProjectBuildGitHubPublishRequest, ProjectBuildGitHubPublishSummary>? publishGitHub = null,
        Func<ProjectBuildConfiguration, DotNetRepositoryReleaseResult, string, string?>? validateGitHubPreflight = null,
        Action<DotNetReleaseBuildAssemblySigningRequest>? signAssemblies = null,
        Action<DotNetReleaseBuildAssemblySigningPreflightRequest>? validateAssemblySigning = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _support = new ProjectBuildSupportService(_logger);
        _executeRelease = executeRelease is null
            ? (spec, signing, preflight, progress, cancellationToken) => new DotNetRepositoryReleaseService(_logger).Execute(spec, signing, preflight, progress, cancellationToken)
            : (spec, _, _, _, _) => executeRelease(spec);
        _publishGitHub = publishGitHub ?? (request => new ProjectBuildGitHubPublisher(_logger).Publish(request));
        _validateGitHubPreflight = validateGitHubPreflight ?? ((config, plan, token) =>
            new ProjectBuildGitHubPreflightService(_logger).Validate(config, plan, token));
        _signAssemblies = signAssemblies;
        _validateAssemblySigning = validateAssemblySigning;
    }

    public ProjectBuildWorkflowResult Execute(
        ProjectBuildConfiguration config,
        string configDir,
        ProjectBuildPreparedContext preparation,
        bool executeBuild,
        Action? remotePublishAttempted = null,
        bool coordinatedReleaseCheckpointActive = false,
        IProjectBuildProgressReporter? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (config is null)
            throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(configDir))
            throw new ArgumentException("Configuration directory is required.", nameof(configDir));
        if (preparation is null)
            throw new ArgumentNullException(nameof(preparation));

        using var operationLock = executeBuild && !preparation.PlanOnly
            ? ProjectBuildOperationLock.Acquire(preparation)
            : null;

        var spec = preparation.Spec ?? throw new ArgumentException("Prepared spec is required.", nameof(preparation));
        spec.WhatIf = true;
        progress?.PhaseStarted(ProjectBuildProgressPhase.Plan, 1, "Discovering projects and resolving versions");
        var planWatch = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        var plan = _executeRelease(spec, _signAssemblies, _validateAssemblySigning, null, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        planWatch.Stop();
        if (plan.Success)
        {
            _logger.Success($"Project build plan prepared in {DotNetRepositoryReleaseService.FormatDuration(planWatch.Elapsed)}.");
            progress?.PhaseCompleted(
                ProjectBuildProgressPhase.Plan,
                $"{plan.Projects.Count} project(s), {plan.Projects.Count(project => project.IsPackable)} packable, {DotNetRepositoryReleaseService.FormatDuration(planWatch.Elapsed)}");
        }
        else
        {
            _logger.Warn($"Project build plan failed after {DotNetRepositoryReleaseService.FormatDuration(planWatch.Elapsed)}.");
            progress?.PhaseFailed(ProjectBuildProgressPhase.Plan, plan.ErrorMessage);
        }

        var preflightErrors = new List<string>();
        if (!plan.Success)
            preflightErrors.Add(plan.ErrorMessage ?? "Plan/preflight validation failed.");
        else if (TryCreateReusablePlanVersions(plan.ResolvedVersionsByProject, out var reusablePlanVersions) &&
                 (spec.VersionBindings is null || spec.VersionBindings.Count == 0))
        {
            spec.PlannedVersionsByProject = reusablePlanVersions;
            _logger.Info($"Reusing {plan.ResolvedVersionsByProject.Count} resolved project version(s) from the plan for release execution.");
        }
        else if (plan.ResolvedVersionsByProject.Count > 0)
        {
            spec.PlannedVersionsByProject = null;
            _logger.Info("Release execution will re-evaluate effective project versions because the plan cannot be reused safely.");
        }

        if (!executeBuild || preparation.PlanOnly)
        {
            _support.TryWritePlan(plan, preparation.PlanOutputPath);
            return new ProjectBuildWorkflowResult
            {
                Result = CreateResult(preflightErrors, plan)
            };
        }

        var preflightError = _support.ValidatePreflight(
            preparation.PublishNuget,
            preparation.PublishGitHub,
            preparation.CreateReleaseZip,
            preparation.PublishApiKey,
            preparation.GitHubToken,
            config.GitHubUsername,
            config.GitHubRepositoryName);
        if (!string.IsNullOrWhiteSpace(preflightError))
            preflightErrors.Add(preflightError!);

        var gitHubToken = preparation.PublishGitHub ? preparation.GitHubToken : null;
        if (preparation.PublishGitHub && coordinatedReleaseCheckpointActive)
        {
            var retrySafetyError = ProjectBuildGitHubRetrySafety.Validate(config, plan);
            if (!string.IsNullOrWhiteSpace(retrySafetyError))
                preflightErrors.Add(retrySafetyError!);
        }

        if (preparation.PublishGitHub && preflightErrors.Count == 0)
        {
            var gitHubPreflightError = _validateGitHubPreflight(config, plan, gitHubToken!);
            if (!string.IsNullOrWhiteSpace(gitHubPreflightError))
                preflightErrors.Add(gitHubPreflightError!);
        }

        if (preflightErrors.Count > 0)
        {
            return new ProjectBuildWorkflowResult
            {
                Result = CreateResult(preflightErrors, plan)
            };
        }

        if (!string.IsNullOrWhiteSpace(preparation.StagingPath))
            _support.PrepareStaging(preparation.StagingPath!, config.CleanStaging ?? false);
        ProjectBuildSupportService.EnsureDirectory(preparation.OutputPath);
        ProjectBuildSupportService.EnsureDirectory(preparation.ReleaseZipOutputPath);
        _support.TryWritePlan(plan, preparation.PlanOutputPath);

        spec.WhatIf = false;
        spec.RemotePublishAttempted = remotePublishAttempted;
        var releaseWatch = Stopwatch.StartNew();
        cancellationToken.ThrowIfCancellationRequested();
        var release = _executeRelease(spec, _signAssemblies, _validateAssemblySigning, progress, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        releaseWatch.Stop();
        string? releaseFailureReport = release is null
            ? "Project build failed. Cause: The release pipeline returned no result."
            : release.Success
                ? null
                : new DotNetRepositoryReleaseSummaryService().CreateFailureReport(release);
        if (release is not null && release.Success)
            _logger.Success($"Project build release execution completed in {DotNetRepositoryReleaseService.FormatDuration(releaseWatch.Elapsed)}.");
        else
            _logger.Error(
                $"Project build release execution failed after {DotNetRepositoryReleaseService.FormatDuration(releaseWatch.Elapsed)}. " +
                releaseFailureReport);

        var result = new ProjectBuildResult { Release = release };

        if (release is null || !release.Success)
        {
            result.Success = false;
            result.ErrorMessage = releaseFailureReport;
            return new ProjectBuildWorkflowResult { Result = result };
        }

        if (!preparation.PublishGitHub)
        {
            result.Success = true;
            return new ProjectBuildWorkflowResult { Result = result };
        }

        gitHubToken ??= preparation.GitHubToken;
        if (string.IsNullOrWhiteSpace(gitHubToken))
        {
            result.Success = false;
            result.ErrorMessage = "GitHub access token is required for GitHub publishing.";
            return new ProjectBuildWorkflowResult { Result = result };
        }

        if (string.IsNullOrWhiteSpace(config.GitHubUsername) || string.IsNullOrWhiteSpace(config.GitHubRepositoryName))
        {
            result.Success = false;
            result.ErrorMessage = "GitHubUsername and GitHubRepositoryName are required for GitHub publishing.";
            return new ProjectBuildWorkflowResult { Result = result };
        }

        var gitHubWatch = Stopwatch.StartNew();
        progress?.PhaseStarted(ProjectBuildProgressPhase.GitHubPublish, 1, "Publishing GitHub release");
        remotePublishAttempted?.Invoke();
        var publishSummary = _publishGitHub(new ProjectBuildGitHubPublishRequest
        {
            Owner = config.GitHubUsername!,
            Repository = config.GitHubRepositoryName!,
            Token = gitHubToken!,
            Release = release,
            ReleaseMode = config.GitHubReleaseMode ?? "Single",
            IncludeProjectNameInTag = config.GitHubIncludeProjectNameInTag,
            IsPreRelease = config.GitHubIsPreRelease,
            GenerateReleaseNotes = config.GitHubGenerateReleaseNotes,
            PublishFailFast = spec.PublishFailFast,
            ReleaseName = config.GitHubReleaseName,
            TagName = config.GitHubTagName,
            TagTemplate = config.GitHubTagTemplate,
            PrimaryProject = config.GitHubPrimaryProject,
            TagConflictPolicy = config.GitHubTagConflictPolicy,
            Progress = progress as IProjectBuildProgressReporterV2
        });
        gitHubWatch.Stop();
        if (publishSummary.Success)
        {
            _logger.Success($"GitHub publish completed in {DotNetRepositoryReleaseService.FormatDuration(gitHubWatch.Elapsed)}.");
            progress?.PhaseCompleted(
                ProjectBuildProgressPhase.GitHubPublish,
                $"{publishSummary.Results.Count} release result(s), {DotNetRepositoryReleaseService.FormatDuration(gitHubWatch.Elapsed)}");
        }
        else
        {
            _logger.Warn($"GitHub publish failed after {DotNetRepositoryReleaseService.FormatDuration(gitHubWatch.Elapsed)}.");
            progress?.PhaseFailed(ProjectBuildProgressPhase.GitHubPublish, publishSummary.ErrorMessage);
        }

        result.GitHub.AddRange(publishSummary.Results);
        result.Success = publishSummary.Success;
        result.ErrorMessage = publishSummary.ErrorMessage;
        if (result.ErrorMessage is null)
            result.Success = result.GitHub.Count == 0 || result.GitHub.TrueForAll(gitHub => gitHub.Success);

        return new ProjectBuildWorkflowResult
        {
            Result = result,
            GitHubPublishSummary = publishSummary
        };
    }

    private static bool TryCreateReusablePlanVersions(
        IReadOnlyDictionary<string, string> resolvedVersions,
        out Dictionary<string, string> reusableVersions)
    {
        reusableVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (resolvedVersions.Count == 0)
            return false;

        foreach (var pair in resolvedVersions)
        {
            if (!PackageVersionUtility.TryNormalizeExact(pair.Value, out var normalized))
            {
                reusableVersions.Clear();
                return false;
            }

            reusableVersions[pair.Key] = normalized;
        }

        return true;
    }

    private static ProjectBuildResult CreateResult(IReadOnlyCollection<string> errors, DotNetRepositoryReleaseResult plan)
    {
        return new ProjectBuildResult
        {
            Success = errors.Count == 0,
            ErrorMessage = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors),
            Release = plan
        };
    }
}
