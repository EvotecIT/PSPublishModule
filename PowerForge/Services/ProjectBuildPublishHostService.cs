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

        var resolvedConfigPath = Path.GetFullPath(configPath.Trim().Trim('"'));
        var configDirectory = Path.GetDirectoryName(resolvedConfigPath);
        if (string.IsNullOrWhiteSpace(configDirectory))
            throw new InvalidOperationException($"Unable to resolve the configuration directory for '{resolvedConfigPath}'.");

        var config = new ProjectBuildSupportService(_logger).LoadConfig(resolvedConfigPath);
        var publishSource = string.IsNullOrWhiteSpace(config.PublishSource)
            ? "https://api.nuget.org/v3/index.json"
            : config.PublishSource!.Trim();
        var releaseMode = string.IsNullOrWhiteSpace(config.GitHubReleaseMode)
            ? "Single"
            : config.GitHubReleaseMode!.Trim();
        return new ProjectBuildPublishHostConfiguration {
            ConfigPath = resolvedConfigPath,
            PublishNuget = config.PublishNuget == true,
            PublishGitHub = config.PublishGitHub == true,
            PublishSource = publishSource,
            PublishApiKey = ProjectBuildSupportService.ResolveSecret(
                config.PublishApiKey,
                config.PublishApiKeyFilePath,
                config.PublishApiKeyEnvName,
                configDirectory),
            GitHubToken = ProjectBuildSupportService.ResolveSecret(
                config.GitHubAccessToken,
                config.GitHubAccessTokenFilePath,
                config.GitHubAccessTokenEnvName,
                configDirectory),
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
            GitHubTagConflictPolicy = TrimOrNull(config.GitHubTagConflictPolicy)
        };
    }

    /// <summary>
    /// Publishes GitHub releases for the provided project release plan using shared PowerForge logic.
    /// </summary>
    public ProjectBuildGitHubPublishSummary PublishGitHub(ProjectBuildPublishHostConfiguration configuration, DotNetRepositoryReleaseResult release)
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
            TagConflictPolicy = configuration.GitHubTagConflictPolicy
        };

        return (_publishGitHub ?? (publishRequest => new ProjectBuildGitHubPublisher(_logger).Publish(publishRequest)))(request);
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
