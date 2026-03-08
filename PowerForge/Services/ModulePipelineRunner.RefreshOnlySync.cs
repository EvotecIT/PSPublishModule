using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    internal void SyncRefreshManifestToProjectRoot(
        ModulePipelinePlan plan)
    {
        if (!plan.BuildSpec.RefreshManifestOnly)
            return;

        var projectManifestPath = GetProjectManifestPath(plan);
        if (!File.Exists(projectManifestPath))
            return;

        RefreshProjectManifestFromPlan(plan, projectManifestPath);
        _logger.Info("RefreshPSD1Only: refreshed project-root manifest from source manifest inputs.");
    }

    internal void SyncPublishedManifestToProjectRoot(
        ModulePipelinePlan plan,
        IReadOnlyList<ModulePublishResult>? publishResults)
    {
        if (publishResults is null || publishResults.Count == 0)
            return;

        if (publishResults.Any(r => r is null || !r.Succeeded))
            return;

        var projectManifestPath = GetProjectManifestPath(plan);
        if (!File.Exists(projectManifestPath))
            return;

        RefreshProjectManifestFromPlan(plan, projectManifestPath);

        _logger.Info("Publish: refreshed project-root manifest from source manifest inputs.");
    }

    private static string GetProjectManifestPath(ModulePipelinePlan plan)
    {
        var projectRoot = Path.GetFullPath(plan.ProjectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(projectRoot, $"{plan.ModuleName}.psd1");
    }
}
