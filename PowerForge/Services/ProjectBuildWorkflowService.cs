using System.Diagnostics;

namespace PowerForge;

internal sealed class ProjectBuildWorkflowService
{
    private readonly ILogger _logger;
    private readonly ProjectBuildSupportService _support;
    private readonly Func<DotNetRepositoryReleaseSpec, Action<DotNetReleaseBuildAssemblySigningRequest>?, Action<DotNetReleaseBuildAssemblySigningPreflightRequest>?, IProjectBuildProgressReporter?, DotNetRepositoryReleaseResult> _executeRelease;
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
            ? (spec, signing, preflight, progress) => new DotNetRepositoryReleaseService(_logger).Execute(spec, signing, preflight, progress)
            : (spec, _, _, _) => executeRelease(spec);
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
        IProjectBuildProgressReporter? progress = null)
    {
        if (config is null)
            throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(configDir))
            throw new ArgumentException("Configuration directory is required.", nameof(configDir));
        if (preparation is null)
            throw new ArgumentNullException(nameof(preparation));

        var spec = preparation.Spec ?? throw new ArgumentException("Prepared spec is required.", nameof(preparation));
        spec.WhatIf = true;
        progress?.PhaseStarted(ProjectBuildProgressPhase.Plan, 1, "Discovering projects and resolving versions");
        var planWatch = Stopwatch.StartNew();
        var plan = _executeRelease(spec, _signAssemblies, _validateAssemblySigning, null);
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
        else if (plan.ResolvedVersionsByProject.Count > 0)
        {
            spec.ExpectedVersion = null;
            spec.ExpectedVersionsByProject = new Dictionary<string, string>(
                plan.ResolvedVersionsByProject,
                StringComparer.OrdinalIgnoreCase);
            _logger.Info($"Reusing {plan.ResolvedVersionsByProject.Count} resolved project version(s) from the plan for release execution.");
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
        var release = _executeRelease(spec, _signAssemblies, _validateAssemblySigning, progress);
        releaseWatch.Stop();
        if (release is not null && release.Success)
            _logger.Success($"Project build release execution completed in {DotNetRepositoryReleaseService.FormatDuration(releaseWatch.Elapsed)}.");
        else
            _logger.Error($"Project build release execution failed after {DotNetRepositoryReleaseService.FormatDuration(releaseWatch.Elapsed)}.");

        var result = new ProjectBuildResult { Release = release };

        if (release is null || !release.Success)
        {
            result.Success = false;
            result.ErrorMessage = release is null
                ? "Project build failed. Cause: The release pipeline returned no result."
                : new DotNetRepositoryReleaseSummaryService().CreateFailureReport(release);
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
            TagConflictPolicy = config.GitHubTagConflictPolicy
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
