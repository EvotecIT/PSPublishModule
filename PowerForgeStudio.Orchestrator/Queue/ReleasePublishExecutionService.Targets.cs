using PowerForge;
using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;
using PowerForgeStudio.Domain.Catalog;
using PowerForgeStudio.Orchestrator.Catalog;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed partial class ReleasePublishExecutionService
{
    private IEnumerable<ReleasePublishTarget> ProjectPendingTargets(
        ReleaseQueueItem item,
        ReleaseSigningExecutionResult signingResult)
    {
        var targets = new List<ReleasePublishTarget>();
        var repository = _catalogScanner.InspectRepository(item.RootPath);
        var receipts = signingResult.Receipts ?? [];
        var directModuleConfigFailure = ValidateDirectModuleConfigCheckpoint(repository, signingResult);
        if (!string.IsNullOrWhiteSpace(directModuleConfigFailure))
        {
            return [
                new ReleasePublishTarget(
                    RootPath: item.RootPath,
                    RepositoryName: item.RepositoryName,
                    AdapterKind: ReleaseBuildAdapterKind.ModuleBuild.ToString(),
                    TargetName: "Module build contract",
                    TargetKind: "ConfigurationError",
                    SourcePath: repository.ModuleBuildScriptPath,
                    Destination: directModuleConfigFailure)
            ];
        }

        var unifiedSpec = !string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath)
            ? TryLoadUnifiedReleaseSpec(repository.UnifiedReleaseConfigPath!)
            : null;
        var grouped = receipts.GroupBy(receipt => receipt.AdapterKind, StringComparer.OrdinalIgnoreCase);
        foreach (var group in grouped)
        {
            var adapterKind = group.Key;
            var paths = group.Select(receipt => receipt.ArtifactPath).ToArray();
            var unifiedOwnsModulePackages =
                !string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath) &&
                string.Equals(adapterKind, ReleaseBuildAdapterKind.ModuleBuild.ToString(), StringComparison.OrdinalIgnoreCase);
            if (!unifiedOwnsModulePackages &&
                paths.Any(path => path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)))
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

            if (paths.Any(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) &&
                AllowsAdapterGitHubTarget(
                    repository.UnifiedReleaseConfigPath,
                    unifiedSpec,
                    adapterKind))
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
                group.Any(receipt => string.Equals(receipt.ArtifactKind, "Directory", StringComparison.OrdinalIgnoreCase)) &&
                AllowsModuleRepositoryTarget(
                    repository.UnifiedReleaseConfigPath,
                    unifiedSpec))
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

        foreach (var unifiedTarget in BuildUnifiedPublishTargets(item, signingResult))
        {
            if (!targets.Any(target =>
                    string.Equals(target.TargetKind, unifiedTarget.TargetKind, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(target.SourcePath, unifiedTarget.SourcePath, StringComparison.OrdinalIgnoreCase)))
            {
                targets.Add(unifiedTarget);
            }
        }

        if (!string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath) &&
            targets.Any(static target =>
                !string.Equals(target.TargetKind, "ConfigurationError", StringComparison.OrdinalIgnoreCase)))
        {
            var integrityFailure = ReleaseSigningArtifactIntegrity.Validate(receipts);
            if (!string.IsNullOrWhiteSpace(integrityFailure))
            {
                return [
                    new ReleasePublishTarget(
                        RootPath: item.RootPath,
                        RepositoryName: item.RepositoryName,
                        AdapterKind: "UnifiedRelease",
                        TargetName: "Signed artifact integrity",
                        TargetKind: "ConfigurationError",
                        SourcePath: item.RootPath,
                        Destination: integrityFailure)
                ];
            }
        }

        return targets;
    }

    private string? ValidateDirectModuleConfigCheckpoint(
        RepositoryCatalogEntry repository,
        ReleaseSigningExecutionResult signingResult)
    {
        if (!string.IsNullOrWhiteSpace(repository.UnifiedReleaseConfigPath) ||
            !string.Equals(Path.GetExtension(repository.ModuleBuildScriptPath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var buildResult = _checkpointSerializer.TryDeserialize<ReleaseBuildExecutionResult>(
                signingResult.SourceCheckpointStateJson);
            UnifiedReleaseConfigFingerprint.ValidateModuleConfig(
                repository.ModuleBuildScriptPath!,
                buildResult?.ModuleBuildConfigSha256);
            return null;
        }
        catch (Exception ex)
        {
            return FirstLine(ex.Message) ?? "Module build config checkpoint validation failed.";
        }
    }

    private static PowerForgeReleaseSpec? TryLoadUnifiedReleaseSpec(string configPath)
    {
        try
        {
            return PowerForgeReleaseService.LoadConfiguration(configPath);
        }
        catch
        {
            return null;
        }
    }

    private static bool AllowsAdapterGitHubTarget(
        string? releaseConfigPath,
        PowerForgeReleaseSpec? spec,
        string adapterKind)
    {
        if (string.IsNullOrWhiteSpace(releaseConfigPath))
            return true;
        if (spec is null || spec.GitHub?.Publish == true)
            return false;

        if (string.Equals(adapterKind, ReleaseBuildAdapterKind.ProjectBuild.ToString(), StringComparison.OrdinalIgnoreCase))
            return spec.Packages?.PublishGitHub == true;
        if (!string.Equals(adapterKind, ReleaseBuildAdapterKind.ModuleBuild.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return AllowsModulePublishDestination(
            releaseConfigPath!,
            spec,
            PublishDestination.GitHub);
    }

    private static bool AllowsModuleRepositoryTarget(
        string? releaseConfigPath,
        PowerForgeReleaseSpec? spec)
    {
        if (string.IsNullOrWhiteSpace(releaseConfigPath))
            return true;
        if (spec is null)
            return false;

        return AllowsModulePublishDestination(
            releaseConfigPath!,
            spec,
            PublishDestination.PowerShellGallery);
    }

    private static bool AllowsModulePublishDestination(
        string releaseConfigPath,
        PowerForgeReleaseSpec spec,
        PublishDestination destination)
    {
        try
        {
            return HasEnabledModulePublishDestination(releaseConfigPath, spec, destination);
        }
        catch
        {
            // Preserve the existing fail-closed publish path: a stale or malformed
            // module recipe must produce an explicit failed receipt, not disappear
            // from target projection as though publication were disabled.
            return true;
        }
    }

    private static bool HasEnabledModulePublishDestination(
        string releaseConfigPath,
        PowerForgeReleaseSpec spec,
        PublishDestination destination)
    {
        if (spec.Module is null)
            return false;
        if (string.IsNullOrWhiteSpace(spec.Module.ConfigPath))
            return true;

        var moduleInput = UnifiedReleaseModuleInputResolver.Resolve(releaseConfigPath, spec.Module);
        var context = new ModulePipelineConfigurationService().Load(moduleInput.ConfigPath!);
        return (context.Spec.Segments ?? [])
            .OfType<ConfigurationPublishSegment>()
            .Any(segment =>
                segment.Configuration.Enabled &&
                segment.Configuration.Destination == destination);
    }
}
