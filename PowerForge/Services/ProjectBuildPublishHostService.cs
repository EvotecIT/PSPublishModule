namespace PowerForge;

/// <summary>
/// Host-facing service for resolving project publish settings and invoking shared GitHub publish logic.
/// </summary>
public sealed class ProjectBuildPublishHostService
{
    private readonly ILogger _logger;
    private readonly Func<ProjectBuildGitHubPublishRequest, ProjectBuildGitHubPublishSummary>? _publishGitHub;

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
        Func<ProjectBuildGitHubPublishRequest, ProjectBuildGitHubPublishSummary>? publishGitHub)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _publishGitHub = publishGitHub;
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

    private static ProjectBuildPublishHostConfiguration CreateHostConfiguration(
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

        var configDirectory = Path.GetDirectoryName(configuration.ConfigPath);
        if (string.IsNullOrWhiteSpace(configDirectory))
            throw new InvalidOperationException($"Unable to resolve the configuration directory for '{configuration.ConfigPath}'.");
        var source = DotNetRepositoryReleaseService.ResolvePublishSource(
            configuration.PublishSource,
            string.IsNullOrWhiteSpace(repositoryRoot) ? configDirectory : repositoryRoot!,
            nuGetConfigSearchRoot: configDirectory);
        release.PublishSource = source;
        var publishSymbolsSeparately = configuration.IncludeSymbols &&
            DotNetRepositoryReleaseService.IsLocalPublishSource(source);
        var packages = DotNetRepositoryReleaseService.GetPackagesForPublish(
            release.Projects,
            includeSymbolPackages: publishSymbolsSeparately);
        if (packages.Length == 0)
            throw new InvalidOperationException("The package release checkpoint contains no package artifacts to publish.");

        _logger.Info($"Publishing {packages.Length} existing package(s) from the staged release checkpoint.");
        var publish = new NuGetPackagePublishService(
            _logger,
            workingDirectory: configDirectory).ExecutePackages(
            packages,
            configuration.PublishApiKey!,
            source,
            configuration.SkipDuplicate,
            configuration.PublishFailFast,
            suppressCompanionSymbols: !configuration.IncludeSymbols || publishSymbolsSeparately,
            remotePublishAttempted: remotePublishAttempted,
            progress: progress,
            cancellationToken: cancellationToken);

        DotNetRepositoryReleaseService.ApplyPublishedNuGetArtifactOutcomes(
            release,
            publish,
            publishSymbolsSeparately,
            configuration.SkipDuplicate);
        if (!publish.Success)
        {
            release.Success = false;
            release.ErrorMessage = publish.ErrorMessage ?? "One or more packages failed to publish.";
        }

        return publish;
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
