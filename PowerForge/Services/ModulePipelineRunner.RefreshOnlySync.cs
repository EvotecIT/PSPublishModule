using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    internal string? SyncBuildManifestToProjectRoot(
        ModulePipelinePlan plan)
    {
        var projectManifestPath = GetProjectManifestPath(plan);
        if (!File.Exists(projectManifestPath))
            return null;

        RefreshProjectManifestFromPlan(plan, projectManifestPath);

        var label = plan.BuildSpec.RefreshManifestOnly ? "RefreshPSD1Only" : "Build";
        var message = $"{label}: refreshed project-root manifest from source manifest inputs.";
        _logger.Info(message);
        return message;
    }

    internal void SyncRefreshManifestToProjectRoot(
        ModulePipelinePlan plan)
    {
        if (!plan.BuildSpec.RefreshManifestOnly)
            return;

        _ = SyncBuildManifestToProjectRoot(plan);
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
