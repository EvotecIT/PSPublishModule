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
        ValidateModulePublishCheckpoint(repository, signingResult);
        IReadOnlyList<ModulePackageReleaseLane> lanes;
        try
        {
            lanes = await ResolveModuleOwnedPackageLanesAsync(
                repository,
                spec,
                signingResult,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return [
                FailedReceipt(
                    repository.RootPath,
                    repository.Name,
                    "UnifiedRelease",
                    "ModulePackages",
                    null,
                    FirstLine(ex.Message) ?? "Module-owned package configuration could not be restored.")
            ];
        }
        var unified = ReadUnifiedReleaseCheckpoint(signingResult);
        if (unified is null)
        {
            return [
                FailedReceipt(
                    repository.RootPath,
                    repository.Name,
                    "UnifiedRelease",
                    "ModulePackages",
                    null,
                    "Module-owned package publication requires the signed unified build checkpoint.")
            ];
        }

        var receipts = new List<ReleasePublishReceipt>();
        foreach (var lane in lanes.Where(static lane => lane.PublishNuget || lane.PublishGitHub))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateModulePublishCheckpoint(repository, signingResult);
            var publishConfig = lane.Reference is not null
                ? _projectBuildPublishHostService.LoadConfiguration(lane.Reference, lane.ConfigPath)
                : _projectBuildPublishHostService.LoadConfiguration(lane.Inline!, lane.ResolutionConfigPath);

            var plan = ModulePackageReleaseCheckpointService
                .Restore(lane, unified.ModulePackagePlans)
                .Release;
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
                            publishConfig.SkipDuplicate,
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
                        if (!publish.Succeeded && publishConfig.PublishFailFast)
                            break;
                    }
                }
            }

            if (publishConfig.PublishFailFast &&
                receipts.Any(static receipt => receipt.Status == ReleasePublishReceiptStatus.Failed))
            {
                return receipts;
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

    private async Task<IReadOnlyList<ModulePackageReleaseLane>> ResolveModuleOwnedPackageLanesAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        PowerForgeReleaseSpec spec,
        ReleaseSigningExecutionResult signingResult,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(spec.Module?.ConfigPath))
        {
            if (!string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath))
            {
                return ModulePackageReleaseCheckpointService.ResolveLanes(
                    repository.UnifiedReleaseConfigPath!,
                    spec);
            }

            if (string.Equals(
                    Path.GetExtension(repository.ModuleBuildScriptPath),
                    ".json",
                    StringComparison.OrdinalIgnoreCase))
            {
                var context = new ModulePipelineConfigurationService().Load(
                    repository.ModuleBuildScriptPath!);
                return ModulePackageReleaseCheckpointService.ResolveLanes(context);
            }
        }

        if (string.IsNullOrWhiteSpace(spec.Module?.ScriptPath) ||
            string.IsNullOrWhiteSpace(repository.ModuleBuildScriptPath))
        {
            return [];
        }

        var publishSet = await ExportModulePublishConfigsAsync(
            repository.RootPath,
            repository.ModuleBuildScriptPath!,
            cancellationToken).ConfigureAwait(false);
        ValidateModuleExportedConfigurationCheckpoint(signingResult, publishSet);
        return publishSet.Context is null
            ? throw new InvalidOperationException(
                "Script-backed module package configuration could not be loaded from the checkpointed export.")
            : ModulePackageReleaseCheckpointService.ResolveLanes(publishSet.Context);
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
            project.ReleaseZipPath = !string.IsNullOrWhiteSpace(expectedZipName)
                ? archives.FirstOrDefault(path =>
                    string.Equals(Path.GetFileName(path), expectedZipName, StringComparison.OrdinalIgnoreCase))
                : archives.FirstOrDefault(path => MatchesProjectArchive(path, project));
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

    private static bool MatchesProjectArchive(string path, DotNetRepositoryProjectResult project)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var identities = new[] { project.PackageId, project.ProjectName }
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var identity in identities)
        {
            if (string.Equals(fileName, identity, StringComparison.OrdinalIgnoreCase))
                return true;

            if (fileName.StartsWith(identity + ".", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith(identity + "-", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(project.NewVersion) ||
                       fileName.Contains(project.NewVersion!, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static bool HasConfiguredModulePackagePublication(
        string releaseConfigPath,
        PowerForgeReleaseSpec spec)
        => ModulePackageReleaseCheckpointService.ResolveLanes(releaseConfigPath, spec)
            .Any(static lane => lane.PublishNuget || lane.PublishGitHub);

    private PowerForgeModulePackageReleaseCheckpoint[] GetCheckpointedModulePackagePlans(
        ReleaseSigningExecutionResult signingResult)
        => ReadUnifiedReleaseCheckpoint(signingResult)?.ModulePackagePlans ?? [];
}
