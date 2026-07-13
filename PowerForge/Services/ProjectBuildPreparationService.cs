namespace PowerForge;

/// <summary>
/// Resolves project build action flags, credentials, paths, and repository release specifications.
/// </summary>
internal sealed class ProjectBuildPreparationService
{
    private readonly ILogger _logger;

    public ProjectBuildPreparationService()
        : this(new NullLogger())
    {
    }

    public ProjectBuildPreparationService(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Builds the prepared execution context for the project build workflow.
    /// </summary>
    public ProjectBuildPreparedContext Prepare(
        ProjectBuildConfiguration config,
        string configDir,
        string? planPath,
        ProjectBuildRequestedActions requestedActions)
    {
        if (config is null)
            throw new ArgumentNullException(nameof(config));
        if (string.IsNullOrWhiteSpace(configDir))
            throw new ArgumentException("Configuration directory is required.", nameof(configDir));
        if (requestedActions is null)
            throw new ArgumentNullException(nameof(requestedActions));

        var anyConfigSpecified = config.UpdateVersions is not null ||
                                 config.Build is not null ||
                                 config.PublishNuget is not null ||
                                 config.PublishGitHub is not null;
        var anyOverrideSpecified = requestedActions.UpdateVersions is not null ||
                                   requestedActions.Build is not null ||
                                   requestedActions.PublishNuget is not null ||
                                   requestedActions.PublishGitHub is not null;

        var context = new ProjectBuildPreparedContext
        {
            PlanOnly = requestedActions.PlanOnly ?? (config.PlanOnly ?? false),
            UpdateVersions = requestedActions.UpdateVersions ?? (config.UpdateVersions ?? false),
            Build = requestedActions.Build ?? (config.Build ?? false),
            PublishNuget = requestedActions.PublishNuget ?? (config.PublishNuget ?? false),
            PublishGitHub = requestedActions.PublishGitHub ?? (config.PublishGitHub ?? false),
            CreateReleaseZip = config.CreateReleaseZip ?? true
        };

        if (!anyConfigSpecified && !anyOverrideSpecified)
        {
            context.UpdateVersions = true;
            context.Build = true;
            context.PublishNuget = true;
            context.PublishGitHub = true;
        }

        context.RootPath = ProjectBuildSupportService.ResolveOptionalPath(config.RootPath, configDir) ?? configDir;
        context.StagingPath = ProjectBuildSupportService.ResolveOptionalPath(config.StagingPath, context.RootPath);
        context.OutputPath = ProjectBuildSupportService.ResolveOptionalPath(config.OutputPath, context.RootPath);
        context.ReleaseZipOutputPath = ProjectBuildSupportService.ResolveOptionalPath(config.ReleaseZipOutputPath, context.RootPath);
        context.PlanOutputPath = ProjectBuildSupportService.ResolveOptionalPath(planPath ?? config.PlanOutputPath, configDir);

        if (string.IsNullOrWhiteSpace(context.OutputPath) && !string.IsNullOrWhiteSpace(context.StagingPath))
            context.OutputPath = Path.Combine(context.StagingPath, "packages");
        if (string.IsNullOrWhiteSpace(context.ReleaseZipOutputPath) && !string.IsNullOrWhiteSpace(context.StagingPath))
            context.ReleaseZipOutputPath = Path.Combine(context.StagingPath, "releases");

        var feed = ProjectBuildPackageFeedResolver.Resolve(config, configDir);
        var nugetCredential = feed.VersionSourceCredential;
        var expectedVersionMap = new ProjectBuildVersionTrackService(_logger).ResolveExpectedVersionMap(
            config,
            feed.VersionSources,
            nugetCredential,
            feed.VersionSourceCredentials);

        context.PublishApiKey = feed.PublishApiKey;
        context.GitHubToken = feed.GitHubToken;

        var packStrategy = ProjectBuildSupportService.ParsePackStrategy(config.PackStrategy);
        if (!ProjectBuildSupportService.IsKnownPackStrategy(config.PackStrategy))
            _logger.Warn($"Unknown PackStrategy '{config.PackStrategy!.Trim()}'; using PerProject.");

        context.Spec = new DotNetRepositoryReleaseSpec
        {
            RootPath = context.RootPath,
            ExpectedVersion = config.ExpectedVersion,
            ExpectedVersionsByProject = expectedVersionMap,
            ExpectedVersionMapAsInclude = config.ExpectedVersionMapAsInclude,
            ExpectedVersionMapUseWildcards = config.ExpectedVersionMapUseWildcards,
            IncludeProjects = config.IncludeProjects,
            ExcludeProjects = config.ExcludeProjects,
            ExcludeDirectories = config.ExcludeDirectories,
            VersionSources = feed.VersionSources,
            VersionSourceCredential = nugetCredential,
            VersionSourceCredentials = feed.VersionSourceCredentials,
            IncludePrerelease = config.IncludePrerelease,
            Configuration = string.IsNullOrWhiteSpace(config.Configuration) ? "Release" : config.Configuration!,
            OutputPath = context.OutputPath,
            ReleaseZipOutputPath = context.ReleaseZipOutputPath,
            PackStrategy = packStrategy,
            IncludeSymbols = config.IncludeSymbols ?? false,
            CertificateThumbprint = config.CertificateThumbprint,
            CertificateStore = ProjectBuildSupportService.ParseCertificateStore(config.CertificateStore),
            TimeStampServer = config.TimeStampServer,
            SignAssemblies = ResolveSigningEnabled(config.SignAssemblies, config.CertificateThumbprint),
            SignDependencyAssemblies = config.SignDependencyAssemblies ?? false,
            SignPackages = ResolveSigningEnabled(config.SignPackages, config.CertificateThumbprint),
            Pack = context.Build || context.PublishNuget || context.PublishGitHub,
            CreateReleaseZip = context.CreateReleaseZip,
            Publish = context.PublishNuget,
            PublishSource = feed.PublishSource,
            PublishApiKey = context.PublishApiKey,
            SkipDuplicate = config.SkipDuplicate ?? true,
            PublishFailFast = config.PublishFailFast ?? true,
            UpdateVersions = context.UpdateVersions
        };

        return context;
    }

    private static bool ResolveSigningEnabled(bool? configuredValue, string? certificateThumbprint)
        => configuredValue ?? !string.IsNullOrWhiteSpace(certificateThumbprint);
}
