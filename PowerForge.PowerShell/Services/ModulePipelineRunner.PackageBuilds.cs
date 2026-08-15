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
        var coordinatedReleaseCheckpointActive = state.SynchronizedReleaseCheckpoint is not null;
        var mode = destination == PackageBuildPublishDestination.NuGet
            ? PackageBuildExecutionMode.PublishNuGet
            : PackageBuildExecutionMode.PublishGitHub;

        foreach (var segment in plan.ProjectBuilds ?? Array.Empty<ConfigurationProjectBuildSegment>())
        {
            if (segment?.Configuration is null || !ShouldExecuteProjectBuildPublish(plan, segment, destination))
                continue;

            var operationKey = CreateProjectBuildPublishOperationFingerprint(plan, segment, destination);
            if (ShouldSkipSynchronizedReleaseOperation(state, operationKey))
                continue;

            var useDuplicateTolerantNuGetRetry = destination == PackageBuildPublishDestination.NuGet &&
                state.IsResumingSynchronizedRelease &&
                WasSynchronizedReleaseOperationAttempted(state, operationKey);
            Action remotePublishAttempted = () => MarkSynchronizedReleaseOperationAttempted(state, operationKey);
            if (!plan.GenerateReleaseProvenance && TryExecuteExistingProjectBuildPublish(
                    plan,
                    session,
                    state,
                    segment,
                    destination,
                    useDuplicateTolerantNuGetRetry,
                    remotePublishAttempted,
                    coordinatedReleaseCheckpointActive))
            {
                MarkSynchronizedReleaseOperationCompleted(state, operationKey);
                continue;
            }

            ExecuteProjectBuildSegment(
                plan,
                session,
                state,
                segment,
                mode,
                useDuplicateTolerantNuGetRetry,
                remotePublishAttempted,
                coordinatedReleaseCheckpointActive);
            MarkSynchronizedReleaseOperationCompleted(state, operationKey);
        }

        foreach (var segment in plan.PackageBuilds ?? Array.Empty<ConfigurationPackageBuildSegment>())
        {
            if (segment?.Configuration is null || !ShouldExecutePackageBuildPublish(plan, segment, destination))
                continue;

            var operationKey = CreatePackageBuildPublishOperationFingerprint(plan, segment, destination);
            if (ShouldSkipSynchronizedReleaseOperation(state, operationKey))
                continue;

            var useDuplicateTolerantNuGetRetry = destination == PackageBuildPublishDestination.NuGet &&
                state.IsResumingSynchronizedRelease &&
                WasSynchronizedReleaseOperationAttempted(state, operationKey);
            Action remotePublishAttempted = () => MarkSynchronizedReleaseOperationAttempted(state, operationKey);
            if (!plan.GenerateReleaseProvenance && TryExecuteExistingPackageBuildPublish(
                    plan,
                    session,
                    state,
                    segment,
                    destination,
                    useDuplicateTolerantNuGetRetry,
                    remotePublishAttempted,
                    coordinatedReleaseCheckpointActive))
            {
                MarkSynchronizedReleaseOperationCompleted(state, operationKey);
                continue;
            }

            ExecutePackageBuildSegment(
                plan,
                session,
                state,
                segment,
                mode,
                useDuplicateTolerantNuGetRetry,
                remotePublishAttempted,
                coordinatedReleaseCheckpointActive);
            MarkSynchronizedReleaseOperationCompleted(state, operationKey);
        }
    }

    private bool TryExecuteExistingProjectBuildPublish(
        ModulePipelinePlan plan,
        ModulePipelineExecutionSession session,
        ModulePipelineRunState state,
        ConfigurationProjectBuildSegment segment,
        PackageBuildPublishDestination destination,
        bool useDuplicateTolerantNuGetRetry,
        Action remotePublishAttempted,
        bool coordinatedReleaseCheckpointActive)
    {
        if (!state.PackageBuildResultsBySegment.TryGetValue(segment, out var existing))
            return false;
        if (existing.Result.Release is null)
            return false;

        var cfg = segment.Configuration ?? throw new InvalidOperationException("ProjectBuild configuration is missing.");
        var configPath = ResolvePackageBuildPath(plan.ProjectRoot, cfg.ConfigPath);
        var configuration = LoadProjectBuildConfiguration(configPath, cfg);
        ApplySynchronizedNuGetRetryPolicy(
            configuration,
            useDuplicateTolerantNuGetRetry,
            coordinatedReleaseCheckpointActive);
        if (!CanPublishExistingPackageBuildResult(configuration, configPath, destination))
            return false;
        if (!HasReusablePackageBuildArtifacts(existing.Result.Release, destination))
            return false;

        var step = session.GetProjectBuildStep(segment);
        session.Start(step);
        try
        {
            PublishExistingPackageBuildResult(
                existing,
                configuration,
                configPath,
                destination,
                remotePublishAttempted,
                coordinatedReleaseCheckpointActive,
                session.CreateProjectBuildProgressReporter(step));
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
        PackageBuildPublishDestination destination,
        bool useDuplicateTolerantNuGetRetry,
        Action remotePublishAttempted,
        bool coordinatedReleaseCheckpointActive)
    {
        if (!state.PackageBuildResultsBySegment.TryGetValue(segment, out var existing))
            return false;
        if (existing.Result.Release is null)
            return false;

        var configuration = MapPackageBuildConfiguration(segment.Configuration, plan.ProjectRoot);
        ApplySynchronizedNuGetRetryPolicy(
            configuration,
            useDuplicateTolerantNuGetRetry,
            coordinatedReleaseCheckpointActive);
        var configPath = Path.Combine(plan.ProjectRoot, "module.packagebuild.inline.json");
        if (!CanPublishExistingPackageBuildResult(configuration, configPath, destination))
            return false;
        if (!HasReusablePackageBuildArtifacts(existing.Result.Release, destination))
            return false;

        var step = session.GetPackageBuildStep(segment);
        session.Start(step);
        try
        {
            PublishExistingPackageBuildResult(
                existing,
                configuration,
                configPath,
                destination,
                remotePublishAttempted,
                coordinatedReleaseCheckpointActive,
                session.CreateProjectBuildProgressReporter(step));
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
                .SelectMany(project => project.Packages.Concat(project.SymbolPackages))
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

    private static void ApplySynchronizedNuGetRetryPolicy(
        ProjectBuildConfiguration configuration,
        bool useDuplicateTolerantNuGetRetry,
        bool coordinatedReleaseCheckpointActive)
    {
        if (!coordinatedReleaseCheckpointActive)
        {
            if (useDuplicateTolerantNuGetRetry)
                configuration.SkipDuplicate = true;
            return;
        }

        configuration.SkipDuplicate = useDuplicateTolerantNuGetRetry;
    }

    private void PublishExistingPackageBuildResult(
        ProjectBuildHostExecutionResult existing,
        ProjectBuildConfiguration configuration,
        string configPath,
        PackageBuildPublishDestination destination,
        Action remotePublishAttempted,
        bool coordinatedReleaseCheckpointActive,
        IProjectBuildProgressReporter? progress)
    {
        var release = existing.Result.Release
            ?? throw new InvalidOperationException($"Cannot reuse package build result for {destination}; the earlier package build did not include a release result.");

        if (!release.Success)
            throw new InvalidOperationException(release.ErrorMessage ?? $"Cannot reuse failed package build result for {destination}.");

        switch (destination)
        {
            case PackageBuildPublishDestination.NuGet:
                PublishExistingNuGetPackages(
                    release,
                    configuration,
                    configPath,
                    existing.RootPath,
                    remotePublishAttempted,
                    progress);
                break;
            case PackageBuildPublishDestination.GitHub:
                PublishExistingGitHubRelease(
                    existing,
                    release,
                    configuration,
                    configPath,
                    remotePublishAttempted,
                    coordinatedReleaseCheckpointActive,
                    progress);
                break;
        }
    }

    private void PublishExistingNuGetPackages(
        DotNetRepositoryReleaseResult release,
        ProjectBuildConfiguration configuration,
        string configPath,
        string repositoryRoot,
        Action remotePublishAttempted,
        IProjectBuildProgressReporter? progress)
    {
        var configDirectory = Path.GetDirectoryName(configPath);
        if (string.IsNullOrWhiteSpace(configDirectory))
            throw new InvalidOperationException($"Unable to resolve the configuration directory for '{configPath}'.");

        var feed = ProjectBuildPackageFeedResolver.Resolve(configuration, configDirectory);
        var apiKey = feed.PublishApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("PublishApiKey is required when package NuGet publishing is enabled.");

        var configuredSource = string.IsNullOrWhiteSpace(feed.PublishSource)
            ? ProjectBuildPackageFeedResolver.GetDefaultPublishSource()
            : feed.PublishSource!.Trim();
        var source = DotNetRepositoryReleaseService.ResolvePublishSource(
            configuredSource,
            string.IsNullOrWhiteSpace(repositoryRoot) ? configDirectory : repositoryRoot,
            nuGetConfigSearchRoot: configDirectory);
        release.PublishSource = source;
        var publishSymbolsSeparately = (configuration.IncludeSymbols ?? false) &&
            DotNetRepositoryReleaseService.IsLocalPublishSource(source);
        var packages = DotNetRepositoryReleaseService.GetPackagesForPublish(
            release.Projects,
            includeSymbolPackages: publishSymbolsSeparately);

        _logger.Info($"Publishing {packages.Length} existing package(s) from earlier package build.");
        var publish = new NuGetPackagePublishService(
            _logger,
            workingDirectory: configDirectory).ExecutePackages(
            packages,
            apiKey!,
            source,
            configuration.SkipDuplicate ?? true,
            configuration.PublishFailFast ?? true,
            suppressCompanionSymbols: !(configuration.IncludeSymbols ?? false) || publishSymbolsSeparately,
            remotePublishAttempted: remotePublishAttempted,
            progress: progress);

        ApplyPublishedNuGetArtifactOutcomes(
            release,
            publish,
            publishSymbolsSeparately,
            configuration.SkipDuplicate ?? true);
        if (!publish.Success)
        {
            release.Success = false;
            release.ErrorMessage = publish.ErrorMessage ?? "One or more packages failed to publish.";
            throw new InvalidOperationException(release.ErrorMessage);
        }
    }

    internal static void ApplyPublishedNuGetArtifactOutcomes(
        DotNetRepositoryReleaseResult release,
        NuGetPackagePublishResult publish,
        bool publishSymbolsSeparately = false,
        bool skipDuplicate = true)
    {
        var publishedPrimaryPackages = new HashSet<string>(publish.PublishedItems, StringComparer.OrdinalIgnoreCase);
        var skippedPrimaryPackages = new HashSet<string>(publish.SkippedDuplicateItems, StringComparer.OrdinalIgnoreCase);
        var failedPrimaryPackages = new HashSet<string>(publish.FailedItems, StringComparer.OrdinalIgnoreCase);
        var publishedArtifacts = new HashSet<string>(release.PublishedPackages, StringComparer.OrdinalIgnoreCase);
        var skippedArtifacts = new HashSet<string>(release.SkippedDuplicatePackages, StringComparer.OrdinalIgnoreCase);
        var failedArtifacts = new HashSet<string>(release.FailedPackages, StringComparer.OrdinalIgnoreCase);
        var attemptedPackages = publish.PackagePushResults.Keys
            .Concat(publish.PublishedItems)
            .Concat(publish.FailedItems)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var package in attemptedPackages)
        {
            var project = release.Projects.FirstOrDefault(candidate =>
                candidate.Packages.Contains(package, StringComparer.OrdinalIgnoreCase) ||
                candidate.SymbolPackages.Contains(package, StringComparer.OrdinalIgnoreCase));
            var artifacts = DotNetRepositoryReleaseService.GetPublishedArtifacts(
                project,
                package,
                includeCompanionSymbols: !publishSymbolsSeparately);
            if (!publish.PackagePushResults.TryGetValue(package, out var pushResult))
            {
                pushResult = new DotNetRepositoryReleaseService.PackagePushResult
                {
                    Outcome = failedPrimaryPackages.Contains(package)
                        ? DotNetRepositoryReleaseService.PackagePushOutcome.Failed
                        : skippedPrimaryPackages.Contains(package)
                            ? DotNetRepositoryReleaseService.PackagePushOutcome.SkippedDuplicate
                            : publishedPrimaryPackages.Contains(package)
                                ? DotNetRepositoryReleaseService.PackagePushOutcome.Published
                                : DotNetRepositoryReleaseService.PackagePushOutcome.Failed
                };
            }

            var outcomes = DotNetRepositoryReleaseService.ClassifyPublishedArtifacts(
                artifacts,
                pushResult,
                skipDuplicate);
            foreach (var artifact in artifacts)
            {
                if (outcomes[artifact] == DotNetRepositoryReleaseService.PackagePushOutcome.SkippedDuplicate)
                {
                    if (skippedArtifacts.Add(artifact))
                        release.SkippedDuplicatePackages.Add(artifact);
                }
                else if (outcomes[artifact] == DotNetRepositoryReleaseService.PackagePushOutcome.Published)
                {
                    if (publishedArtifacts.Add(artifact))
                        release.PublishedPackages.Add(artifact);
                }
                else if (failedArtifacts.Add(artifact))
                {
                    release.FailedPackages.Add(artifact);
                }
            }
        }
    }

    private void PublishExistingGitHubRelease(
        ProjectBuildHostExecutionResult existing,
        DotNetRepositoryReleaseResult release,
        ProjectBuildConfiguration configuration,
        string configPath,
        Action remotePublishAttempted,
        bool coordinatedReleaseCheckpointActive,
        IProjectBuildProgressReporter? progress)
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

        if (coordinatedReleaseCheckpointActive)
            ValidateCoordinatedProjectBuildGitHubRetrySafety(configuration, release);

        var preflightError = new ProjectBuildGitHubPreflightService(_logger).Validate(configuration, release, token!);
        if (!string.IsNullOrWhiteSpace(preflightError))
            throw new InvalidOperationException(preflightError);

        _logger.Info("Publishing GitHub release from existing package build result.");
        remotePublishAttempted();
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
            release,
            progress);

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
        PackageBuildExecutionMode mode,
        bool useDuplicateTolerantNuGetRetry = false,
        Action? remotePublishAttempted = null,
        bool coordinatedReleaseCheckpointActive = false)
    {
        var step = session.GetProjectBuildStep(segment);
        session.Start(step);
        try
        {
            var result = ExecuteProjectBuildSegment(
                plan,
                state,
                segment,
                mode,
                session.CreateProjectBuildProgressReporter(step),
                useDuplicateTolerantNuGetRetry,
                remotePublishAttempted,
                coordinatedReleaseCheckpointActive);
            var laneLabel = segment.Configuration.Name ?? result.ConfigPath;
            var checkpointKey = ResolveSynchronizedReleaseLaneKey(
                plan,
                ReleaseVersionSource.ProjectBuild,
                segment,
                laneLabel);
            CompletePackageBuildExecution(
                plan,
                state,
                result,
                ReleaseVersionSource.ProjectBuild,
                laneLabel,
                checkpointKey,
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
        PackageBuildExecutionMode mode,
        bool useDuplicateTolerantNuGetRetry = false,
        Action? remotePublishAttempted = null,
        bool coordinatedReleaseCheckpointActive = false)
    {
        var step = session.GetPackageBuildStep(segment);
        session.Start(step);
        try
        {
            var result = ExecutePackageBuildSegment(
                plan,
                state,
                segment,
                mode,
                session.CreateProjectBuildProgressReporter(step),
                useDuplicateTolerantNuGetRetry,
                remotePublishAttempted,
                coordinatedReleaseCheckpointActive);
            var laneLabel = segment.Configuration.Name ?? result.ConfigPath;
            var checkpointKey = ResolveSynchronizedReleaseLaneKey(
                plan,
                ReleaseVersionSource.PackageBuild,
                segment,
                laneLabel);
            CompletePackageBuildExecution(
                plan,
                state,
                result,
                ReleaseVersionSource.PackageBuild,
                laneLabel,
                checkpointKey,
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
        string checkpointKey,
        bool useAsReleaseVersionSource,
        bool provideLocalNuGetFeed,
        object segment,
        PackageBuildExecutionMode mode,
        string failurePrefix)
    {
        if (mode is not PackageBuildExecutionMode.PublishNuGet and not PackageBuildExecutionMode.PublishGitHub)
            RecordSynchronizedReleaseLaneCheckpoint(state, source, laneLabel, checkpointKey, result);

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
            checkpointKey,
            useAsReleaseVersionSource,
            result);
    }

    private ProjectBuildHostExecutionResult ExecuteProjectBuildSegment(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        ConfigurationProjectBuildSegment segment,
        PackageBuildExecutionMode mode,
        IProjectBuildProgressReporter? progress,
        bool useDuplicateTolerantNuGetRetry = false,
        Action? remotePublishAttempted = null,
        bool coordinatedReleaseCheckpointActive = false)
    {
        var cfg = segment.Configuration ?? throw new InvalidOperationException("ProjectBuild configuration is missing.");
        if (string.IsNullOrWhiteSpace(cfg.ConfigPath))
            throw new InvalidOperationException("ProjectBuild ConfigPath is required.");

        var configPath = ResolvePackageBuildPath(plan.ProjectRoot, cfg.ConfigPath);
        var configuration = LoadProjectBuildConfiguration(configPath, cfg);
        ApplySynchronizedNuGetRetryPolicy(
            configuration,
            useDuplicateTolerantNuGetRetry,
            coordinatedReleaseCheckpointActive);
        var laneLabel = cfg.Name ?? configPath;
        var checkpointKey = ResolveSynchronizedReleaseLaneKey(
            plan,
            ReleaseVersionSource.ProjectBuild,
            segment,
            laneLabel);
        ApplySynchronizedReleaseCheckpointVersion(
            plan,
            state,
            ReleaseVersionSource.ProjectBuild,
            laneLabel,
            checkpointKey,
            configuration);
        var releaseVersionFloor = ResolveCoordinatedVersionFloor(
            plan,
            state,
            ReleaseVersionSource.ProjectBuild,
            checkpointKey,
            cfg.UseAsReleaseVersionSource);
        if (mode is not PackageBuildExecutionMode.PublishNuGet and not PackageBuildExecutionMode.PublishGitHub)
            MarkSynchronizedReleaseLaneAttempted(state, checkpointKey);
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
            PublishGitHub = ResolvePublishGitHub(actions, mode),
            ReleaseVersionFloor = releaseVersionFloor,
            ReleaseVersionFloorProject = releaseVersionFloor is null
                ? null
                : plan.Release?.Configuration?.PrimaryProject,
            CoordinatedReleaseCheckpointActive = coordinatedReleaseCheckpointActive,
            Progress = progress
        };
        var sourceGuard = new PackagePublicationSourceGuard(plan, remotePublishAttempted);
        request.BuildSpecPrepared = sourceGuard.Capture;
        request.RemotePublishAttempted = sourceGuard.BeforeRemotePublish;

        _logger.Info($"Running package project build ({DescribePackageBuildMode(mode)}): {configPath}");
        return _packageBuildExecutor(request, configuration, configPath);
    }

    private ProjectBuildHostExecutionResult ExecutePackageBuildSegment(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        ConfigurationPackageBuildSegment segment,
        PackageBuildExecutionMode mode,
        IProjectBuildProgressReporter? progress,
        bool useDuplicateTolerantNuGetRetry = false,
        Action? remotePublishAttempted = null,
        bool coordinatedReleaseCheckpointActive = false)
    {
        var cfg = segment.Configuration ?? throw new InvalidOperationException("PackageBuild configuration is missing.");
        var projectBuildConfig = MapPackageBuildConfiguration(cfg, plan.ProjectRoot);
        ApplySynchronizedNuGetRetryPolicy(
            projectBuildConfig,
            useDuplicateTolerantNuGetRetry,
            coordinatedReleaseCheckpointActive);
        var laneLabel = cfg.Name ?? Path.Combine(plan.ProjectRoot, "module.packagebuild.inline.json");
        var checkpointKey = ResolveSynchronizedReleaseLaneKey(
            plan,
            ReleaseVersionSource.PackageBuild,
            segment,
            laneLabel);
        ApplySynchronizedReleaseCheckpointVersion(
            plan,
            state,
            ReleaseVersionSource.PackageBuild,
            laneLabel,
            checkpointKey,
            projectBuildConfig);
        var releaseVersionFloor = ResolveCoordinatedVersionFloor(
            plan,
            state,
            ReleaseVersionSource.PackageBuild,
            checkpointKey,
            cfg.UseAsReleaseVersionSource);
        if (mode is not PackageBuildExecutionMode.PublishNuGet and not PackageBuildExecutionMode.PublishGitHub)
            MarkSynchronizedReleaseLaneAttempted(state, checkpointKey);
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
            PublishGitHub = ResolvePublishGitHub(actions, mode),
            ReleaseVersionFloor = releaseVersionFloor,
            ReleaseVersionFloorProject = releaseVersionFloor is null
                ? null
                : plan.Release?.Configuration?.PrimaryProject,
            CoordinatedReleaseCheckpointActive = coordinatedReleaseCheckpointActive,
            Progress = progress
        };
        var sourceGuard = new PackagePublicationSourceGuard(plan, remotePublishAttempted);
        request.BuildSpecPrepared = sourceGuard.Capture;
        request.RemotePublishAttempted = sourceGuard.BeforeRemotePublish;

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
        return ProjectBuildConfigurationAdapter.ApplyReference(configuration, reference);
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

        if (gateMode == ConfigurationGateMode.Documentation &&
            mode is PackageBuildExecutionMode.DependencyBuild or PackageBuildExecutionMode.BuildOnly)
        {
            target.CertificateThumbprint = null;
            target.SignAssemblies = false;
            target.SignPackages = false;
            target.CreateReleaseZip = false;
        }
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
            PackageBuildExecutionMode.DependencyBuild when gateMode == ConfigurationGateMode.Documentation => false,
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
        => ShouldRunPackageBuildBeforeModule(plan.Release, buildBeforeModule);

    private static bool ShouldRunPackageBuildBeforeModule(
        ConfigurationReleaseSegment? release,
        bool buildBeforeModule)
        => ModulePipelinePackageBuildOrder.ShouldRunBeforeModule(release, buildBeforeModule);

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
        var target = ProjectBuildConfigurationAdapter.FromPackageBuild(source);
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

    private sealed class PackagePublicationSourceGuard
    {
        private readonly ModulePipelinePlan _plan;
        private readonly Action? _downstream;
        private DotNetRepositoryReleaseSpec? _spec;

        internal PackagePublicationSourceGuard(ModulePipelinePlan plan, Action? downstream)
        {
            _plan = plan;
            _downstream = downstream;
        }

        internal void Capture(DotNetRepositoryReleaseSpec spec) => _spec = spec;

        internal void BeforeRemotePublish()
        {
            if (_plan.GenerateReleaseProvenance)
            {
                if (_spec is null)
                    throw new InvalidOperationException("Package source provenance was not prepared before publication.");
                ValidatePackageReleaseSourceUnchanged(_plan, _spec);
            }
            _downstream?.Invoke();
        }
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
