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

        var actions = ResolveActions(config, requestedActions);
        var stageNuget = requestedActions.StageNuget ?? config.StageNuget ?? false;
        if (stageNuget && requestedActions.PublishGitHub == false)
            throw new InvalidOperationException("StageNuget cannot create a draft because GitHub publication was explicitly disabled by the caller.");
        if (stageNuget && (!ResolveSigningEnabled(config.SignPackages, config.CertificateThumbprint) ||
                           !ResolveSigningEnabled(config.SignAssemblies, config.CertificateThumbprint)))
            throw new InvalidOperationException("StageNuget requires assembly and package signing.");
        if (stageNuget && !string.Equals(config.GitHubReleaseMode ?? "Single", "Single", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("StageNuget requires GitHubReleaseMode Single.");
        if (stageNuget && !string.Equals(config.GitHubTagConflictPolicy ?? "Fail", "Fail", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("StageNuget requires GitHubTagConflictPolicy Fail; published or existing drafts must not be silently reused.");
        var context = new ProjectBuildPreparedContext
        {
            PlanOnly = requestedActions.PlanOnly ?? (config.PlanOnly ?? false),
            UpdateVersions = actions.UpdateVersions,
            Build = stageNuget || actions.Build,
            StageNuget = stageNuget,
            PublishNuget = !stageNuget && actions.PublishNuGet,
            PublishGitHub = stageNuget || actions.PublishGitHub,
            CreateReleaseZip = !stageNuget && (config.CreateReleaseZip ?? true)
        };

        context.RootPath = ProjectBuildSupportService.ResolveOptionalPath(config.RootPath, configDir) ?? configDir;
        context.StagingPath = ProjectBuildSupportService.ResolveOptionalPath(config.StagingPath, context.RootPath);
        context.OutputPath = ProjectBuildSupportService.ResolveOptionalPath(config.OutputPath, context.RootPath);
        context.ReleaseZipOutputPath = ProjectBuildSupportService.ResolveOptionalPath(config.ReleaseZipOutputPath, context.RootPath);
        context.PlanOutputPath = ProjectBuildSupportService.ResolveOptionalPath(planPath ?? config.PlanOutputPath, configDir);

        if (stageNuget && string.IsNullOrWhiteSpace(context.OutputPath) && string.IsNullOrWhiteSpace(context.StagingPath))
            throw new InvalidOperationException("StageNuget requires OutputPath or StagingPath for the signed package asset set.");

        if (string.IsNullOrWhiteSpace(context.OutputPath) && !string.IsNullOrWhiteSpace(context.StagingPath))
            context.OutputPath = Path.Combine(context.StagingPath, "packages");
        if (string.IsNullOrWhiteSpace(context.ReleaseZipOutputPath) && !string.IsNullOrWhiteSpace(context.StagingPath))
            context.ReleaseZipOutputPath = Path.Combine(context.StagingPath, "releases");

        var feed = ProjectBuildPackageFeedResolver.Resolve(
            config, configDir,
            publishNuget: context.PublishNuget && !context.PlanOnly,
            publishGitHub: context.PublishGitHub && !context.PlanOnly);
        var nugetCredential = feed.VersionSourceCredential;
        var versionTrackService = new ProjectBuildVersionTrackService(_logger);
        var expectedVersionMap = versionTrackService.ResolveExpectedVersionMap(
            config,
            feed.VersionSources,
            nugetCredential,
            feed.VersionSourceCredentials);
        var versionAlignmentGroups = versionTrackService.ResolveVersionAlignmentGroups(config, feed.VersionSources);

        context.PublishApiKey = feed.PublishApiKey;
        context.GitHubToken = feed.GitHubToken;

        var packStrategy = ProjectBuildSupportService.ParsePackStrategy(config.PackStrategy);
        if (!ProjectBuildSupportService.IsKnownPackStrategy(config.PackStrategy))
            _logger.Warn($"Unknown PackStrategy '{config.PackStrategy!.Trim()}'; using PerProject.");

        context.Spec = new DotNetRepositoryReleaseSpec
        {
            RootPath = context.RootPath,
            ReleaseVersionFloor = requestedActions.ReleaseVersionFloor,
            ReleaseVersionFloorProject = requestedActions.ReleaseVersionFloorProject,
            ExpectedVersion = config.ExpectedVersion,
            ExpectedVersionsByProject = expectedVersionMap,
            ExpectedVersionMapAsInclude = config.ExpectedVersionMapAsInclude,
            ExpectedVersionMapUseWildcards = config.ExpectedVersionMapUseWildcards,
            AlignPackageVersions = config.AlignPackageVersions,
            VersionBindings = config.VersionBindings,
            VersionAlignmentGroups = versionAlignmentGroups,
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
            OverwriteSignedAssemblies = config.OverwriteSignedAssemblies ?? false,
            OverwriteSignedPackages = config.OverwriteSignedPackages ?? false,
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

    internal static ProjectBuildEffectiveActions ResolveActions(
        ProjectBuildConfiguration config,
        ProjectBuildRequestedActions? requested = null)
    {
        var update = requested?.UpdateVersions ?? config.UpdateVersions;
        var build = requested?.Build ?? config.Build;
        var nuget = requested?.PublishNuget ?? config.PublishNuget;
        var github = requested?.PublishGitHub ?? config.PublishGitHub;
        var useDefaultBuild = update is null && build is null && nuget is null && github is null;
        return new ProjectBuildEffectiveActions(
            update ?? false, build ?? useDefaultBuild, nuget ?? false, github ?? false);
    }

    private static bool ResolveSigningEnabled(bool? configuredValue, string? certificateThumbprint)
        => configuredValue ?? !string.IsNullOrWhiteSpace(certificateThumbprint);
}
