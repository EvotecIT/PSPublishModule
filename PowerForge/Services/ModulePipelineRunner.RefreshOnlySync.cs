using System.IO;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void SyncRefreshManifestToProjectRoot(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult)
    {
        if (!plan.BuildSpec.RefreshManifestOnly)
            return;

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
        _logger.Info("RefreshPSD1Only: synchronized 1 manifest file from staging back to project root.");
    }
}
