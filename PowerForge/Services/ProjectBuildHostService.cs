namespace PowerForge;

/// <summary>
/// Host-facing service for planning or executing repository project builds from <c>project.build.json</c>.
/// </summary>
public sealed class ProjectBuildHostService
{
    private readonly ILogger _logger;
    private readonly Func<DotNetRepositoryReleaseSpec, DotNetRepositoryReleaseResult>? _executeRelease;
    private readonly Func<ProjectBuildGitHubPublishRequest, ProjectBuildGitHubPublishSummary>? _publishGitHub;
    private readonly Func<ProjectBuildConfiguration, DotNetRepositoryReleaseResult, string, string?>? _validateGitHubPreflight;
    private readonly Action<DotNetReleaseBuildAssemblySigningRequest>? _signAssemblies;
    private readonly Action<DotNetReleaseBuildAssemblySigningPreflightRequest>? _validateAssemblySigning;

    /// <summary>
    /// Creates a new host service using a null logger.
    /// </summary>
    public ProjectBuildHostService()
        : this(new NullLogger())
    {
    }

    /// <summary>
    /// Creates a new host service using the provided logger.
    /// </summary>
    /// <param name="logger">Logger used by the underlying workflow.</param>
    public ProjectBuildHostService(ILogger logger)
        : this(logger, null)
    {
    }

    /// <summary>
    /// Creates a new host service using the provided logger and optional assembly signing handler.
    /// </summary>
    /// <param name="logger">Logger used by the underlying workflow.</param>
    /// <param name="signAssemblies">Optional handler used to sign build outputs before packages are created.</param>
    /// <param name="validateAssemblySigning">Optional handler used to validate assembly signing before mutable release steps run.</param>
    public ProjectBuildHostService(
        ILogger logger,
        Action<DotNetReleaseBuildAssemblySigningRequest>? signAssemblies,
        Action<DotNetReleaseBuildAssemblySigningPreflightRequest>? validateAssemblySigning = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _signAssemblies = signAssemblies;
        _validateAssemblySigning = validateAssemblySigning;
    }

    internal ProjectBuildHostService(
        ILogger logger,
        Func<DotNetRepositoryReleaseSpec, DotNetRepositoryReleaseResult>? executeRelease,
        Func<ProjectBuildGitHubPublishRequest, ProjectBuildGitHubPublishSummary>? publishGitHub,
        Func<ProjectBuildConfiguration, DotNetRepositoryReleaseResult, string, string?>? validateGitHubPreflight,
        Action<DotNetReleaseBuildAssemblySigningRequest>? signAssemblies = null,
        Action<DotNetReleaseBuildAssemblySigningPreflightRequest>? validateAssemblySigning = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _executeRelease = executeRelease;
        _publishGitHub = publishGitHub;
        _validateGitHubPreflight = validateGitHubPreflight;
        _signAssemblies = signAssemblies;
        _validateAssemblySigning = validateAssemblySigning;
    }

    /// <summary>
    /// Executes the requested project build workflow.
    /// </summary>
    /// <param name="request">Host execution request.</param>
    /// <returns>Execution result including resolved paths and the underlying workflow result.</returns>
    public ProjectBuildHostExecutionResult Execute(ProjectBuildHostRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.ConfigPath))
            throw new ArgumentException("ConfigPath is required.", nameof(request));

        var startedAt = DateTimeOffset.UtcNow;
        var configPath = PathValueResolver.Resolve(Directory.GetCurrentDirectory(), request.ConfigPath);
        var configDirectory = Path.GetDirectoryName(configPath);
        if (string.IsNullOrWhiteSpace(configDirectory))
            throw new InvalidOperationException($"Unable to resolve the configuration directory for '{configPath}'.");

        var support = new ProjectBuildSupportService(_logger);
        var config = support.LoadConfig(configPath);
        return ExecuteCore(request, config, configPath, configDirectory, startedAt);
    }

    /// <summary>
    /// Executes the requested project build workflow using an already loaded configuration object.
    /// </summary>
    /// <param name="request">Host execution request.</param>
    /// <param name="config">Loaded configuration to execute.</param>
    /// <param name="configPath">Source configuration path used for resolving relative values and reporting.</param>
    internal ProjectBuildHostExecutionResult Execute(ProjectBuildHostRequest request, ProjectBuildConfiguration config, string configPath)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (config is null)
            throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(configPath))
            throw new ArgumentException("Config path is required.", nameof(configPath));

        var startedAt = DateTimeOffset.UtcNow;
        var fullConfigPath = PathValueResolver.Resolve(Directory.GetCurrentDirectory(), configPath);
        var configDirectory = Path.GetDirectoryName(fullConfigPath);
        if (string.IsNullOrWhiteSpace(configDirectory))
            throw new InvalidOperationException($"Unable to resolve the configuration directory for '{fullConfigPath}'.");

        return ExecuteCore(request, config, fullConfigPath, configDirectory, startedAt);
    }

    private ProjectBuildHostExecutionResult ExecuteCore(
        ProjectBuildHostRequest request,
        ProjectBuildConfiguration config,
        string configPath,
        string configDirectory,
        DateTimeOffset startedAt)
    {
        var preparation = new ProjectBuildPreparationService().Prepare(
            config,
            configDirectory,
            request.PlanOutputPath,
            new ProjectBuildRequestedActions {
                PlanOnly = request.PlanOnly,
                UpdateVersions = request.UpdateVersions,
                Build = request.Build,
                PublishNuget = request.PublishNuget,
                PublishGitHub = request.PublishGitHub
            });

        var workflow = new ProjectBuildWorkflowService(
            _logger,
            _executeRelease,
            _publishGitHub,
            _validateGitHubPreflight,
            _signAssemblies,
            _validateAssemblySigning)
            .Execute(
                config,
                configDirectory,
                preparation,
                request.ExecuteBuild,
                request.RemotePublishAttempted);

        return new ProjectBuildHostExecutionResult {
            Success = workflow.Result.Success,
            ErrorMessage = workflow.Result.ErrorMessage,
            ConfigPath = configPath,
            RootPath = preparation.RootPath,
            StagingPath = preparation.StagingPath,
            OutputPath = preparation.OutputPath,
            ReleaseZipOutputPath = preparation.ReleaseZipOutputPath,
            PlanOutputPath = preparation.PlanOutputPath,
            Duration = DateTimeOffset.UtcNow - startedAt,
            Result = workflow.Result
        };
    }
}
