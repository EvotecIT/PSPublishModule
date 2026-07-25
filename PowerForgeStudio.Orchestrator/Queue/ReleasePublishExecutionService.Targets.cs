using PowerForgeStudio.Domain.Publish;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Domain.Signing;

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

        foreach (var unifiedTarget in BuildUnifiedPublishTargets(item, signingResult))
        {
            if (!targets.Any(target =>
                    string.Equals(target.TargetKind, unifiedTarget.TargetKind, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(target.SourcePath, unifiedTarget.SourcePath, StringComparison.OrdinalIgnoreCase)))
            {
                targets.Add(unifiedTarget);
            }
        }

        return targets;
    }
}
