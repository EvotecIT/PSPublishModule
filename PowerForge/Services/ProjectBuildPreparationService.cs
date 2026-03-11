namespace PowerForge;

/// <summary>
/// Resolves project build action flags, credentials, paths, and repository release specifications.
/// </summary>
internal sealed class ProjectBuildPreparationService
{
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

        var nugetCredentialSecret = ProjectBuildSupportService.ResolveSecret(
            config.NugetCredentialSecret,
            config.NugetCredentialSecretFilePath,
            config.NugetCredentialSecretEnvName,
            configDir);
        var nugetUser = string.IsNullOrWhiteSpace(config.NugetCredentialUserName) ? null : config.NugetCredentialUserName!.Trim();
        var nugetCredential = (!string.IsNullOrWhiteSpace(nugetUser) || !string.IsNullOrWhiteSpace(nugetCredentialSecret))
            ? new RepositoryCredential
            {
                UserName = nugetUser,
                Secret = nugetCredentialSecret
            }
            : null;

        context.PublishApiKey = ProjectBuildSupportService.ResolveSecret(
            config.PublishApiKey,
            config.PublishApiKeyFilePath,
            config.PublishApiKeyEnvName,
            configDir);
        context.GitHubToken = ProjectBuildSupportService.ResolveSecret(
            config.GitHubAccessToken,
            config.GitHubAccessTokenFilePath,
            config.GitHubAccessTokenEnvName,
            configDir);

        context.Spec = new DotNetRepositoryReleaseSpec
        {
            RootPath = context.RootPath,
            ExpectedVersion = config.ExpectedVersion,
            ExpectedVersionsByProject = config.ExpectedVersionMap,
            ExpectedVersionMapAsInclude = config.ExpectedVersionMapAsInclude,
            ExpectedVersionMapUseWildcards = config.ExpectedVersionMapUseWildcards,
            IncludeProjects = config.IncludeProjects,
            ExcludeProjects = config.ExcludeProjects,
            ExcludeDirectories = config.ExcludeDirectories,
            VersionSources = config.NugetSource,
            VersionSourceCredential = nugetCredential,
            IncludePrerelease = config.IncludePrerelease,
            Configuration = string.IsNullOrWhiteSpace(config.Configuration) ? "Release" : config.Configuration!,
            OutputPath = context.OutputPath,
            ReleaseZipOutputPath = context.ReleaseZipOutputPath,
            CertificateThumbprint = config.CertificateThumbprint,
            CertificateStore = ProjectBuildSupportService.ParseCertificateStore(config.CertificateStore),
            TimeStampServer = config.TimeStampServer,
            Pack = context.Build || context.PublishNuget || context.PublishGitHub,
            CreateReleaseZip = context.CreateReleaseZip,
            Publish = context.PublishNuget,
            PublishSource = config.PublishSource,
            PublishApiKey = context.PublishApiKey,
            SkipDuplicate = config.SkipDuplicate ?? true,
            PublishFailFast = config.PublishFailFast ?? true,
            UpdateVersions = context.UpdateVersions
        };

        return context;
    }
}
