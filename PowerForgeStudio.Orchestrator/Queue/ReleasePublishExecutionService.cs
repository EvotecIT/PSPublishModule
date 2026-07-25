using PowerForge;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Host;
using PowerForgeStudio.Orchestrator.Portfolio;
using PowerForgeStudio.Orchestrator.PowerShell;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed partial class ReleasePublishExecutionService : IReleasePublishExecutionService
{
    private readonly RepositoryCatalogScanner _catalogScanner;
    private readonly ModuleBuildHostService _moduleBuildHostService;
    private readonly ProjectBuildHostService _projectBuildHostService;
    private readonly ProjectBuildCommandHostService _projectBuildCommandHostService;
    private readonly ProjectBuildPublishHostService _projectBuildPublishHostService;
    private readonly ReleaseQueueCheckpointSerializer _checkpointSerializer = new();
    private readonly ReleaseQueueTargetProjectionService _targetProjectionService = new();
    private readonly Func<DotNetNuGetPushRequest, CancellationToken, Task<DotNetNuGetPushResult>> _pushNuGetPackageAsync;
    private readonly Func<GitHubReleasePublishRequest, CancellationToken, Task<GitHubReleasePublishResult>> _publishGitHubReleaseAsync;
    private readonly Func<ModuleCheckpointPublishRequest, CancellationToken, Task<ModulePublishResult>> _publishCheckpointedModuleAsync;
    private readonly Func<string, string, CancellationToken, PowerForgeReleaseResult> _publishUnifiedRelease;

    public ReleasePublishExecutionService()
        : this(
            new RepositoryCatalogScanner(),
            new ModuleBuildHostService(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(),
            new ProjectBuildPublishHostService(),
            (request, cancellationToken) => new DotNetNuGetClient().PushPackageAsync(request, cancellationToken),
            (request, _) => Task.FromResult(new GitHubReleasePublisher(new NullLogger()).PublishRelease(request)))
    {
    }

    internal ReleasePublishExecutionService(
        RepositoryCatalogScanner catalogScanner,
        ModuleBuildHostService moduleBuildHostService,
        ProjectBuildHostService projectBuildHostService,
        ProjectBuildCommandHostService projectBuildCommandHostService,
        ProjectBuildPublishHostService projectBuildPublishHostService,
        Func<DotNetNuGetPushRequest, CancellationToken, Task<DotNetNuGetPushResult>> pushNuGetPackageAsync,
        Func<GitHubReleasePublishRequest, CancellationToken, Task<GitHubReleasePublishResult>>? publishGitHubReleaseAsync = null,
        Func<ModuleCheckpointPublishRequest, CancellationToken, Task<ModulePublishResult>>? publishCheckpointedModuleAsync = null,
        Func<string, string, PowerForgeReleaseResult>? publishUnifiedRelease = null,
        Func<string, string, CancellationToken, PowerForgeReleaseResult>? publishUnifiedReleaseWithCancellation = null)
    {
        _catalogScanner = catalogScanner;
        _moduleBuildHostService = moduleBuildHostService;
        _projectBuildHostService = projectBuildHostService;
        _projectBuildCommandHostService = projectBuildCommandHostService;
        _projectBuildPublishHostService = projectBuildPublishHostService;
        _pushNuGetPackageAsync = pushNuGetPackageAsync;
        _publishGitHubReleaseAsync = publishGitHubReleaseAsync ?? ((request, _) => Task.FromResult(new GitHubReleasePublisher(new NullLogger()).PublishRelease(request)));
        _publishCheckpointedModuleAsync = publishCheckpointedModuleAsync ??
            ((request, _) => Task.FromResult(new ModulePublisher(new NullLogger()).PublishCheckpointed(request)));
        _publishUnifiedRelease = publishUnifiedReleaseWithCancellation
            ?? (publishUnifiedRelease is not null
                ? (configPath, stateJson, _) => publishUnifiedRelease(configPath, stateJson)
                : PublishUnifiedRelease);
    }

    public IReadOnlyList<ReleasePublishTarget> BuildPendingTargets(IEnumerable<ReleaseQueueItem> queueItems)
    {
        return _targetProjectionService.BuildTargets(
            queueItems,
            ReleaseQueueStage.Publish,
            TryDeserializeSigningResult,
            (item, signingResult) => ProjectPendingTargets(item, signingResult),
            static target => $"{target.RootPath}|{target.AdapterKind}|{target.TargetKind}|{target.SourcePath}");
    }

    public async Task<ReleasePublishExecutionResult> ExecuteAsync(ReleaseQueueItem queueItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queueItem);

        var signingResult = TryDeserializeSigningResult(queueItem);
        if (signingResult is null)
        {
            return new ReleasePublishExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: false,
                Summary: "Publish checkpoint could not be read from queue state.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: [
                    FailedReceipt(queueItem.RootPath, queueItem.RepositoryName, "Publish", "Queue checkpoint", null, "Queue state is missing the signing checkpoint.")
                ]);
        }

        var pendingTargets = BuildPendingTargets([queueItem]);
        var projectionFailure = pendingTargets.FirstOrDefault(static target =>
            string.Equals(target.TargetKind, "ConfigurationError", StringComparison.OrdinalIgnoreCase));
        if (projectionFailure is not null)
        {
            return new ReleasePublishExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: false,
                Summary: "Unified release publish targets could not be projected.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: [
                    FailedReceipt(
                        queueItem.RootPath,
                        queueItem.RepositoryName,
                        projectionFailure.AdapterKind,
                        "Configuration",
                        projectionFailure.SourcePath,
                        projectionFailure.Destination)
                ]);
        }

        if (pendingTargets.Count == 0)
        {
            return new ReleasePublishExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: true,
                Summary: "No publish targets were detected for this queue item.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: [
                    SkippedReceipt(
                        queueItem.RootPath,
                        queueItem.RepositoryName,
                        "Publish",
                        "Publish",
                        null,
                        "No external publish targets were detected for this queue item, so verification can be skipped.")
                ]);
        }

        if (!IsPublishEnabled())
        {
            return new ReleasePublishExecutionResult(
                RootPath: queueItem.RootPath,
                Succeeded: false,
                Summary: "Publish is disabled. Set RELEASE_OPS_STUDIO_ENABLE_PUBLISH=true to unlock external publishing.",
                SourceCheckpointStateJson: queueItem.CheckpointStateJson,
                Receipts: pendingTargets.Select(target => FailedReceipt(
                    queueItem.RootPath,
                    queueItem.RepositoryName,
                    target.AdapterKind,
                    target.TargetKind,
                    target.Destination,
                    "Publish is disabled. Set RELEASE_OPS_STUDIO_ENABLE_PUBLISH=true to unlock external publishing.")).ToList());
        }

        var repository = _catalogScanner.InspectRepository(queueItem.RootPath);
        var receipts = new List<ReleasePublishReceipt>();
        var unifiedOwnsGitHub = UnifiedReleaseOwnsGitHub(repository.UnifiedReleaseConfigPath);

        if (!string.IsNullOrWhiteSpace(repository.ProjectBuildScriptPath))
        {
            var projectReceipts = await ExecuteProjectPublishAsync(
                repository,
                signingResult,
                cancellationToken,
                unifiedOwnsGitHub);
            receipts.AddRange(projectReceipts);
            if (projectReceipts.Any(static receipt =>
                    receipt.Status == ReleasePublishReceiptStatus.Failed))
            {
                return ReleaseQueueExecutionResultFactory.CreatePublishResult(queueItem, receipts);
            }
        }

        if (!string.IsNullOrWhiteSpace(repository.ModuleBuildScriptPath))
        {
            var moduleReceipts = await ExecuteModulePublishAsync(
                repository,
                signingResult,
                cancellationToken,
                unifiedOwnsGitHub);
            receipts.AddRange(moduleReceipts);
            if (moduleReceipts.Any(static receipt =>
                    receipt.Status == ReleasePublishReceiptStatus.Failed))
            {
                return ReleaseQueueExecutionResultFactory.CreatePublishResult(queueItem, receipts);
            }
        }

        if (!string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath))
        {
            var unifiedSpec = PowerForgeReleaseService.LoadConfiguration(repository.UnifiedReleaseConfigPath!);
            if (GetCheckpointedModulePackagePlans(signingResult).Length > 0)
            {
                var modulePackageReceipts = await ExecuteModuleOwnedPackagePublishAsync(
                    repository,
                    unifiedSpec,
                    signingResult,
                    cancellationToken);
                receipts.AddRange(modulePackageReceipts);
                if (modulePackageReceipts.Any(static receipt =>
                        receipt.Status == ReleasePublishReceiptStatus.Failed))
                {
                    return ReleaseQueueExecutionResultFactory.CreatePublishResult(queueItem, receipts);
                }
            }
            receipts.AddRange(await ExecuteUnifiedPublishAsync(repository, signingResult, cancellationToken));
        }

        if (receipts.Count == 0)
        {
            receipts.Add(FailedReceipt(queueItem.RootPath, queueItem.RepositoryName, "Publish", "Publish", null, "No publish-capable adapter execution was produced."));
        }

        return ReleaseQueueExecutionResultFactory.CreatePublishResult(queueItem, receipts);
    }

}

public sealed partial class ReleasePublishExecutionService
{
    private async Task<(bool Succeeded, string? ErrorMessage)> PublishNugetPackageAsync(
        string packagePath,
        string apiKey,
        string source,
        bool skipDuplicate,
        CancellationToken cancellationToken)
    {
        var result = await _pushNuGetPackageAsync(
            new DotNetNuGetPushRequest(
                packagePath: packagePath,
                apiKey: apiKey,
                source: source,
                skipDuplicate: skipDuplicate,
                workingDirectory: Path.GetDirectoryName(packagePath)),
            cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
            return (true, null);

        return (false, result.ErrorMessage);
    }

    private async Task<GitHubReleaseExecutionResult> PublishGitHubReleaseAsync(
        string repositoryRoot,
        string owner,
        string repo,
        string token,
        string tag,
        string releaseName,
        IReadOnlyList<string> assetPaths,
        bool generateReleaseNotes,
        bool isPreRelease,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _publishGitHubReleaseAsync(
                new GitHubReleasePublishRequest {
                    Owner = owner,
                    Repository = repo,
                    Token = token,
                    TagName = tag,
                    ReleaseName = releaseName,
                    GenerateReleaseNotes = generateReleaseNotes,
                    IsPreRelease = isPreRelease,
                    ReuseExistingReleaseOnConflict = true,
                    AssetFilePaths = assetPaths
                },
                cancellationToken).ConfigureAwait(false);

            return new GitHubReleaseExecutionResult(
                result.Succeeded,
                result.HtmlUrl,
                result.Succeeded ? null : "GitHub publish failed.");
        }
        catch (Exception ex)
        {
            return new GitHubReleaseExecutionResult(false, null, FirstLine(ex.Message) ?? "GitHub publish failed.");
        }
    }

    private static bool IsPublishEnabled()
        => string.Equals(Environment.GetEnvironmentVariable("RELEASE_OPS_STUDIO_ENABLE_PUBLISH"), "true", StringComparison.OrdinalIgnoreCase);

    private ReleaseSigningExecutionResult? TryDeserializeSigningResult(ReleaseQueueItem queueItem)
        => _checkpointSerializer.TryDeserialize<ReleaseSigningExecutionResult>(queueItem.CheckpointStateJson);

    private static ReleasePublishReceipt FailedReceipt(string rootPath, string repositoryName, string adapterKind, string targetKind, string? destination, string summary)
        => ReleaseQueueReceiptFactory.FailedPublishReceipt(rootPath, repositoryName, adapterKind, targetKind, destination, summary);

    private static ReleasePublishReceipt SkippedReceipt(string rootPath, string repositoryName, string adapterKind, string targetKind, string? destination, string summary)
        => ReleaseQueueReceiptFactory.SkippedPublishReceipt(rootPath, repositoryName, adapterKind, targetKind, destination, summary);

    private static string ResolveModuleRepositoryName(PublishConfiguration publishConfig)
        => publishConfig.Repository?.Name
           ?? publishConfig.RepositoryName
           ?? "PSGallery";

    private static string? FindZipAsset(ReleaseSigningExecutionResult signingResult, string? projectName = null)
    {
        var zipAssets = signingResult.Receipts
            .Select(receipt => receipt.ArtifactPath)
            .Where(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (zipAssets.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(projectName))
        {
            return zipAssets[0];
        }

        return zipAssets.FirstOrDefault(path => Path.GetFileName(path).Contains(projectName, StringComparison.OrdinalIgnoreCase))
               ?? zipAssets[0];
    }

    private static string? FirstLine(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();

    private sealed record ModulePackageDetails(string ModuleName, string Version, string? PreRelease, string PackagePath, IReadOnlyList<string> ZipAssets);
    private sealed record ModuleManifestInfo(string ModuleName, string Version, string? PreRelease);

    private sealed record GitHubReleaseExecutionResult(bool Succeeded, string? ReleaseUrl, string? ErrorMessage);
}

public sealed partial class ReleasePublishExecutionService
{
    private async Task<DotNetRepositoryReleaseResult?> GenerateProjectPlanAsync(PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository, CancellationToken cancellationToken)
    {
        var scriptPath = repository.ProjectBuildScriptPath!;
        var configPath = RepositoryPlanPreviewService.ResolveProjectConfigPath(scriptPath, repository.RootPath);
        var planPath = PowerForgeStudioHostPaths.GetRuntimeFilePath(repository.Name, "project-publish", "project.publish.plan.json");
        if (!string.IsNullOrWhiteSpace(configPath))
        {
            var execution = _projectBuildHostService.Execute(new ProjectBuildHostRequest {
                ConfigPath = configPath,
                PlanOutputPath = planPath,
                ExecuteBuild = false,
                PlanOnly = true,
                UpdateVersions = false,
                Build = false,
                PublishNuget = false,
                PublishGitHub = false
            });

            if (!execution.Success || !File.Exists(planPath))
            {
                return null;
            }

            return execution.Result.Release;
        }
        else
        {
            var execution = await _projectBuildCommandHostService.GeneratePlanAsync(new ProjectBuildCommandPlanRequest {
                RepositoryRoot = repository.RootPath,
                PlanOutputPath = planPath,
                ConfigPath = configPath,
                ModulePath = PowerForgeStudioHostPaths.ResolvePSPublishModulePath()
            }, cancellationToken);
            if (!execution.Succeeded || !File.Exists(planPath))
            {
                return null;
            }
        }

        return await ReadProjectPlanFileAsync(planPath, cancellationToken);
    }

    private PowerForgeReleaseResult? ReadUnifiedReleaseCheckpoint(ReleaseSigningExecutionResult signingResult)
    {
        var buildResult = _checkpointSerializer.TryDeserialize<ReleaseBuildExecutionResult>(
            signingResult.SourceCheckpointStateJson);
        return string.IsNullOrWhiteSpace(buildResult?.UnifiedReleaseStateJson)
            ? null
            : _checkpointSerializer.TryDeserialize<PowerForgeReleaseResult>(
                buildResult.UnifiedReleaseStateJson);
    }

    private DotNetRepositoryReleaseResult? ReadCheckpointedProjectPlan(
        ReleaseSigningExecutionResult signingResult)
        => ReadUnifiedReleaseCheckpoint(signingResult)?.Packages?.Result.Release;

    private static IReadOnlyList<string> ResolveProjectGitHubAssets(DotNetRepositoryReleaseResult plan, ReleaseSigningExecutionResult signingResult, string? projectName = null)
    {
        var assets = plan.Projects
            .Where(project => project.IsPackable && (string.IsNullOrWhiteSpace(projectName) || string.Equals(project.ProjectName, projectName, StringComparison.OrdinalIgnoreCase)))
            .Select(project => !string.IsNullOrWhiteSpace(project.ReleaseZipPath) && File.Exists(project.ReleaseZipPath)
                ? project.ReleaseZipPath!
                : FindZipAsset(signingResult, project.ProjectName))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return assets;
    }

    private async Task<DotNetRepositoryReleaseResult?> ReadProjectPlanFileAsync(string planPath, CancellationToken cancellationToken)
    {
        var plan = _checkpointSerializer.TryDeserialize<ProjectReleasePlanFile>(
            await File.ReadAllTextAsync(planPath, cancellationToken).ConfigureAwait(false));
        if (plan is null)
        {
            return null;
        }

        var result = new DotNetRepositoryReleaseResult {
            Success = plan.Success,
            ErrorMessage = plan.ErrorMessage
        };

        foreach (var project in plan.Projects)
        {
            result.Projects.Add(new DotNetRepositoryProjectResult {
                ProjectName = project.ProjectName ?? string.Empty,
                IsPackable = project.IsPackable,
                OldVersion = project.OldVersion,
                NewVersion = project.NewVersion,
                ReleaseZipPath = project.ReleaseZipPath
            });
        }

        return result;
    }

    private sealed class ProjectReleasePlanFile
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<ProjectReleaseProjectFile> Projects { get; set; } = [];
    }

    private sealed class ProjectReleaseProjectFile
    {
        public string? ProjectName { get; set; }
        public bool IsPackable { get; set; }
        public string? OldVersion { get; set; }
        public string? NewVersion { get; set; }
        public string? ReleaseZipPath { get; set; }
    }
}

public sealed partial class ReleasePublishExecutionService
{
    private async Task<IReadOnlyList<ReleasePublishReceipt>> ExecuteProjectPublishAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        ReleaseSigningExecutionResult signingResult,
        CancellationToken cancellationToken,
        bool suppressGitHub)
    {
        var scriptPath = repository.ProjectBuildScriptPath!;
        var configPath = RepositoryPlanPreviewService.ResolveProjectConfigPath(scriptPath, repository.RootPath);
        if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
        {
            return [
                FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), "Project publish", null, $"Project config was not found at {configPath}.")
            ];
        }

        var config = _projectBuildPublishHostService.LoadConfiguration(configPath);
        var receipts = new List<ReleasePublishReceipt>();

        if (config.PublishNuget)
        {
            if (string.IsNullOrWhiteSpace(config.PublishApiKey))
            {
                receipts.Add(FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), "NuGet publish", config.PublishSource, "NuGet publishing is enabled but no API key was resolved."));
            }
            else
            {
                var packages = signingResult.Receipts
                    .Where(receipt => string.Equals(receipt.AdapterKind, ReleaseBuildAdapterKind.ProjectBuild.ToString(), StringComparison.OrdinalIgnoreCase))
                    .Where(receipt => receipt.Status == Domain.Signing.ReleaseSigningReceiptStatus.Signed)
                    .Select(receipt => receipt.ArtifactPath)
                    .Where(path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (!string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath))
                {
                    var checkpointedPlan = ReadCheckpointedProjectPlan(signingResult);
                    if (checkpointedPlan is null)
                    {
                        packages.Clear();
                    }
                    else
                    {
                        var approvedPackageNames = checkpointedPlan.Projects
                            .SelectMany(static project => project.Packages)
                            .Select(Path.GetFileName)
                            .Where(static name => !string.IsNullOrWhiteSpace(name))
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        packages = packages
                            .Where(path => approvedPackageNames.Contains(Path.GetFileName(path)))
                            .ToList();
                    }
                }

                if (packages.Count == 0)
                {
                    receipts.Add(FailedReceipt(
                        repository.RootPath,
                        repository.Name,
                        ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                        "NuGet publish",
                        config.PublishSource,
                        string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath)
                            ? "No signed .nupkg packages were found for publishing."
                            : "No signed checkpointed .nupkg packages were found for publishing."));
                }
                else
                {
                    foreach (var package in packages)
                    {
                        var result = await PublishNugetPackageAsync(
                            package,
                            config.PublishApiKey!,
                            config.PublishSource,
                            config.SkipDuplicate,
                            cancellationToken);
                        receipts.Add(ReleaseQueueReceiptFactory.CreatePublishReceipt(
                            repository.RootPath,
                            repository.Name,
                            ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                            Path.GetFileName(package),
                            "NuGet",
                            config.PublishSource,
                            result.Succeeded ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
                            result.Succeeded ? "Package pushed with dotnet nuget push." : result.ErrorMessage!,
                            package));
                        if (!result.Succeeded && config.PublishFailFast)
                            break;
                    }
                }
            }
        }

        if (config.PublishFailFast &&
            receipts.Any(static receipt => receipt.Status == ReleasePublishReceiptStatus.Failed))
        {
            return receipts;
        }

        if (config.PublishGitHub && !suppressGitHub)
        {
            receipts.AddRange(await ExecuteProjectGitHubPublishAsync(repository, config, signingResult, cancellationToken));
        }

        return receipts;
    }

    private async Task<IReadOnlyList<ReleasePublishReceipt>> ExecuteProjectGitHubPublishAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        ProjectBuildPublishHostConfiguration config,
        ReleaseSigningExecutionResult signingResult,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(config.GitHubToken))
        {
            return [
                FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), "GitHub release", null, "GitHub publishing is enabled but no access token was resolved.")
            ];
        }

        if (string.IsNullOrWhiteSpace(config.GitHubUsername) || string.IsNullOrWhiteSpace(config.GitHubRepositoryName))
        {
            return [
                FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), "GitHub release", null, "GitHubUsername and GitHubRepositoryName are required for GitHub publishing.")
            ];
        }

        var plan = ReadCheckpointedProjectPlan(signingResult);
        if (plan is null && string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath))
            plan = await GenerateProjectPlanAsync(repository, cancellationToken);
        if (plan is null)
        {
            return [
                FailedReceipt(
                    repository.RootPath,
                    repository.Name,
                    ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                    "GitHub release",
                    $"{config.GitHubUsername}/{config.GitHubRepositoryName}",
                    string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath)
                        ? "Project release plan could not be generated for GitHub publishing."
                        : "The checkpointed unified package release plan is missing; rebuild before publishing.")
            ];
        }

        var repoName = config.GitHubRepositoryName!.Trim();
        var owner = config.GitHubUsername!.Trim();
        var publishSummary = _projectBuildPublishHostService.PublishGitHub(config, plan);

        if (publishSummary.PerProject)
        {
            return plan.Projects
                .Where(project => project.IsPackable)
                .Select(project => {
                    var publishResult = publishSummary.Results.FirstOrDefault(result => string.Equals(result.ProjectName, project.ProjectName, StringComparison.OrdinalIgnoreCase));
                    var sourcePath = ResolveProjectGitHubAssets(plan, signingResult, project.ProjectName).FirstOrDefault();
                    return ReleaseQueueReceiptFactory.CreatePublishReceipt(
                        repository.RootPath,
                        repository.Name,
                        ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                        $"{project.ProjectName} GitHub release",
                        "GitHub",
                        publishResult?.ReleaseUrl ?? $"{owner}/{repoName}",
                        publishResult?.Success == true ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
                        publishResult?.Success == true
                            ? $"GitHub release {publishResult.TagName} published."
                            : publishResult?.ErrorMessage ?? "GitHub publish failed.",
                        sourcePath);
                })
                .ToList();
        }

        var assets = ResolveProjectGitHubAssets(plan, signingResult);
        if (assets.Count == 0)
        {
            return [
                FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), "GitHub release", $"{owner}/{repoName}", "No release zips were found for GitHub publishing.")
            ];
        }

        return [
            ReleaseQueueReceiptFactory.CreatePublishReceipt(
                repository.RootPath,
                repository.Name,
                ReleaseBuildAdapterKind.ProjectBuild.ToString(),
                "GitHub release",
                "GitHub",
                publishSummary.SummaryReleaseUrl ?? $"{owner}/{repoName}",
                publishSummary.Success ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
                publishSummary.Success
                    ? $"GitHub release {publishSummary.SummaryTag} published with {assets.Count} asset(s)."
                    : publishSummary.ErrorMessage ?? "GitHub publish failed.",
                assets.FirstOrDefault())
        ];
    }
}
