namespace PowerForge;

internal sealed class ProjectBuildWorkflowService
{
    private readonly ILogger _logger;
    private readonly ProjectBuildSupportService _support;
    private readonly Func<DotNetRepositoryReleaseSpec, DotNetRepositoryReleaseResult> _executeRelease;
    private readonly Func<ProjectBuildGitHubPublishRequest, ProjectBuildGitHubPublishSummary> _publishGitHub;
    private readonly Func<ProjectBuildConfiguration, DotNetRepositoryReleaseResult, string, string?> _validateGitHubPreflight;

    public ProjectBuildWorkflowService(
        ILogger logger,
        Func<DotNetRepositoryReleaseSpec, DotNetRepositoryReleaseResult>? executeRelease = null,
        Func<ProjectBuildGitHubPublishRequest, ProjectBuildGitHubPublishSummary>? publishGitHub = null,
        Func<ProjectBuildConfiguration, DotNetRepositoryReleaseResult, string, string?>? validateGitHubPreflight = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _support = new ProjectBuildSupportService(_logger);
        _executeRelease = executeRelease ?? (spec => new DotNetRepositoryReleaseService(_logger).Execute(spec));
        _publishGitHub = publishGitHub ?? (request => new ProjectBuildGitHubPublisher(_logger).Publish(request));
        _validateGitHubPreflight = validateGitHubPreflight ?? ((config, plan, token) =>
            new ProjectBuildGitHubPreflightService(_logger).Validate(config, plan, token));
    }

    public ProjectBuildWorkflowResult Execute(
        ProjectBuildConfiguration config,
        string configDir,
        ProjectBuildPreparedContext preparation,
        bool executeBuild)
    {
        if (config is null)
            throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(configDir))
            throw new ArgumentException("Configuration directory is required.", nameof(configDir));
        if (preparation is null)
            throw new ArgumentNullException(nameof(preparation));

        var spec = preparation.Spec ?? throw new ArgumentException("Prepared spec is required.", nameof(preparation));
        spec.WhatIf = true;
        var plan = _executeRelease(spec);
        var preflightErrors = new List<string>();
        if (!plan.Success)
            preflightErrors.Add(plan.ErrorMessage ?? "Plan/preflight validation failed.");

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
        if (preparation.PublishGitHub && string.IsNullOrWhiteSpace(preflightError))
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
        var release = _executeRelease(spec);
        var result = new ProjectBuildResult { Release = release };

        if (release is null || !release.Success)
        {
            result.Success = false;
            result.ErrorMessage = release?.ErrorMessage ?? "Release pipeline failed.";
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
