using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void SyncRefreshOnlyOutputsToProjectRoot(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        FormatterResult[] formattingStagingResults)
    {
        if (!plan.BuildSpec.RefreshManifestOnly)
            return;

        var stagingRoot = Path.GetFullPath(buildResult.StagingPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var projectRoot = Path.GetFullPath(plan.ProjectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var modulePsd1 = Path.Combine(stagingRoot, $"{plan.ModuleName}.psd1");
        var modulePsm1 = Path.Combine(stagingRoot, $"{plan.ModuleName}.psm1");

        var filesToSync = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            modulePsd1,
            modulePsm1
        };

        foreach (var result in formattingStagingResults ?? Array.Empty<FormatterResult>())
        {
            if (result is null || !result.Changed) continue;
            if (!IsPowerShellSource(result.Path)) continue;
            filesToSync.Add(Path.GetFullPath(result.Path));
        }

        var sourcePrefix = stagingRoot + Path.DirectorySeparatorChar;
        var synced = 0;

        foreach (var sourcePath in filesToSync)
        {
            if (!File.Exists(sourcePath)) continue;
            if (!sourcePath.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var relative = sourcePath.Substring(sourcePrefix.Length);
            if (string.IsNullOrWhiteSpace(relative)) continue;

            var destinationPath = Path.Combine(projectRoot, relative);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            File.Copy(sourcePath, destinationPath, overwrite: true);
            synced++;
        }

        _logger.Info($"RefreshPSD1Only: synchronized {synced} file(s) from staging back to project root.");

        static bool IsPowerShellSource(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var ext = Path.GetExtension(path);
            return ext.Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".psm1", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".psd1", StringComparison.OrdinalIgnoreCase);
        }
    }
}
