using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Package-build execution support for <see cref="ModulePipelineRunner"/>.
/// </summary>
public sealed partial class ModulePipelineRunner
{
    private void ExecutePackageBuildsBeforeModule(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state)
    {
        ValidatePackageBuildOrdering(plan);

        foreach (var segment in plan.ProjectBuilds ?? Array.Empty<ConfigurationProjectBuildSegment>())
        {
            if (segment?.Configuration is null || !ShouldRunPackageBuildBeforeModule(plan, segment.Configuration.BuildBeforeModule))
                continue;

            ExecuteProjectBuildSegment(plan, session, state, segment, PackageBuildExecutionMode.DependencyBuild);
        }

        foreach (var segment in plan.PackageBuilds ?? Array.Empty<ConfigurationPackageBuildSegment>())
        {
            if (segment?.Configuration is null || !ShouldRunPackageBuildBeforeModule(plan, segment.Configuration.BuildBeforeModule))
                continue;

            ExecutePackageBuildSegment(plan, session, state, segment, PackageBuildExecutionMode.DependencyBuild);
        }
    }

    private void ExecutePackageBuildsAfterModule(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state)
    {
        foreach (var segment in plan.ProjectBuilds ?? Array.Empty<ConfigurationProjectBuildSegment>())
        {
            if (segment?.Configuration is null || ShouldRunPackageBuildBeforeModule(plan, segment.Configuration.BuildBeforeModule))
                continue;

            ExecuteProjectBuildSegment(plan, session, state, segment, PackageBuildExecutionMode.BuildOnly);
        }

        foreach (var segment in plan.PackageBuilds ?? Array.Empty<ConfigurationPackageBuildSegment>())
        {
            if (segment?.Configuration is null || ShouldRunPackageBuildBeforeModule(plan, segment.Configuration.BuildBeforeModule))
                continue;

            ExecutePackageBuildSegment(plan, session, state, segment, PackageBuildExecutionMode.BuildOnly);
        }
    }

    private void ExecutePackageBuildPublishes(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state,
        PackageBuildPublishDestination destination)
    {
        var mode = destination == PackageBuildPublishDestination.NuGet
            ? PackageBuildExecutionMode.PublishNuGet
            : PackageBuildExecutionMode.PublishGitHub;

        foreach (var segment in plan.ProjectBuilds ?? Array.Empty<ConfigurationProjectBuildSegment>())
        {
            if (segment?.Configuration is null || !ShouldExecuteProjectBuildPublish(plan, segment, destination))
                continue;

            if (TryExecuteExistingProjectBuildPublish(plan, session, state, segment, destination))
                continue;

            ExecuteProjectBuildSegment(plan, session, state, segment, mode);
        }

        foreach (var segment in plan.PackageBuilds ?? Array.Empty<ConfigurationPackageBuildSegment>())
        {
            if (segment?.Configuration is null || !ShouldExecutePackageBuildPublish(plan, segment, destination))
                continue;

            if (TryExecuteExistingPackageBuildPublish(plan, session, state, segment, destination))
                continue;

            ExecutePackageBuildSegment(plan, session, state, segment, mode);
        }
    }

    private bool TryExecuteExistingProjectBuildPublish(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state,
        ConfigurationProjectBuildSegment segment,
        PackageBuildPublishDestination destination)
    {
        if (!state.PackageBuildResultsBySegment.TryGetValue(segment, out var existing))
            return false;
        if (existing.Result.Release is null)
            return false;

        var cfg = segment.Configuration ?? throw new InvalidOperationException("ProjectBuild configuration is missing.");
        var configPath = ResolvePackageBuildPath(plan.ProjectRoot, cfg.ConfigPath);
        var configuration = LoadProjectBuildConfiguration(configPath, cfg);
        if (!CanPublishExistingPackageBuildResult(configuration, configPath, destination))
            return false;
        if (!HasReusablePackageBuildArtifacts(existing.Result.Release, destination))
            return false;

        var step = session.GetProjectBuildStep(segment);
        session.Start(step);
        try
        {
            PublishExistingPackageBuildResult(existing, configuration, configPath, destination);
            session.Done(step);
            return true;
        }
        catch (Exception ex)
        {
            session.Fail(step, ex);
            throw;
        }
    }

    private bool TryExecuteExistingPackageBuildPublish(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state,
        ConfigurationPackageBuildSegment segment,
        PackageBuildPublishDestination destination)
    {
        if (!state.PackageBuildResultsBySegment.TryGetValue(segment, out var existing))
            return false;
        if (existing.Result.Release is null)
            return false;

        var configuration = MapPackageBuildConfiguration(segment.Configuration, plan.ProjectRoot);
        var configPath = Path.Combine(plan.ProjectRoot, "module.packagebuild.inline.json");
        if (!CanPublishExistingPackageBuildResult(configuration, configPath, destination))
            return false;
        if (!HasReusablePackageBuildArtifacts(existing.Result.Release, destination))
            return false;

        var step = session.GetPackageBuildStep(segment);
        session.Start(step);
        try
        {
            PublishExistingPackageBuildResult(existing, configuration, configPath, destination);
            session.Done(step);
            return true;
        }
        catch (Exception ex)
        {
            session.Fail(step, ex);
            throw;
        }
    }

    private static bool CanPublishExistingPackageBuildResult(
        ProjectBuildConfiguration configuration,
        string configPath,
        PackageBuildPublishDestination destination)
    {
        var configDirectory = Path.GetDirectoryName(configPath);
        if (string.IsNullOrWhiteSpace(configDirectory))
            return false;

        var feed = ProjectBuildPackageFeedResolver.Resolve(configuration, configDirectory);
        return destination switch
        {
            PackageBuildPublishDestination.NuGet => !string.IsNullOrWhiteSpace(feed.PublishApiKey),
            PackageBuildPublishDestination.GitHub =>
                !string.IsNullOrWhiteSpace(feed.GitHubToken) &&
                !string.IsNullOrWhiteSpace(configuration.GitHubUsername) &&
                !string.IsNullOrWhiteSpace(configuration.GitHubRepositoryName),
            _ => false
        };
    }

    private static bool HasReusablePackageBuildArtifacts(
        DotNetRepositoryReleaseResult release,
        PackageBuildPublishDestination destination)
    {
        return destination switch
        {
            PackageBuildPublishDestination.NuGet => HasAllArtifacts(release.Projects
                .SelectMany(project => project.Packages)
                .Where(package => !string.IsNullOrWhiteSpace(package))),
            PackageBuildPublishDestination.GitHub => HasAllArtifacts(release.Projects
                .Select(project => project.ReleaseZipPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))),
            _ => false
        };
    }

    private static bool HasAllArtifacts(IEnumerable<string?> artifactPaths)
    {
        var paths = artifactPaths
            .Select(path => path?.Trim())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return paths.Length > 0 && paths.All(path => File.Exists(path!));
    }

    private void PublishExistingPackageBuildResult(
        ProjectBuildHostExecutionResult existing,
        ProjectBuildConfiguration configuration,
        string configPath,
        PackageBuildPublishDestination destination)
    {
        var release = existing.Result.Release
            ?? throw new InvalidOperationException($"Cannot reuse package build result for {destination}; the earlier package build did not include a release result.");

        if (!release.Success)
            throw new InvalidOperationException(release.ErrorMessage ?? $"Cannot reuse failed package build result for {destination}.");

        switch (destination)
        {
            case PackageBuildPublishDestination.NuGet:
                PublishExistingNuGetPackages(release, configuration, configPath);
                break;
            case PackageBuildPublishDestination.GitHub:
                PublishExistingGitHubRelease(existing, release, configuration, configPath);
                break;
        }
    }

    private void PublishExistingNuGetPackages(
        DotNetRepositoryReleaseResult release,
        ProjectBuildConfiguration configuration,
        string configPath)
    {
        var configDirectory = Path.GetDirectoryName(configPath);
        if (string.IsNullOrWhiteSpace(configDirectory))
            throw new InvalidOperationException($"Unable to resolve the configuration directory for '{configPath}'.");

        var feed = ProjectBuildPackageFeedResolver.Resolve(configuration, configDirectory);
        var apiKey = feed.PublishApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("PublishApiKey is required when package NuGet publishing is enabled.");

        var source = string.IsNullOrWhiteSpace(feed.PublishSource)
            ? ProjectBuildPackageFeedResolver.GetDefaultPublishSource()
            : feed.PublishSource!.Trim();
        var packages = release.Projects
            .SelectMany(project => project.Packages)
            .Where(package => !string.IsNullOrWhiteSpace(package))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.Info($"Publishing {packages.Length} existing package(s) from earlier package build.");
        var publish = new NuGetPackagePublishService(_logger).ExecutePackages(
            packages,
            apiKey!,
            source,
            configuration.SkipDuplicate ?? true,
            configuration.PublishFailFast ?? true);

        release.PublishedPackages.AddRange(publish.PublishedItems);
        release.FailedPackages.AddRange(publish.FailedItems);
        if (!publish.Success)
        {
            release.Success = false;
            release.ErrorMessage = publish.ErrorMessage ?? "One or more packages failed to publish.";
            throw new InvalidOperationException(release.ErrorMessage);
        }
    }

    private void PublishExistingGitHubRelease(
        ProjectBuildHostExecutionResult existing,
        DotNetRepositoryReleaseResult release,
        ProjectBuildConfiguration configuration,
        string configPath)
    {
        var configDirectory = Path.GetDirectoryName(configPath);
        if (string.IsNullOrWhiteSpace(configDirectory))
            throw new InvalidOperationException($"Unable to resolve the configuration directory for '{configPath}'.");

        var feed = ProjectBuildPackageFeedResolver.Resolve(configuration, configDirectory);
        var token = feed.GitHubToken;
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("GitHub access token is required for package GitHub publishing.");
        if (string.IsNullOrWhiteSpace(configuration.GitHubUsername) || string.IsNullOrWhiteSpace(configuration.GitHubRepositoryName))
            throw new InvalidOperationException("GitHubUsername and GitHubRepositoryName are required for package GitHub publishing.");

        var preflightError = new ProjectBuildGitHubPreflightService(_logger).Validate(configuration, release, token!);
        if (!string.IsNullOrWhiteSpace(preflightError))
            throw new InvalidOperationException(preflightError);

        _logger.Info("Publishing GitHub release from existing package build result.");
        var summary = new ProjectBuildPublishHostService(_logger).PublishGitHub(
            new ProjectBuildPublishHostConfiguration
            {
                GitHubUsername = configuration.GitHubUsername!.Trim(),
                GitHubRepositoryName = configuration.GitHubRepositoryName!.Trim(),
                GitHubToken = token,
                GitHubReleaseMode = string.IsNullOrWhiteSpace(configuration.GitHubReleaseMode) ? "Single" : configuration.GitHubReleaseMode!.Trim(),
                GitHubIncludeProjectNameInTag = configuration.GitHubIncludeProjectNameInTag,
                GitHubIsPreRelease = configuration.GitHubIsPreRelease,
                GitHubGenerateReleaseNotes = configuration.GitHubGenerateReleaseNotes,
                GitHubReleaseName = NormalizeOptional(configuration.GitHubReleaseName),
                GitHubTagName = NormalizeOptional(configuration.GitHubTagName),
                GitHubTagTemplate = NormalizeOptional(configuration.GitHubTagTemplate),
                GitHubPrimaryProject = NormalizeOptional(configuration.GitHubPrimaryProject),
                GitHubTagConflictPolicy = NormalizeOptional(configuration.GitHubTagConflictPolicy),
                PublishFailFast = configuration.PublishFailFast ?? true
            },
            release);

        existing.Result.GitHub.AddRange(summary.Results);
        existing.Result.Success = summary.Success;
        existing.Result.ErrorMessage = summary.ErrorMessage;
        if (!summary.Success)
            throw new InvalidOperationException(summary.ErrorMessage ?? "Package GitHub publishing failed.");
    }

    private void ExecuteProjectBuildSegment(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state,
        ConfigurationProjectBuildSegment segment,
        PackageBuildExecutionMode mode)
    {
        var step = session.GetProjectBuildStep(segment);
        session.Start(step);
        try
        {
            var result = ExecuteProjectBuildSegment(plan, segment, mode);
            CompletePackageBuildExecution(
                plan,
                state,
                result,
                ReleaseVersionSource.ProjectBuild,
                segment.Configuration.Name ?? result.ConfigPath,
                segment.Configuration.UseAsReleaseVersionSource,
                segment.Configuration.ProvideLocalNuGetFeed,
                segment,
                mode,
                "Project build");

            session.Done(step);
        }
        catch (Exception ex)
        {
            session.Fail(step, ex);
            throw;
        }
    }

    private void ExecutePackageBuildSegment(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state,
        ConfigurationPackageBuildSegment segment,
        PackageBuildExecutionMode mode)
    {
        var step = session.GetPackageBuildStep(segment);
        session.Start(step);
        try
        {
            var result = ExecutePackageBuildSegment(plan, segment, mode);
            CompletePackageBuildExecution(
                plan,
                state,
                result,
                ReleaseVersionSource.PackageBuild,
                segment.Configuration.Name ?? result.ConfigPath,
                segment.Configuration.UseAsReleaseVersionSource,
                segment.Configuration.ProvideLocalNuGetFeed,
                segment,
                mode,
                "Package build");

            session.Done(step);
        }
        catch (Exception ex)
        {
            session.Fail(step, ex);
            throw;
        }
    }

    private void CompletePackageBuildExecution(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        ProjectBuildHostExecutionResult result,
        ReleaseVersionSource source,
        string laneLabel,
        bool useAsReleaseVersionSource,
        bool provideLocalNuGetFeed,
        object segment,
        PackageBuildExecutionMode mode,
        string failurePrefix)
    {
        if (!result.Success)
            throw new InvalidOperationException(result.ErrorMessage ?? $"{failurePrefix} failed for '{result.ConfigPath}'.");

        if (mode is PackageBuildExecutionMode.PublishNuGet or PackageBuildExecutionMode.PublishGitHub)
        {
            state.ReleaseCoordinationResult = null;
            return;
        }

        state.ProjectBuildResults.Add(result);
        state.PackageBuildResultsBySegment[segment] = result;

        if (provideLocalNuGetFeed)
            RegisterLocalNuGetFeeds(plan, result, laneLabel);

        RegisterReleaseVersionCandidate(
            state,
            source,
            laneLabel,
            useAsReleaseVersionSource,
            result);
    }

    private ProjectBuildHostExecutionResult ExecuteProjectBuildSegment(
        ModulePipelinePlan plan,
        ConfigurationProjectBuildSegment segment,
        PackageBuildExecutionMode mode)
    {
        var cfg = segment.Configuration ?? throw new InvalidOperationException("ProjectBuild configuration is missing.");
        if (string.IsNullOrWhiteSpace(cfg.ConfigPath))
            throw new InvalidOperationException("ProjectBuild ConfigPath is required.");

        var configPath = ResolvePackageBuildPath(plan.ProjectRoot, cfg.ConfigPath);
        var configuration = LoadProjectBuildConfiguration(configPath, cfg);
        ApplyProjectBuildGateDefaults(configuration, mode, plan.GateMode);
        var actions = ResolveEffectiveActions(configuration);
        var request = new ProjectBuildHostRequest
        {
            ConfigPath = configPath,
            ExecuteBuild = true,
            PlanOnly = configuration.PlanOnly,
            UpdateVersions = ResolveUpdateVersions(actions, mode, plan.GateMode),
            Build = ResolveBuild(actions, mode, plan.GateMode),
            PublishNuget = ResolvePublishNuGet(actions, mode),
            PublishGitHub = ResolvePublishGitHub(actions, mode)
        };

        _logger.Info($"Running package project build ({DescribePackageBuildMode(mode)}): {configPath}");
        return _packageBuildExecutor(request, configuration, configPath);
    }

    private ProjectBuildHostExecutionResult ExecutePackageBuildSegment(
        ModulePipelinePlan plan,
        ConfigurationPackageBuildSegment segment,
        PackageBuildExecutionMode mode)
    {
        var cfg = segment.Configuration ?? throw new InvalidOperationException("PackageBuild configuration is missing.");
        var projectBuildConfig = MapPackageBuildConfiguration(cfg, plan.ProjectRoot);
        ApplyProjectBuildGateDefaults(projectBuildConfig, mode, plan.GateMode);
        var actions = ResolveEffectiveActions(projectBuildConfig);
        var configPath = Path.Combine(plan.ProjectRoot, "module.packagebuild.inline.json");
        var request = new ProjectBuildHostRequest
        {
            ConfigPath = configPath,
            ExecuteBuild = true,
            PlanOnly = cfg.PlanOnly,
            UpdateVersions = ResolveUpdateVersions(actions, mode, plan.GateMode),
            Build = ResolveBuild(actions, mode, plan.GateMode),
            PublishNuget = ResolvePublishNuGet(actions, mode),
            PublishGitHub = ResolvePublishGitHub(actions, mode)
        };

        _logger.Info($"Running inline package build ({DescribePackageBuildMode(mode)}).");
        return _packageBuildExecutor(request, projectBuildConfig, configPath);
    }

    private ProjectBuildConfiguration LoadProjectBuildConfiguration(string configPath)
        => new ProjectBuildSupportService(_logger).LoadConfig(configPath);

    private ProjectBuildConfiguration LoadProjectBuildConfiguration(
        string configPath,
        ProjectBuildConfigurationReference reference)
    {
        var configuration = LoadProjectBuildConfiguration(configPath);
        ApplyProjectBuildReferenceOverrides(configuration, reference);
        return configuration;
    }

    private static void ApplyProjectBuildReferenceOverrides(
        ProjectBuildConfiguration target,
        ProjectBuildConfigurationReference reference)
    {
        ApplyPackageBuildOptions(target, reference.Options);

        if (UsesDefaultProjectBuildActions(target) && HasProjectBuildActionOverride(reference))
            ApplyDefaultProjectBuildActions(target);

        if (reference.UpdateVersions is not null)
            target.UpdateVersions = reference.UpdateVersions;
        if (reference.Build is not null)
            target.Build = reference.Build;
        if (reference.PublishNuget is not null)
            target.PublishNuget = reference.PublishNuget;
        if (reference.PublishGitHub is not null)
            target.PublishGitHub = reference.PublishGitHub;
        if (reference.CreateReleaseZip is not null)
            target.CreateReleaseZip = reference.CreateReleaseZip;
        if (reference.SignAssemblies is not null)
            target.SignAssemblies = reference.SignAssemblies;
        if (reference.SignDependencyAssemblies is not null)
            target.SignDependencyAssemblies = reference.SignDependencyAssemblies;
        if (reference.SignPackages is not null)
            target.SignPackages = reference.SignPackages;
    }

    private static void ApplyProjectBuildGateDefaults(
        ProjectBuildConfiguration target,
        PackageBuildExecutionMode mode,
        ConfigurationGateMode? gateMode)
    {
        if (gateMode == ConfigurationGateMode.Build &&
            mode is PackageBuildExecutionMode.DependencyBuild or PackageBuildExecutionMode.BuildOnly)
        {
            target.CertificateThumbprint = null;
        }
    }

    private static bool HasProjectBuildActionOverride(ProjectBuildConfigurationReference reference)
        => reference.UpdateVersions is not null ||
           reference.Build is not null ||
           reference.PublishNuget is not null ||
           reference.PublishGitHub is not null;

    private static bool UsesDefaultProjectBuildActions(ProjectBuildConfiguration target)
        => target.UpdateVersions is null &&
           target.Build is null &&
           target.PublishNuget is null &&
           target.PublishGitHub is null;

    private static void ApplyDefaultProjectBuildActions(ProjectBuildConfiguration target)
    {
        target.UpdateVersions = true;
        target.Build = true;
        target.PublishNuget = false;
        target.PublishGitHub = false;
    }

    private bool ShouldExecuteProjectBuildPublish(
        ModulePipelinePlan plan,
        ConfigurationProjectBuildSegment segment,
        PackageBuildPublishDestination destination)
    {
        var cfg = segment.Configuration ?? throw new InvalidOperationException("ProjectBuild configuration is missing.");
        if (string.IsNullOrWhiteSpace(cfg.ConfigPath))
            return false;

        var actions = ResolveEffectiveActions(LoadProjectBuildConfiguration(ResolvePackageBuildPath(plan.ProjectRoot, cfg.ConfigPath), cfg));
        return destination == PackageBuildPublishDestination.NuGet
            ? actions.PublishNuGet
            : actions.PublishGitHub;
    }

    private static bool ShouldExecutePackageBuildPublish(
        ModulePipelinePlan plan,
        ConfigurationPackageBuildSegment segment,
        PackageBuildPublishDestination destination)
    {
        var cfg = segment.Configuration ?? throw new InvalidOperationException("PackageBuild configuration is missing.");
        var actions = ResolveEffectiveActions(MapPackageBuildConfiguration(cfg, plan.ProjectRoot));
        return destination == PackageBuildPublishDestination.NuGet
            ? actions.PublishNuGet
            : actions.PublishGitHub;
    }

    private static bool? ResolveUpdateVersions(
        ProjectBuildEffectiveActions actions,
        PackageBuildExecutionMode mode,
        ConfigurationGateMode? gateMode)
        => mode switch
        {
            PackageBuildExecutionMode.DependencyBuild => actions.UpdateVersions || actions.PublishNuGet || actions.PublishGitHub,
            PackageBuildExecutionMode.BuildOnly when gateMode == ConfigurationGateMode.Build => actions.UpdateVersions || actions.PublishNuGet || actions.PublishGitHub,
            PackageBuildExecutionMode.BuildOnly => actions.UpdateVersions,
            _ => false
        };

    private static bool? ResolveBuild(
        ProjectBuildEffectiveActions actions,
        PackageBuildExecutionMode mode,
        ConfigurationGateMode? gateMode)
        => mode switch
        {
            PackageBuildExecutionMode.DependencyBuild => actions.Build || actions.PublishNuGet || actions.PublishGitHub,
            PackageBuildExecutionMode.BuildOnly when gateMode == ConfigurationGateMode.Build => actions.Build || actions.PublishNuGet || actions.PublishGitHub,
            PackageBuildExecutionMode.BuildOnly => actions.Build,
            _ => false
        };

    private static bool? ResolvePublishNuGet(ProjectBuildEffectiveActions actions, PackageBuildExecutionMode mode)
        => mode == PackageBuildExecutionMode.PublishNuGet && actions.PublishNuGet;

    private static bool? ResolvePublishGitHub(ProjectBuildEffectiveActions actions, PackageBuildExecutionMode mode)
        => mode == PackageBuildExecutionMode.PublishGitHub && actions.PublishGitHub;

    private static ProjectBuildEffectiveActions ResolveEffectiveActions(ProjectBuildConfiguration config)
    {
        var defaultAll = config.UpdateVersions is null &&
                         config.Build is null &&
                         config.PublishNuget is null &&
                         config.PublishGitHub is null;

        return new ProjectBuildEffectiveActions(
            config.UpdateVersions ?? defaultAll,
            config.Build ?? defaultAll,
            config.PublishNuget ?? false,
            config.PublishGitHub ?? false);
    }

    private static void ValidatePackageBuildOrdering(ModulePipelinePlan plan)
    {
        foreach (var segment in plan.ProjectBuilds ?? Array.Empty<ConfigurationProjectBuildSegment>())
        {
            var cfg = segment?.Configuration;
            if (cfg is null)
                continue;

            ValidatePackageBuildLaneOrdering(
                plan,
                cfg.Name ?? cfg.ConfigPath ?? "ProjectBuild",
                "ProjectBuild",
                cfg.BuildBeforeModule,
                cfg.ProvideLocalNuGetFeed);
        }

        foreach (var segment in plan.PackageBuilds ?? Array.Empty<ConfigurationPackageBuildSegment>())
        {
            var cfg = segment?.Configuration;
            if (cfg is null)
                continue;

            ValidatePackageBuildLaneOrdering(
                plan,
                cfg.Name ?? cfg.RootPath ?? "PackageBuild",
                "PackageBuild",
                cfg.BuildBeforeModule,
                cfg.ProvideLocalNuGetFeed);
        }
    }

    private static void ValidatePackageBuildLaneOrdering(
        ModulePipelinePlan plan,
        string laneLabel,
        string laneType,
        bool buildBeforeModule,
        bool provideLocalNuGetFeed)
    {
        var runsBeforeModule = ShouldRunPackageBuildBeforeModule(plan, buildBeforeModule);
        if (provideLocalNuGetFeed && !runsBeforeModule)
        {
            throw new InvalidOperationException(
                $"{laneType} lane '{laneLabel}' uses ProvideLocalNuGetFeed and must run before the module build. Set BuildBeforeModule to true or configure Release BuildOrder so PackageBuild runs before Module.");
        }
    }

    private static bool ShouldRunPackageBuildBeforeModule(ModulePipelinePlan plan, bool buildBeforeModule)
        => ResolveReleaseBuildOrderOverride(plan) ?? buildBeforeModule;

    private static bool? ResolveReleaseBuildOrderOverride(ModulePipelinePlan plan)
    {
        var order = plan.Release?.Configuration?.BuildOrder;
        if (order is null || order.Length == 0)
            return null;

        int? packageIndex = null;
        int? moduleIndex = null;
        for (var index = 0; index < order.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(order[index]))
                continue;

            if (TryParseReleaseBuildLane(order[index], out var lane))
            {
                if (lane == ReleaseBuildLane.PackageBuild && packageIndex is null)
                    packageIndex = index;
                if (lane == ReleaseBuildLane.Module && moduleIndex is null)
                    moduleIndex = index;
            }
        }

        if (packageIndex is null || moduleIndex is null)
            return null;

        return packageIndex.Value < moduleIndex.Value;
    }

    private static bool TryParseReleaseBuildLane(string value, out ReleaseBuildLane lane)
    {
        var normalized = value.Trim().Replace("-", string.Empty).Replace("_", string.Empty);
        if (string.Equals(normalized, "PackageBuild", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "PackageBuilds", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "ProjectBuild", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "ProjectBuilds", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Packages", StringComparison.OrdinalIgnoreCase))
        {
            lane = ReleaseBuildLane.PackageBuild;
            return true;
        }

        if (string.Equals(normalized, "Module", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "ModuleBuild", StringComparison.OrdinalIgnoreCase))
        {
            lane = ReleaseBuildLane.Module;
            return true;
        }

        lane = default;
        return false;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private static string DescribePackageBuildMode(PackageBuildExecutionMode mode)
        => mode switch
        {
            PackageBuildExecutionMode.DependencyBuild => "dependency build",
            PackageBuildExecutionMode.BuildOnly => "post-module build",
            PackageBuildExecutionMode.PublishNuGet => "NuGet publish",
            PackageBuildExecutionMode.PublishGitHub => "GitHub publish",
            _ => mode.ToString()
        };

    private static ProjectBuildConfiguration MapPackageBuildConfiguration(PackageBuildConfiguration source, string? projectRoot = null)
    {
        var target = new ProjectBuildConfiguration
        {
            RootPath = source.RootPath,
            ExpectedVersion = source.ExpectedVersion,
            ExpectedVersionMap = source.ExpectedVersionMap,
            VersionTracks = MapVersionTracks(source.VersionTracks),
            ExpectedVersionMapAsInclude = source.ExpectedVersionMapAsInclude,
            ExpectedVersionMapUseWildcards = source.ExpectedVersionMapUseWildcards,
            IncludeProjects = source.IncludeProjects,
            ExcludeProjects = source.ExcludeProjects,
            ExcludeDirectories = source.ExcludeDirectories,
            NugetSource = source.NugetSource,
            IncludePrerelease = source.IncludePrerelease,
            Configuration = source.Configuration,
            OutputPath = source.OutputPath,
            ReleaseZipOutputPath = source.ReleaseZipOutputPath,
            StagingPath = source.StagingPath,
            CleanStaging = source.CleanStaging,
            PlanOnly = source.PlanOnly,
            PlanOutputPath = source.PlanOutputPath,
            UpdateVersions = source.UpdateVersions,
            Build = source.Build,
            PackStrategy = source.PackStrategy,
            PublishNuget = source.PublishNuget,
            PublishGitHub = source.PublishGitHub,
            CreateReleaseZip = source.CreateReleaseZip,
            UseGitHubPackages = source.UseGitHubPackages,
            GitHubPackagesOwner = source.GitHubPackagesOwner,
            PublishSource = source.PublishSource,
            PublishApiKey = source.PublishApiKey,
            PublishApiKeyFilePath = source.PublishApiKeyFilePath,
            PublishApiKeyEnvName = source.PublishApiKeyEnvName,
            SkipDuplicate = source.SkipDuplicate,
            PublishFailFast = source.PublishFailFast,
            CertificateThumbprint = source.CertificateThumbprint,
            CertificateStore = source.CertificateStore,
            TimeStampServer = source.TimeStampServer,
            SignAssemblies = source.SignAssemblies,
            SignDependencyAssemblies = source.SignDependencyAssemblies,
            SignPackages = source.SignPackages,
            NugetCredentialUserName = source.NugetCredentialUserName,
            NugetCredentialSecret = source.NugetCredentialSecret,
            NugetCredentialSecretFilePath = source.NugetCredentialSecretFilePath,
            NugetCredentialSecretEnvName = source.NugetCredentialSecretEnvName,
            GitHubAccessToken = source.GitHubAccessToken,
            GitHubAccessTokenFilePath = source.GitHubAccessTokenFilePath,
            GitHubAccessTokenEnvName = source.GitHubAccessTokenEnvName,
            GitHubUsername = source.GitHubUsername,
            GitHubRepositoryName = source.GitHubRepositoryName,
            GitHubIsPreRelease = source.GitHubIsPreRelease,
            GitHubIncludeProjectNameInTag = source.GitHubIncludeProjectNameInTag,
            GitHubGenerateReleaseNotes = source.GitHubGenerateReleaseNotes,
            GitHubReleaseName = source.GitHubReleaseName,
            GitHubTagName = source.GitHubTagName,
            GitHubTagTemplate = source.GitHubTagTemplate,
            GitHubReleaseMode = source.GitHubReleaseMode,
            GitHubPrimaryProject = source.GitHubPrimaryProject,
            GitHubTagConflictPolicy = source.GitHubTagConflictPolicy
        };

        ApplyPackageBuildOptions(target, source.Options);
        ResolveInlinePackageBuildPaths(target, projectRoot);
        return target;
    }

    private static void ResolveInlinePackageBuildPaths(ProjectBuildConfiguration target, string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
            return;

        target.RootPath = ResolveInlinePackageBuildPath(projectRoot!, target.RootPath);
        target.OutputPath = ResolveInlinePackageBuildPath(projectRoot!, target.OutputPath);
        target.ReleaseZipOutputPath = ResolveInlinePackageBuildPath(projectRoot!, target.ReleaseZipOutputPath);
        target.StagingPath = ResolveInlinePackageBuildPath(projectRoot!, target.StagingPath);
        target.PlanOutputPath = ResolveInlinePackageBuildPath(projectRoot!, target.PlanOutputPath);
    }

    private static string? ResolveInlinePackageBuildPath(string projectRoot, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return PathValueResolver.Resolve(projectRoot, path!);
    }

    private static Dictionary<string, ProjectBuildVersionTrack>? MapVersionTracks(
        Dictionary<string, PackageBuildVersionTrackConfiguration>? source)
    {
        if (source is null || source.Count == 0)
            return null;

        var target = new Dictionary<string, ProjectBuildVersionTrack>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in source)
        {
            target[entry.Key] = new ProjectBuildVersionTrack
            {
                ExpectedVersion = entry.Value.ExpectedVersion,
                AnchorProject = entry.Value.AnchorProject,
                AnchorPackageId = entry.Value.AnchorPackageId,
                Projects = entry.Value.Projects,
                NugetSource = entry.Value.NugetSource,
                IncludePrerelease = entry.Value.IncludePrerelease
            };
        }

        return target;
    }

    private static void ApplyPackageBuildOptions(ProjectBuildConfiguration target, Dictionary<string, object?>? options)
    {
        if (options is null || options.Count == 0)
            return;

        var properties = typeof(ProjectBuildConfiguration)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.CanWrite)
            .ToDictionary(static property => property.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var option in options)
        {
            if (string.IsNullOrWhiteSpace(option.Key))
                continue;
            if (!properties.TryGetValue(option.Key.Trim(), out var property))
                continue;

            var converted = ConvertPackageBuildOption(option.Value, property.PropertyType);
            if (converted is not null || Nullable.GetUnderlyingType(property.PropertyType) is not null || !property.PropertyType.IsValueType)
                property.SetValue(target, converted);
        }
    }

    private static object? ConvertPackageBuildOption(object? value, Type targetType)
    {
        if (value is null)
            return null;

        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (underlyingType.IsInstanceOfType(value))
            return value;

        if (value is JsonElement json)
            return ConvertJsonPackageBuildOption(json, underlyingType);

        if (underlyingType == typeof(Dictionary<string, string>))
            return ConvertPackageBuildStringDictionaryOption(value);
        if (underlyingType == typeof(Dictionary<string, ProjectBuildVersionTrack>))
            return ConvertPackageBuildVersionTracksOption(value);
        if (underlyingType == typeof(string))
            return value.ToString();
        if (underlyingType == typeof(bool))
            return value is bool boolean ? boolean : bool.TryParse(value.ToString(), out var parsed) && parsed;
        if (underlyingType == typeof(string[]))
            return ConvertPackageBuildStringArrayOption(value);

        return Convert.ChangeType(value, underlyingType);
    }

    private static Dictionary<string, string>? ConvertPackageBuildStringDictionaryOption(object value)
    {
        if (value is not System.Collections.IDictionary dictionary)
            return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in dictionary)
        {
            var key = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            result[key!.Trim()] = entry.Value?.ToString() ?? string.Empty;
        }

        return result.Count == 0 ? null : result;
    }

    private static Dictionary<string, ProjectBuildVersionTrack>? ConvertPackageBuildVersionTracksOption(object value)
    {
        if (value is not System.Collections.IDictionary dictionary)
            return null;

        var result = new Dictionary<string, ProjectBuildVersionTrack>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in dictionary)
        {
            var key = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            result[key!.Trim()] = ConvertPackageBuildVersionTrackOption(entry.Value);
        }

        return result.Count == 0 ? null : result;
    }

    private static ProjectBuildVersionTrack ConvertPackageBuildVersionTrackOption(object? value)
    {
        if (value is ProjectBuildVersionTrack track)
            return track;

        if (value is not System.Collections.IDictionary dictionary)
            return new ProjectBuildVersionTrack { ExpectedVersion = value?.ToString() };

        return new ProjectBuildVersionTrack
        {
            ExpectedVersion = GetPackageBuildDictionaryString(dictionary, nameof(ProjectBuildVersionTrack.ExpectedVersion)),
            AnchorProject = GetPackageBuildDictionaryString(dictionary, nameof(ProjectBuildVersionTrack.AnchorProject)),
            AnchorPackageId = GetPackageBuildDictionaryString(dictionary, nameof(ProjectBuildVersionTrack.AnchorPackageId)),
            Projects = GetPackageBuildDictionaryStringArray(dictionary, nameof(ProjectBuildVersionTrack.Projects)),
            NugetSource = GetPackageBuildDictionaryStringArray(dictionary, nameof(ProjectBuildVersionTrack.NugetSource)),
            IncludePrerelease = GetPackageBuildDictionaryBool(dictionary, nameof(ProjectBuildVersionTrack.IncludePrerelease))
        };
    }

    private static object? GetPackageBuildDictionaryValue(System.Collections.IDictionary dictionary, string key)
    {
        foreach (System.Collections.DictionaryEntry entry in dictionary)
        {
            if (string.Equals(entry.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }

        return null;
    }

    private static string? GetPackageBuildDictionaryString(System.Collections.IDictionary dictionary, string key)
        => GetPackageBuildDictionaryValue(dictionary, key)?.ToString();

    private static string[]? GetPackageBuildDictionaryStringArray(System.Collections.IDictionary dictionary, string key)
    {
        var value = GetPackageBuildDictionaryValue(dictionary, key);
        if (value is null)
            return null;

        var values = ConvertPackageBuildStringArrayOption(value);
        return values.Length == 0 ? null : values;
    }

    private static bool? GetPackageBuildDictionaryBool(System.Collections.IDictionary dictionary, string key)
    {
        var value = GetPackageBuildDictionaryValue(dictionary, key);
        if (value is null)
            return null;

        if (value is bool boolean)
            return boolean;

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }

    private static object? ConvertJsonPackageBuildOption(JsonElement value, Type targetType)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (targetType == typeof(string))
            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        if (targetType == typeof(bool))
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => bool.TryParse(value.GetString(), out var parsed) && parsed,
                _ => false
            };
        if (targetType == typeof(string[]))
        {
            if (value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Select(static item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Select(static item => item!.Trim())
                    .ToArray();
            }

            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            return string.IsNullOrWhiteSpace(text) ? Array.Empty<string>() : new[] { text!.Trim() };
        }

        return JsonSerializer.Deserialize(value.GetRawText(), targetType);
    }

    private static string[] ConvertPackageBuildStringArrayOption(object value)
    {
        if (value is string text)
            return string.IsNullOrWhiteSpace(text) ? Array.Empty<string>() : new[] { text.Trim() };

        if (value is System.Collections.IEnumerable enumerable)
        {
            var values = new List<string>();
            foreach (var item in enumerable)
            {
                var itemText = item?.ToString();
                if (!string.IsNullOrWhiteSpace(itemText))
                    values.Add(itemText!.Trim());
            }

            return values.ToArray();
        }

        return new[] { value.ToString() ?? string.Empty };
    }

    private void RegisterLocalNuGetFeeds(
        ModulePipelinePlan plan,
        ProjectBuildHostExecutionResult result,
        string laneLabel)
    {
        var feeds = ResolveLocalNuGetFeedPaths(result);
        if (feeds.Length == 0)
        {
            throw new InvalidOperationException(
                $"Package build lane '{laneLabel}' requested ProvideLocalNuGetFeed, but no built .nupkg files were found in its reported outputs.");
        }

        plan.BuildSpec.NuGetRestoreSources = (plan.BuildSpec.NuGetRestoreSources ?? Array.Empty<string>())
            .Concat(feeds)
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path.Trim().Trim('"')))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _logger.Info($"Added local NuGet restore source(s) from package build '{laneLabel}': {string.Join(", ", feeds)}");
    }

    private static string[] ResolveLocalNuGetFeedPaths(ProjectBuildHostExecutionResult result)
    {
        var feeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var package in result.Result?.Release?.Projects
                     .SelectMany(static project => project.Packages) ?? Array.Empty<string>())
        {
            TryAddPackageDirectory(feeds, package);
        }

        TryAddPackageSourcePath(feeds, result.OutputPath);
        return feeds.ToArray();
    }

    private static void TryAddPackageSourcePath(HashSet<string> feeds, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        var fullPath = Path.GetFullPath(path!.Trim().Trim('"'));
        if (File.Exists(fullPath))
        {
            TryAddPackageDirectory(feeds, fullPath);
            return;
        }

        if (!Directory.Exists(fullPath))
            return;

        foreach (var package in Directory.EnumerateFiles(fullPath, "*.nupkg", SearchOption.AllDirectories))
            TryAddPackageDirectory(feeds, package);
    }

    private static void TryAddPackageDirectory(HashSet<string> feeds, string? packagePath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            return;

        var fullPath = Path.GetFullPath(packagePath!.Trim().Trim('"'));
        if (!IsRestorePackagePath(fullPath) || !File.Exists(fullPath))
            return;

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            feeds.Add(directory!);
    }

    private static bool IsRestorePackagePath(string path)
        => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) &&
           !path.EndsWith(".symbols.nupkg", StringComparison.OrdinalIgnoreCase);

    private static string ResolvePackageBuildPath(string projectRoot, string path)
    {
        return PathValueResolver.Resolve(projectRoot, path);
    }

    private enum PackageBuildExecutionMode
    {
        DependencyBuild,
        BuildOnly,
        PublishNuGet,
        PublishGitHub
    }

    private enum PackageBuildPublishDestination
    {
        NuGet,
        GitHub
    }

    private enum ReleaseBuildLane
    {
        PackageBuild,
        Module
    }

    private readonly struct ProjectBuildEffectiveActions
    {
        public ProjectBuildEffectiveActions(
            bool updateVersions,
            bool build,
            bool publishNuGet,
            bool publishGitHub)
        {
            UpdateVersions = updateVersions;
            Build = build;
            PublishNuGet = publishNuGet;
            PublishGitHub = publishGitHub;
        }

        public bool UpdateVersions { get; }
        public bool Build { get; }
        public bool PublishNuGet { get; }
        public bool PublishGitHub { get; }
    }
}
