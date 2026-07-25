using PowerForge;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Signing;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed partial class ReleasePublishExecutionService
{
    private async Task<IReadOnlyList<ReleasePublishReceipt>> ExecuteModuleOwnedPackagePublishAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        PowerForgeReleaseSpec spec,
        ReleaseSigningExecutionResult signingResult,
        CancellationToken cancellationToken)
    {
        var lanes = ResolveModulePackagePublishLanes(repository.UnifiedReleaseConfigPath!, spec);
        var receipts = new List<ReleasePublishReceipt>();
        foreach (var lane in lanes.Where(static lane => lane.PublishNuget || lane.PublishGitHub))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var publishConfig = lane.Reference is not null
                ? _projectBuildPublishHostService.LoadConfiguration(lane.Reference, lane.ConfigPath)
                : _projectBuildPublishHostService.LoadConfiguration(lane.Inline!, lane.ConfigPath);

            var plan = CreateModulePackagePublishPlan(lane);
            ApplySignedCheckpointArtifacts(plan, signingResult);

            if (publishConfig.PublishNuget)
            {
                var packages = plan.Projects
                    .SelectMany(static project => project.Packages)
                    .Where(File.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (string.IsNullOrWhiteSpace(publishConfig.PublishApiKey))
                {
                    receipts.Add(FailedReceipt(
                        repository.RootPath,
                        repository.Name,
                        "UnifiedRelease",
                        "ModulePackages",
                        publishConfig.PublishSource,
                        $"{lane.Name}: NuGet publishing is enabled but no API key was resolved."));
                }
                else if (packages.Length == 0)
                {
                    receipts.Add(FailedReceipt(
                        repository.RootPath,
                        repository.Name,
                        "UnifiedRelease",
                        "ModulePackages",
                        publishConfig.PublishSource,
                        $"{lane.Name}: no signed checkpointed NuGet packages were found."));
                }
                else
                {
                    foreach (var package in packages)
                    {
                        var publish = await PublishNugetPackageAsync(
                            package,
                            publishConfig.PublishApiKey!,
                            publishConfig.PublishSource,
                            cancellationToken);
                        receipts.Add(ReleaseQueueReceiptFactory.CreatePublishReceipt(
                            repository.RootPath,
                            repository.Name,
                            "UnifiedRelease",
                            Path.GetFileName(package),
                            "ModulePackages",
                            publishConfig.PublishSource,
                            publish.Succeeded ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
                            publish.Succeeded ? "Signed checkpointed package published without rebuilding." : publish.ErrorMessage!,
                            package));
                    }
                }
            }

            if (publishConfig.PublishGitHub)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(publishConfig.GitHubToken))
                {
                    receipts.Add(FailedReceipt(
                        repository.RootPath,
                        repository.Name,
                        "UnifiedRelease",
                        "ModulePackages",
                        null,
                        $"{lane.Name}: GitHub publishing is enabled but no access token was resolved."));
                    continue;
                }

                var publishSummary = await Task.Run(
                        () => _projectBuildPublishHostService.PublishGitHub(publishConfig, plan),
                        cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                receipts.Add(ReleaseQueueReceiptFactory.CreatePublishReceipt(
                    repository.RootPath,
                    repository.Name,
                    "UnifiedRelease",
                    $"{lane.Name} GitHub release",
                    "ModulePackages",
                    publishSummary.SummaryReleaseUrl ?? $"{publishConfig.GitHubUsername}/{publishConfig.GitHubRepositoryName}",
                    publishSummary.Success ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
                    publishSummary.Success
                        ? "Signed checkpointed package release published without rebuilding."
                        : publishSummary.ErrorMessage ?? "Package GitHub publishing failed.",
                    plan.Projects.Select(static project => project.ReleaseZipPath).FirstOrDefault(File.Exists)));
            }
        }

        return receipts;
    }

    private DotNetRepositoryReleaseResult CreateModulePackagePublishPlan(ModulePackagePublishLane lane)
    {
        var request = new ProjectBuildHostRequest {
            ConfigPath = lane.ConfigPath,
            ExecuteBuild = false,
            PlanOnly = true,
            UpdateVersions = false,
            Build = false,
            PublishNuget = false,
            PublishGitHub = false
        };
        var execution = lane.Reference is not null
            ? _projectBuildHostService.Execute(request, lane.Reference, lane.ConfigPath)
            : _projectBuildHostService.Execute(request, lane.Inline!, lane.ConfigPath);
        if (!execution.Success || execution.Result.Release is null)
            throw new InvalidOperationException(
                $"{lane.Name}: package publish plan could not be restored. {execution.ErrorMessage}");

        return execution.Result.Release;
    }

    private static void ApplySignedCheckpointArtifacts(
        DotNetRepositoryReleaseResult plan,
        ReleaseSigningExecutionResult signingResult)
    {
        var files = signingResult.Receipts
            .Where(static receipt =>
                string.Equals(receipt.AdapterKind, ReleaseBuildAdapterKind.ModuleBuild.ToString(), StringComparison.OrdinalIgnoreCase) &&
                receipt.Status is ReleaseSigningReceiptStatus.Signed or ReleaseSigningReceiptStatus.Skipped &&
                File.Exists(receipt.ArtifactPath))
            .Select(static receipt => receipt.ArtifactPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var packages = files
            .Where(static path =>
                path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var symbolPackages = files
            .Where(static path => path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var archives = files
            .Where(static path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var project in plan.Projects)
        {
            var expectedPackageNames = project.Packages
                .Select(Path.GetFileName)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var projectPackages = packages
                .Where(path =>
                    expectedPackageNames.Contains(Path.GetFileName(path)) ||
                    MatchesProjectPackage(path, project))
                .ToArray();
            project.Packages.Clear();
            project.Packages.AddRange(projectPackages);

            var expectedSymbolNames = project.SymbolPackages
                .Select(Path.GetFileName)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            project.SymbolPackages.Clear();
            project.SymbolPackages.AddRange(symbolPackages.Where(path =>
                expectedSymbolNames.Contains(Path.GetFileName(path)) ||
                MatchesProjectPackage(path, project)));

            var expectedZipName = Path.GetFileName(project.ReleaseZipPath);
            project.ReleaseZipPath = archives.FirstOrDefault(path =>
                (!string.IsNullOrWhiteSpace(expectedZipName) &&
                 string.Equals(Path.GetFileName(path), expectedZipName, StringComparison.OrdinalIgnoreCase)) ||
                Path.GetFileName(path).Contains(project.ProjectName, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static bool MatchesProjectPackage(string path, DotNetRepositoryProjectResult project)
    {
        var packageId = string.IsNullOrWhiteSpace(project.PackageId) ? project.ProjectName : project.PackageId;
        if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(project.NewVersion))
            return false;

        var extension = path.EndsWith(".snupkg", StringComparison.OrdinalIgnoreCase)
            ? ".snupkg"
            : ".nupkg";
        var expectedName = $"{packageId}.{project.NewVersion}{extension}";
        return string.Equals(Path.GetFileName(path), expectedName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasConfiguredModulePackagePublication(
        string releaseConfigPath,
        PowerForgeReleaseSpec spec)
        => ResolveModulePackagePublishLanes(releaseConfigPath, spec)
            .Any(static lane => lane.PublishNuget || lane.PublishGitHub);

    private static IReadOnlyList<ModulePackagePublishLane> ResolveModulePackagePublishLanes(
        string releaseConfigPath,
        PowerForgeReleaseSpec spec)
    {
        if (spec.Module?.IncludesPackages != true)
            return [];
        if (string.IsNullOrWhiteSpace(spec.Module.ConfigPath))
        {
            throw new InvalidOperationException(
                "Module-owned package publication requires Module.ConfigPath so the declared JSON package lanes can be resumed.");
        }

        var releaseDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
        var repositoryRoot = string.IsNullOrWhiteSpace(spec.Module.RepositoryRoot)
            ? releaseDirectory
            : PathTokenProtection.GetFullPath(releaseDirectory, spec.Module.RepositoryRoot!);
        var moduleConfigPath = PathTokenProtection.GetFullPath(repositoryRoot, spec.Module.ConfigPath!);
        var context = new ModulePipelineConfigurationService().Load(moduleConfigPath);
        var lanes = new List<ModulePackagePublishLane>();
        foreach (var segment in context.Spec.Segments ?? [])
        {
            switch (segment)
            {
                case ConfigurationProjectBuildSegment project when project.Configuration.Enabled:
                {
                    var configPath = ModulePipelineConfigurationService.ResolveProjectBuildConfigurationPath(
                        context,
                        project.Configuration);
                    var publish = new ProjectBuildSupportService(new NullLogger()).LoadConfig(configPath);
                    lanes.Add(new ModulePackagePublishLane(
                        project.Configuration.Name ?? Path.GetFileNameWithoutExtension(configPath),
                        configPath,
                        project.Configuration,
                        null,
                        project.Configuration.PublishNuget ?? (publish.PublishNuget == true),
                        project.Configuration.PublishGitHub ?? (publish.PublishGitHub == true)));
                    break;
                }
                case ConfigurationPackageBuildSegment package when package.Configuration.Enabled:
                    lanes.Add(new ModulePackagePublishLane(
                        package.Configuration.Name ?? "Inline package build",
                        moduleConfigPath,
                        null,
                        package.Configuration,
                        package.Configuration.PublishNuget == true,
                        package.Configuration.PublishGitHub == true));
                    break;
            }
        }

        return lanes;
    }

    private sealed record ModulePackagePublishLane(
        string Name,
        string ConfigPath,
        ProjectBuildConfigurationReference? Reference,
        PackageBuildConfiguration? Inline,
        bool PublishNuget,
        bool PublishGitHub);
}
