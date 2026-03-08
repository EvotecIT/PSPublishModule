using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    internal void SyncRefreshManifestToProjectRoot(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult)
    {
        if (!plan.BuildSpec.RefreshManifestOnly)
            return;

        SyncManifestToProjectRoot(plan, buildResult, "RefreshPSD1Only");
    }

    internal void SyncPublishedManifestToProjectRoot(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        ManifestEditor.RequiredModule[] manifestRequiredModules,
        string[] manifestExternalModuleDependencies,
        IReadOnlyList<ModulePublishResult>? publishResults)
    {
        if (publishResults is null || publishResults.Count == 0)
            return;

        if (publishResults.Any(r => r is null || !r.Succeeded))
            return;

        var projectManifestPath = Path.Combine(
            Path.GetFullPath(plan.ProjectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            $"{plan.ModuleName}.psd1");

        if (!File.Exists(projectManifestPath))
            return;

        RefreshManifestPathFromPlan(
            plan,
            projectManifestPath,
            manifestRequiredModules,
            manifestExternalModuleDependencies);

        _logger.Info("Publish: refreshed project-root manifest from the resolved manifest plan.");
    }

    private void SyncManifestToProjectRoot(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        string reason)
    {
        var stagingRoot = Path.GetFullPath(buildResult.StagingPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var projectRoot = Path.GetFullPath(plan.ProjectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var modulePsd1 = Path.Combine(stagingRoot, $"{plan.ModuleName}.psd1");
        if (!File.Exists(modulePsd1))
            return;

        var destinationPath = Path.Combine(projectRoot, $"{plan.ModuleName}.psd1");
        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
            Directory.CreateDirectory(destinationDirectory);

        File.Copy(modulePsd1, destinationPath, overwrite: true);
        _logger.Info($"{reason}: synchronized 1 manifest file from staging back to project root.");
    }
}
