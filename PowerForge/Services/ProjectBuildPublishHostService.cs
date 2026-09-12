namespace PowerForge;

/// <summary>
/// Host-facing service for resolving project publish settings and invoking shared GitHub publish logic.
/// </summary>
public sealed class ProjectBuildPublishHostService
{
    private readonly ILogger _logger;
    private readonly Func<ProjectBuildGitHubPublishRequest, ProjectBuildGitHubPublishSummary>? _publishGitHub;
    private readonly DotNetRepositoryReleaseService? _publishPackages;

    /// <summary>
    /// Creates a new host service using a null logger.
    /// </summary>
    public ProjectBuildPublishHostService()
        : this(new NullLogger())
    {
    }

    /// <summary>
    /// Creates a new host service using the provided logger.
    /// </summary>
    public ProjectBuildPublishHostService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    internal ProjectBuildPublishHostService(
        ILogger logger,
        Func<ProjectBuildGitHubPublishRequest, ProjectBuildGitHubPublishSummary>? publishGitHub,
        DotNetRepositoryReleaseService? publishPackages = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _publishGitHub = publishGitHub;
        _publishPackages = publishPackages;
    }

    /// <summary>
    /// Loads publish-related settings from <c>project.build.json</c> and resolves secrets.
    /// </summary>
    public ProjectBuildPublishHostConfiguration LoadConfiguration(string configPath)
    {
        FrameworkCompatibility.NotNullOrWhiteSpace(configPath, nameof(configPath));

        var resolvedConfigPath = PathValueResolver.Resolve(Directory.GetCurrentDirectory(), configPath);
        var configDirectory = Path.GetDirectoryName(resolvedConfigPath);
        if (string.IsNullOrWhiteSpace(configDirectory))
            throw new InvalidOperationException($"Unable to resolve the configuration directory for '{resolvedConfigPath}'.");

        var config = new ProjectBuildSupportService(_logger).LoadConfig(resolvedConfigPath);
        return CreateHostConfiguration(config, resolvedConfigPath, configDirectory);
    }

    /// <summary>
    /// Resolves publish settings for an inline module package-build configuration.
    /// </summary>
    public ProjectBuildPublishHostConfiguration LoadConfiguration(
        PackageBuildConfiguration configuration,
        string sourceConfigPath)
    {
        FrameworkCompatibility.NotNull(configuration, nameof(configuration));
        FrameworkCompatibility.NotNullOrWhiteSpace(sourceConfigPath, nameof(sourceConfigPath));

        var resolvedConfigPath = PathValueResolver.Resolve(Directory.GetCurrentDirectory(), sourceConfigPath);
        var configDirectory = Path.GetDirectoryName(resolvedConfigPath);
        if (string.IsNullOrWhiteSpace(configDirectory))
            throw new InvalidOperationException($"Unable to resolve the configuration directory for '{resolvedConfigPath}'.");

        var config = ProjectBuildConfigurationAdapter.FromPackageBuild(configuration);
        return CreateHostConfiguration(config, resolvedConfigPath, configDirectory);
    }

    /// <summary>
    /// Resolves publish settings from a referenced project-build configuration with module-lane overrides applied.
    /// </summary>
    public ProjectBuildPublishHostConfiguration LoadConfiguration(
        ProjectBuildConfigurationReference reference,
        string configPath)
    {
        FrameworkCompatibility.NotNull(reference, nameof(reference));
        FrameworkCompatibility.NotNullOrWhiteSpace(configPath, nameof(configPath));

        var resolvedConfigPath = PathValueResolver.Resolve(Directory.GetCurrentDirectory(), configPath);
        var configDirectory = Path.GetDirectoryName(resolvedConfigPath);
        if (string.IsNullOrWhiteSpace(configDirectory))
            throw new InvalidOperationException($"Unable to resolve the configuration directory for '{resolvedConfigPath}'.");

        var config = new ProjectBuildSupportService(_logger).LoadConfig(resolvedConfigPath);
        ProjectBuildConfigurationAdapter.ApplyReference(config, reference);
        return CreateHostConfiguration(config, resolvedConfigPath, configDirectory);
    }

    internal static ProjectBuildPublishHostConfiguration CreateHostConfiguration(
        ProjectBuildConfiguration config,
        string resolvedConfigPath,
        string configDirectory)
    {
        var feed = ProjectBuildPackageFeedResolver.Resolve(config, configDirectory);
        var publishSource = string.IsNullOrWhiteSpace(feed.PublishSource)
            ? ProjectBuildPackageFeedResolver.GetDefaultPublishSource()
            : feed.PublishSource!.Trim();
        var releaseMode = string.IsNullOrWhiteSpace(config.GitHubReleaseMode)
            ? "Single"
            : config.GitHubReleaseMode!.Trim();
        return new ProjectBuildPublishHostConfiguration {
            PublicationSpec = new DotNetRepositoryReleaseSpec {
                RootPath = ProjectBuildSupportService.ResolveOptionalPath(config.RootPath, configDirectory) ?? configDirectory,
                PublishSource = publishSource,
                PublishApiKey = feed.PublishApiKey,
                VersionSources = feed.VersionSources,
                VersionSourceCredential = feed.VersionSourceCredential,
                VersionSourceCredentials = feed.VersionSourceCredentials,
                IncludePrerelease = config.IncludePrerelease,
                IncludeSymbols = config.IncludeSymbols ?? false,
                SkipDuplicate = config.SkipDuplicate ?? true,
                PublishFailFast = config.PublishFailFast ?? true
            },
            ConfigPath = resolvedConfigPath,
            PublishNuget = config.PublishNuget == true,
            PublishGitHub = config.PublishGitHub == true,
            PublishSource = publishSource,
            PublishApiKey = feed.PublishApiKey,
            GitHubToken = feed.GitHubToken,
            GitHubUsername = TrimOrNull(config.GitHubUsername),
            GitHubRepositoryName = TrimOrNull(config.GitHubRepositoryName),
            GitHubIsPreRelease = config.GitHubIsPreRelease,
            GitHubIncludeProjectNameInTag = config.GitHubIncludeProjectNameInTag,
            GitHubGenerateReleaseNotes = config.GitHubGenerateReleaseNotes,
            GitHubReleaseName = TrimOrNull(config.GitHubReleaseName),
            GitHubTagName = TrimOrNull(config.GitHubTagName),
            GitHubTagTemplate = TrimOrNull(config.GitHubTagTemplate),
            GitHubReleaseMode = releaseMode,
            GitHubPrimaryProject = TrimOrNull(config.GitHubPrimaryProject),
            GitHubTagConflictPolicy = TrimOrNull(config.GitHubTagConflictPolicy),
            PublishFailFast = config.PublishFailFast ?? true,
            SkipDuplicate = config.SkipDuplicate ?? true,
            IncludeSymbols = config.IncludeSymbols ?? false
        };
    }

    /// <summary>
    /// Publishes the exact package files recorded in an existing release result without rebuilding them.
    /// </summary>
    internal NuGetPackagePublishResult PublishNuGet(
        ProjectBuildPublishHostConfiguration configuration,
        DotNetRepositoryReleaseResult release,
        string? repositoryRoot = null,
        Action? remotePublishAttempted = null,
        IProjectBuildProgressReporter? progress = null,
        CancellationToken cancellationToken = default)
    {
        FrameworkCompatibility.NotNull(configuration, nameof(configuration));
        FrameworkCompatibility.NotNull(release, nameof(release));
        if (!configuration.PublishNuget)
            throw new InvalidOperationException("NuGet publishing is not enabled for this package lane.");
        if (string.IsNullOrWhiteSpace(configuration.PublishApiKey))
            throw new InvalidOperationException("PublishApiKey is required when package NuGet publishing is enabled.");

        var policy = configuration.PublicationSpec ?? new DotNetRepositoryReleaseSpec {
            RootPath = repositoryRoot ?? Path.GetDirectoryName(configuration.ConfigPath) ?? string.Empty,
            PublishSource = configuration.PublishSource,
            PublishApiKey = configuration.PublishApiKey,
            IncludeSymbols = configuration.IncludeSymbols,
            SkipDuplicate = configuration.SkipDuplicate,
            PublishFailFast = configuration.PublishFailFast
        };
        var validateContext = configuration.ValidatePublicationContext
            ?? DotNetRepositoryReleaseService.CapturePublicationDestination(policy);
        validateContext?.Invoke();
        policy.RemotePublishAttempted = () => {
            cancellationToken.ThrowIfCancellationRequested();
            validateContext?.Invoke();
            remotePublishAttempted?.Invoke();
        };
        return (_publishPackages ?? new DotNetRepositoryReleaseService(_logger))
            .PublishExistingPackages(policy, release, progress, cancellationToken);
    }

    /// <summary>
    /// Publishes GitHub releases for the provided project release plan using shared PowerForge logic.
    /// </summary>
    public ProjectBuildGitHubPublishSummary PublishGitHub(ProjectBuildPublishHostConfiguration configuration, DotNetRepositoryReleaseResult release)
        => PublishGitHub(configuration, release, progress: null);

    /// <summary>
    /// Publishes GitHub releases and reports durable per-asset progress when a detailed
    /// project-build reporter is supplied.
    /// </summary>
    public ProjectBuildGitHubPublishSummary PublishGitHub(
        ProjectBuildPublishHostConfiguration configuration,
        DotNetRepositoryReleaseResult release,
        IProjectBuildProgressReporter? progress)
    {
        FrameworkCompatibility.NotNull(configuration, nameof(configuration));
        FrameworkCompatibility.NotNull(release, nameof(release));

        var request = new ProjectBuildGitHubPublishRequest {
            Owner = configuration.GitHubUsername ?? string.Empty,
            Repository = configuration.GitHubRepositoryName ?? string.Empty,
            Token = configuration.GitHubToken ?? string.Empty,
            Release = release,
            ReleaseMode = configuration.GitHubReleaseMode,
            IncludeProjectNameInTag = configuration.GitHubIncludeProjectNameInTag,
            IsPreRelease = configuration.GitHubIsPreRelease,
            GenerateReleaseNotes = configuration.GitHubGenerateReleaseNotes,
            ReleaseName = configuration.GitHubReleaseName,
            TagName = configuration.GitHubTagName,
            TagTemplate = configuration.GitHubTagTemplate,
            PrimaryProject = configuration.GitHubPrimaryProject,
            TagConflictPolicy = configuration.GitHubTagConflictPolicy,
            PublishFailFast = configuration.PublishFailFast,
            Progress = progress as IProjectBuildProgressReporterV2
        };

        return (_publishGitHub ?? (publishRequest => new ProjectBuildGitHubPublisher(_logger).Publish(publishRequest)))(request);
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
