using PowerForge;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Signing;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed partial class ReleasePublishExecutionService
{
    private async Task<ModulePackageDetails?> ResolveModulePackageDetailsAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        ReleaseSigningExecutionResult signingResult,
        ModulePipelineConfigurationContext? directModuleContext,
        CancellationToken cancellationToken)
    {
        var receipts = signingResult.Receipts
            .Where(receipt => string.Equals(receipt.AdapterKind, ReleaseBuildAdapterKind.ModuleBuild.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var candidateManifests = receipts
            .Where(receipt => receipt.ArtifactPath.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase) && File.Exists(receipt.ArtifactPath))
            .Select(receipt => receipt.ArtifactPath)
            .ToList();
        foreach (var directory in receipts.Where(receipt =>
                     string.Equals(receipt.ArtifactKind, "Directory", StringComparison.OrdinalIgnoreCase)))
        {
            if (!Directory.Exists(directory.ArtifactPath))
                continue;

            candidateManifests.AddRange(
                Directory.EnumerateFiles(directory.ArtifactPath, "*.psd1", SearchOption.AllDirectories)
                    .Where(path => !path.Contains(
                        $"{Path.DirectorySeparatorChar}en-US{Path.DirectorySeparatorChar}",
                        StringComparison.OrdinalIgnoreCase)));
        }

        var manifests = new List<(string Path, ModuleManifestInfo Info)>();
        foreach (var candidateManifest in candidateManifests.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var readInfo = await ReadModuleManifestAsync(
                repository.RootPath,
                candidateManifest,
                cancellationToken);
            if (readInfo is not null)
                manifests.Add((candidateManifest, readInfo));
        }

        if (manifests.Count == 0)
            return null;

        var expected = ReadUnifiedReleaseCheckpoint(signingResult)?.ModulePlan
                       ?? ResolveDirectModulePlan(directModuleContext);
        var selected = expected is null
            ? manifests[0]
            : manifests.FirstOrDefault(manifest => ModuleManifestMatchesPlan(manifest.Info, expected));
        if (string.IsNullOrWhiteSpace(selected.Path))
            return null;

        var packagePath = Path.GetDirectoryName(selected.Path);
        var manifestInfo = selected.Info;
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

    private static bool ModuleManifestMatchesPlan(
        ModuleManifestInfo manifest,
        PowerForgeModuleReleasePlanSummary plan)
    {
        if (!string.IsNullOrWhiteSpace(plan.ModuleName) &&
            !string.Equals(manifest.ModuleName, plan.ModuleName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(plan.ModuleVersion))
            return true;

        var versionsMatch = Version.TryParse(manifest.Version, out var manifestVersion) &&
                            Version.TryParse(plan.ModuleVersion, out var plannedVersion)
            ? manifestVersion.Equals(plannedVersion)
            : string.Equals(
                manifest.Version?.Trim(),
                plan.ModuleVersion?.Trim(),
                StringComparison.OrdinalIgnoreCase);
        return versionsMatch &&
               string.Equals(
                   NormalizeModulePreRelease(manifest.PreRelease),
                   NormalizeModulePreRelease(plan.PreReleaseTag),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static PowerForgeModuleReleasePlanSummary? ResolveDirectModulePlan(
        ModulePipelineConfigurationContext? context)
    {
        if (context is null)
            return null;

        var preRelease = (context.Spec.Segments ?? [])
            .OfType<ConfigurationManifestSegment>()
            .Select(segment => segment.Configuration?.Prerelease)
            .LastOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return new PowerForgeModuleReleasePlanSummary
        {
            ModuleName = context.Spec.Build.Name,
            ModuleVersion = context.EffectiveVersion,
            PreReleaseTag = preRelease
        };
    }

    private static string NormalizeModulePreRelease(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value!.Trim().TrimStart('-');

    private async Task<ModuleManifestInfo?> ReadModuleManifestAsync(
        string repositoryRoot,
        string manifestPath,
        CancellationToken cancellationToken)
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

    private async Task<IReadOnlyList<ReleasePublishReceipt>> ExecuteModulePublishAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        ReleaseSigningExecutionResult signingResult,
        CancellationToken cancellationToken,
        bool suppressGitHub)
    {
        ModulePublishConfigurationSet publishSet;
        try
        {
            publishSet = await ExportModulePublishConfigsAsync(
                repository.RootPath,
                repository.ModuleBuildScriptPath!,
                cancellationToken);
            ValidateModulePublishCheckpoint(repository, signingResult);
            ValidateModuleExportedConfigurationCheckpoint(
                signingResult,
                publishSet);
        }
        catch (Exception ex)
        {
            return [
                FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ModuleBuild.ToString(), "Module publish configuration", null, FirstLine(ex.Message) ?? "Module publish configuration could not be loaded.")
            ];
        }
        if (publishSet.Configurations.Count == 0)
            return [];

        var packageDetails = await ResolveModulePackageDetailsAsync(
            repository,
            signingResult,
            publishSet.Context,
            cancellationToken);
        var receipts = new List<ReleasePublishReceipt>();
        foreach (var publishConfig in publishSet.Configurations.Where(config => config.Enabled))
        {
            if (publishConfig.Destination == PublishDestination.GitHub)
            {
                if (suppressGitHub)
                    continue;
                receipts.Add(await ExecuteModuleGitHubPublishAsync(
                    repository,
                    publishConfig,
                    publishSet.Context,
                    packageDetails,
                    cancellationToken));
                continue;
            }

            receipts.Add(await ExecuteModuleRepositoryPublishAsync(
                repository,
                publishConfig,
                publishSet.Context,
                packageDetails,
                cancellationToken));
        }

        return receipts;
    }

    private async Task<ReleasePublishReceipt> ExecuteModuleRepositoryPublishAsync(
        PowerForgeStudio.Domain.Catalog.RepositoryCatalogEntry repository,
        PublishConfiguration publishConfig,
        ModulePipelineConfigurationContext? moduleContext,
        ModulePackageDetails? packageDetails,
        CancellationToken cancellationToken)
    {
        var destination = ResolveModuleRepositoryName(publishConfig);
        if (packageDetails is null || string.IsNullOrWhiteSpace(packageDetails.PackagePath))
        {
            return FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ModuleBuild.ToString(), "Module publish", destination, "No publishable module package path was captured from the build artefacts.");
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var information = (moduleContext?.Spec.Segments ?? [])
                .OfType<ConfigurationInformationSegment>()
                .Select(segment => segment.Configuration)
                .LastOrDefault(configuration => configuration is not null);
            var delivery = (moduleContext?.Spec.Segments ?? [])
                .OfType<ConfigurationOptionsSegment>()
                .Select(segment => segment.Options?.Delivery)
                .LastOrDefault(configuration => configuration is { Enable: true });
            var publishResult = await _publishCheckpointedModuleAsync(
                new ModuleCheckpointPublishRequest {
                    Publish = publishConfig,
                    ProjectRoot = moduleContext?.ProjectRoot ?? repository.RootPath,
                    ModuleName = packageDetails.ModuleName,
                    ModuleVersion = packageDetails.Version,
                    PreRelease = packageDetails.PreRelease,
                    ModulePath = packageDetails.PackagePath,
                    Information = information,
                    Delivery = delivery
                },
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            return ReleaseQueueReceiptFactory.CreatePublishReceipt(
                repository.RootPath,
                repository.Name,
                ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                packageDetails.ModuleName,
                "PowerShellRepository",
                publishResult.RepositoryName ?? destination,
                ReleasePublishReceiptStatus.Published,
                $"Module published to {publishResult.RepositoryName ?? destination} using {publishResult.Tool}.",
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
        ModulePipelineConfigurationContext? moduleContext,
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

        try
        {
            var apiKey = ModulePublisher.ResolvePublishApiKey(publishConfig, repository.RootPath);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ModuleBuild.ToString(), "GitHub release", null, "GitHub publishing is enabled but no token was resolved.");
            }

            var repoName = string.IsNullOrWhiteSpace(publishConfig.RepositoryName) ? repository.Name : publishConfig.RepositoryName!.Trim();
            var tag = new ModulePublishTagBuilder().BuildTag(publishConfig, packageDetails.ModuleName, packageDetails.Version, packageDetails.PreRelease);
            var isPreRelease = !string.IsNullOrWhiteSpace(packageDetails.PreRelease) && !publishConfig.DoNotMarkAsPreRelease;
            var zipAssets = ResolveModuleGitHubAssets(moduleContext, publishConfig, packageDetails);

            var execution = await PublishGitHubReleaseAsync(repository.RootPath, publishConfig.UserName!, repoName, apiKey, tag, tag, zipAssets, publishConfig.GenerateReleaseNotes, isPreRelease, cancellationToken);
            return ReleaseQueueReceiptFactory.CreatePublishReceipt(
                repository.RootPath,
                repository.Name,
                ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                "GitHub release",
                "GitHub",
                execution.ReleaseUrl ?? $"{publishConfig.UserName}/{repoName}",
                execution.Succeeded ? ReleasePublishReceiptStatus.Published : ReleasePublishReceiptStatus.Failed,
                execution.Succeeded ? $"GitHub release {tag} published." : execution.ErrorMessage!,
                zipAssets.FirstOrDefault());
        }
        catch (Exception ex)
        {
            return FailedReceipt(repository.RootPath, repository.Name, ReleaseBuildAdapterKind.ModuleBuild.ToString(), "GitHub release", null, FirstLine(ex.Message) ?? "GitHub publish secret could not be resolved.");
        }
    }

    private static IReadOnlyList<string> ResolveModuleGitHubAssets(
        ModulePipelineConfigurationContext? context,
        PublishConfiguration publishConfig,
        ModulePackageDetails packageDetails)
    {
        if (context is null)
        {
            if (!string.IsNullOrWhiteSpace(publishConfig.ID))
            {
                throw new InvalidOperationException(
                    $"Module publish artefact ID '{publishConfig.ID}' cannot be resolved from the checkpointed module configuration.");
            }

            return [packageDetails.ZipAssets[0]];
        }

        var packed = (context.Spec.Segments ?? [])
            .OfType<ConfigurationArtefactSegment>()
            .Where(static segment =>
                segment.Configuration.Enabled == true &&
                segment.ArtefactType is ArtefactType.Packed or ArtefactType.ScriptPacked)
            .ToArray();
        var selected = string.IsNullOrWhiteSpace(publishConfig.ID)
            ? packed.Take(1).ToArray()
            : packed.Where(segment =>
                    string.Equals(
                        segment.Configuration.ID,
                        publishConfig.ID!.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
        if (selected.Length == 0)
        {
            var available = packed
                .Select(static segment => segment.Configuration.ID)
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            throw new InvalidOperationException(
                $"No packed artefacts matched ID '{publishConfig.ID}'. Available IDs: {string.Join(", ", available)}");
        }

        var expected = selected
            .Select(segment =>
            {
                var configuredRoot = string.IsNullOrWhiteSpace(segment.Configuration.Path)
                    ? Path.Combine(context.ProjectRoot, "Artefacts", segment.ArtefactType.ToString())
                    : segment.Configuration.Path!;
                var outputRoot = ModulePathTokenFormatter.ReplacePathTokens(
                    configuredRoot,
                    packageDetails.ModuleName,
                    packageDetails.Version,
                    packageDetails.PreRelease);
                var fileName = ArtefactBuilder.ResolveArtefactFileName(
                    segment.Configuration,
                    packageDetails.ModuleName,
                    packageDetails.Version,
                    packageDetails.PreRelease);
                return Path.GetFullPath(Path.Combine(outputRoot, fileName));
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assets = packageDetails.ZipAssets
            .Where(path => expected.Contains(Path.GetFullPath(path)))
            .ToArray();
        if (assets.Length == 0)
        {
            throw new InvalidOperationException(
                $"The checkpoint contains no signed ZIP for module publish artefact ID '{publishConfig.ID ?? "(default)"}'.");
        }

        return assets;
    }

}
