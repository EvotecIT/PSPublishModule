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
    private readonly Func<RepositoryPublishRequest, CancellationToken, Task<RepositoryPublishResult>> _publishRepositoryAsync;

    public ReleasePublishExecutionService()
        : this(
            new RepositoryCatalogScanner(),
            new ModuleBuildHostService(),
            new ProjectBuildHostService(),
            new ProjectBuildCommandHostService(),
            new ProjectBuildPublishHostService(),
            (request, cancellationToken) => new DotNetNuGetClient().PushPackageAsync(request, cancellationToken),
            (request, _) => Task.FromResult(new GitHubReleasePublisher(new NullLogger()).PublishRelease(request)),
            (request, _) => Task.FromResult(new RepositoryPublisher(new NullLogger()).Publish(request)))
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
        Func<RepositoryPublishRequest, CancellationToken, Task<RepositoryPublishResult>>? publishRepositoryAsync = null)
    {
        _catalogScanner = catalogScanner;
        _moduleBuildHostService = moduleBuildHostService;
        _projectBuildHostService = projectBuildHostService;
        _projectBuildCommandHostService = projectBuildCommandHostService;
        _projectBuildPublishHostService = projectBuildPublishHostService;
        _pushNuGetPackageAsync = pushNuGetPackageAsync;
        _publishGitHubReleaseAsync = publishGitHubReleaseAsync ?? ((request, _) => Task.FromResult(new GitHubReleasePublisher(new NullLogger()).PublishRelease(request)));
        _publishRepositoryAsync = publishRepositoryAsync ?? ((request, _) => Task.FromResult(new RepositoryPublisher(new NullLogger()).Publish(request)));
    }

    public IReadOnlyList<ReleasePublishTarget> BuildPendingTargets(IEnumerable<ReleaseQueueItem> queueItems)
    {
        return _targetProjectionService.BuildTargets(
            queueItems,
            ReleaseQueueStage.Publish,
            TryDeserializeSigningResult,
            static (item, signingResult) => ProjectPendingTargets(item, signingResult),
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

        if (!string.IsNullOrWhiteSpace(repository.ProjectBuildScriptPath))
        {
            receipts.AddRange(await ExecuteProjectPublishAsync(repository, signingResult, cancellationToken));
        }

        if (!string.IsNullOrWhiteSpace(repository.ModuleBuildScriptPath))
        {
            receipts.AddRange(await ExecuteModulePublishAsync(repository, signingResult, cancellationToken));
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
    private async Task<(bool Succeeded, string? ErrorMessage)> PublishNugetPackageAsync(string packagePath, string apiKey, string source, CancellationToken cancellationToken)
    {
        var result = await _pushNuGetPackageAsync(
            new DotNetNuGetPushRequest(
                packagePath: packagePath,
                apiKey: apiKey,
                source: source,
                skipDuplicate: true,
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

    private static IEnumerable<ReleasePublishTarget> ProjectPendingTargets(ReleaseQueueItem item, ReleaseSigningExecutionResult signingResult)
    {
        var targets = new List<ReleasePublishTarget>();
        var receipts = signingResult.Receipts ?? [];
        var grouped = receipts.GroupBy(receipt => receipt.AdapterKind, StringComparer.OrdinalIgnoreCase);
        foreach (var group in grouped)
        {
            var adapterKind = group.Key;
            var paths = group.Select(receipt => receipt.ArtifactPath).ToArray();
            if (paths.Any(path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)))
            {
                targets.Add(new ReleasePublishTarget(
                    RootPath: item.RootPath,
                    RepositoryName: item.RepositoryName,
                    AdapterKind: adapterKind,
                    TargetName: $"{group.Count(path => path.ArtifactPath.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))} NuGet package(s)",
                    TargetKind: "NuGet",
                    SourcePath: paths.FirstOrDefault(path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)),
                    Destination: "Configured NuGet feed"));
            }

            if (paths.Any(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
            {
                targets.Add(new ReleasePublishTarget(
                    RootPath: item.RootPath,
                    RepositoryName: item.RepositoryName,
                    AdapterKind: adapterKind,
                    TargetName: $"{paths.Count(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))} GitHub asset(s)",
                    TargetKind: "GitHub",
                    SourcePath: paths.FirstOrDefault(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)),
                    Destination: "Configured GitHub release"));
            }

            if (string.Equals(adapterKind, ReleaseBuildAdapterKind.ModuleBuild.ToString(), StringComparison.OrdinalIgnoreCase) &&
                group.Any(receipt => string.Equals(receipt.ArtifactKind, "Directory", StringComparison.OrdinalIgnoreCase)))
            {
                targets.Add(new ReleasePublishTarget(
                    RootPath: item.RootPath,
                    RepositoryName: item.RepositoryName,
                    AdapterKind: adapterKind,
                    TargetName: "Module package",
                    TargetKind: "PowerShellRepository",
                    SourcePath: group.First(receipt => string.Equals(receipt.ArtifactKind, "Directory", StringComparison.OrdinalIgnoreCase)).ArtifactPath,
                    Destination: "Configured PowerShell repository"));
            }
        }

        return targets;
    }

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

    private async Task<IReadOnlyList<PublishConfiguration>> ExportModulePublishConfigsAsync(string repositoryRoot, string scriptPath, CancellationToken cancellationToken)
    {
        var repositoryName = Path.GetFileName(repositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var exportPath = PowerForgeStudioHostPaths.GetRuntimeFilePath(repositoryName, "module-publish", "powerforge.publish.json");
        var execution = await _moduleBuildHostService.ExportPipelineJsonAsync(new ModuleBuildHostExportRequest {
            RepositoryRoot = repositoryRoot,
            ScriptPath = scriptPath,
            ModulePath = PowerForgeStudioHostPaths.ResolvePSPublishModulePath(),
            OutputPath = exportPath
        }, cancellationToken);
        if (execution.ExitCode != 0 || !File.Exists(exportPath))
        {
            return [];
        }

        try
        {
            return new ModulePublishConfigurationReader().Read(exportPath);
        }
        catch
        {
            return [];
        }
    }

    private async Task<ModulePackageDetails?> ResolveModulePackageDetailsAsync(
        string repositoryRoot,
        string repositoryName,
        ReleaseSigningExecutionResult signingResult,
        CancellationToken cancellationToken)
    {
        var receipts = signingResult.Receipts
            .Where(receipt => string.Equals(receipt.AdapterKind, ReleaseBuildAdapterKind.ModuleBuild.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var candidateManifest = receipts
            .Where(receipt => receipt.ArtifactPath.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase) && File.Exists(receipt.ArtifactPath))
            .Select(receipt => receipt.ArtifactPath)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(candidateManifest))
        {
            foreach (var directory in receipts.Where(receipt => string.Equals(receipt.ArtifactKind, "Directory", StringComparison.OrdinalIgnoreCase)))
            {
                if (!Directory.Exists(directory.ArtifactPath))
                {
                    continue;
                }

                candidateManifest = Directory.EnumerateFiles(directory.ArtifactPath, "*.psd1", SearchOption.AllDirectories)
                    .FirstOrDefault(path => !path.Contains($"{Path.DirectorySeparatorChar}en-US{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(candidateManifest))
                {
                    break;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(candidateManifest))
        {
            return null;
        }

        var packagePath = Path.GetDirectoryName(candidateManifest);
        var manifestInfo = await ReadModuleManifestAsync(repositoryRoot, candidateManifest, cancellationToken)
                          ?? new ModuleManifestInfo(repositoryName, "0.0.0", null);
        var zipAssets = receipts
            .Select(receipt => receipt.ArtifactPath)
            .Where(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ModulePackageDetails(
            ModuleName: manifestInfo.ModuleName,
            Version: manifestInfo.Version,
            PreRelease: manifestInfo.PreRelease,
            PackagePath: packagePath!,
            ZipAssets: zipAssets);
    }

    private async Task<ModuleManifestInfo?> ReadModuleManifestAsync(string repositoryRoot, string manifestPath, CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        try
        {
            var metadata = new ModuleManifestMetadataReader().Read(manifestPath);
            return new ModuleManifestInfo(metadata.ModuleName, metadata.ModuleVersion, metadata.PreRelease);
        }
        catch
        {
            return null;
        }
    }

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
    private async Task<IReadOnlyList<ReleasePublishReceipt>> ExecuteModulePublishAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        ReleaseSigningExecutionResult signingResult,
        CancellationToken cancellationToken)
    {
        var publishConfigs = await ExportModulePublishConfigsAsync(repository.RootPath, repository.ModuleBuildScriptPath!, cancellationToken);
        if (publishConfigs.Count == 0)
        {
            return [];
        }

        var packageDetails = await ResolveModulePackageDetailsAsync(repository.RootPath, repository.Name, signingResult, cancellationToken);
        var receipts = new List<ReleasePublishReceipt>();
        foreach (var publishConfig in publishConfigs.Where(config => config.Enabled))
        {
            if (publishConfig.Destination == PublishDestination.GitHub)
            {
                receipts.Add(await ExecuteModuleGitHubPublishAsync(repository, publishConfig, packageDetails, cancellationToken));
                continue;
            }

            receipts.Add(await ExecuteModuleRepositoryPublishAsync(repository, publishConfig, packageDetails, cancellationToken));
        }

        return receipts;
    }

    private async Task<ReleasePublishReceipt> ExecuteModuleRepositoryPublishAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        PublishConfiguration publishConfig,
        ModulePackageDetails? packageDetails,
        CancellationToken cancellationToken)
    {
        var destination = ResolveModuleRepositoryName(publishConfig);
        if (packageDetails is null || string.IsNullOrWhiteSpace(packageDetails.PackagePath))
        {
            return FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ModuleBuild.ToString(), "Module publish", destination, "No publishable module package path was captured from the build artefacts.");
        }

        if (string.IsNullOrWhiteSpace(publishConfig.ApiKey))
        {
            return FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ModuleBuild.ToString(), "Module publish", destination, "Module publish is enabled but no API key was resolved.");
        }

        try
        {
            var publishResult = await _publishRepositoryAsync(
                new RepositoryPublishRequest {
                    Path = packageDetails.PackagePath,
                    IsNupkg = false,
                    RepositoryName = destination,
                    Tool = publishConfig.Tool,
                    ApiKey = publishConfig.ApiKey,
                    Repository = publishConfig.Repository,
                    SkipDependenciesCheck = true,
                    SkipModuleManifestValidate = false
                },
                cancellationToken).ConfigureAwait(false);

            return ReleaseQueueReceiptFactory.CreatePublishReceipt(
                repository.RootPath,
                repository.Name,
                ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                packageDetails.ModuleName,
                "PowerShellRepository",
                publishResult.RepositoryName,
                ReleasePublishReceiptStatus.Published,
                $"Module published to {publishResult.RepositoryName} using {publishResult.Tool}.",
                packageDetails.PackagePath);
        }
        catch (Exception ex)
        {
            return ReleaseQueueReceiptFactory.FailedPublishReceipt(
                repository.RootPath,
                repository.Name,
                ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                packageDetails.ModuleName,
                destination,
                FirstLine(ex.Message) ?? "Module publish failed.",
                "PowerShellRepository",
                packageDetails.PackagePath);
        }
    }

    private async Task<ReleasePublishReceipt> ExecuteModuleGitHubPublishAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        PublishConfiguration publishConfig,
        ModulePackageDetails? packageDetails,
        CancellationToken cancellationToken)
    {
        if (packageDetails is null || packageDetails.ZipAssets.Count == 0)
        {
            return FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ModuleBuild.ToString(), "GitHub release", null, "No packed module assets were found for GitHub publishing.");
        }

        if (string.IsNullOrWhiteSpace(publishConfig.UserName))
        {
            return FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ModuleBuild.ToString(), "GitHub release", null, "GitHub publishing requires UserName.");
        }

        if (string.IsNullOrWhiteSpace(publishConfig.ApiKey))
        {
            return FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ModuleBuild.ToString(), "GitHub release", null, "GitHub publishing is enabled but no token was resolved.");
        }

        var repoName = string.IsNullOrWhiteSpace(publishConfig.RepositoryName) ? repository.Name : publishConfig.RepositoryName!.Trim();
        var tag = new ModulePublishTagBuilder().BuildTag(publishConfig, packageDetails.ModuleName, packageDetails.Version, packageDetails.PreRelease);
        var isPreRelease = !string.IsNullOrWhiteSpace(packageDetails.PreRelease) && !publishConfig.DoNotMarkAsPreRelease;

        var execution = await PublishGitHubReleaseAsync(repository.RootPath, publishConfig.UserName!, repoName, publishConfig.ApiKey!, tag, tag, packageDetails.ZipAssets, publishConfig.GenerateReleaseNotes, isPreRelease, cancellationToken);
        return ReleaseQueueReceiptFactory.CreatePublishReceipt(
            repository.RootPath,
            repository.Name,
            ReleaseBuildAdapterKind.ModuleBuild.ToString(),
            "GitHub release",
            "GitHub",
            execution.ReleaseUrl ?? $"{publishConfig.UserName}/{repoName}",
            execution.Succeeded ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
            execution.Succeeded ? $"GitHub release {tag} published." : execution.ErrorMessage!,
            packageDetails.ZipAssets.FirstOrDefault());
    }
}

public sealed partial class ReleasePublishExecutionService
{
    private async Task<IReadOnlyList<ReleasePublishReceipt>> ExecuteProjectPublishAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        ReleaseSigningExecutionResult signingResult,
        CancellationToken cancellationToken)
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

                if (packages.Count == 0)
                {
                    receipts.Add(FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), "NuGet publish", config.PublishSource, "No signed .nupkg packages were found for publishing."));
                }
                else
                {
                    foreach (var package in packages)
                    {
                        var result = await PublishNugetPackageAsync(package, config.PublishApiKey!, config.PublishSource, cancellationToken);
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
                    }
                }
            }
        }

        if (config.PublishGitHub)
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

        var plan = await GenerateProjectPlanAsync(repository, cancellationToken);
        if (plan is null)
        {
            return [
                FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ProjectBuild.ToString(), "GitHub release", $"{config.GitHubUsername}/{config.GitHubRepositoryName}", "Project release plan could not be generated for GitHub publishing.")
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
