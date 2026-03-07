using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void SyncBuildControlledOutputsToProjectRoot(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        FormatterResult[] formattingStagingResults)
    {
        var stagingRoot = Path.GetFullPath(buildResult.StagingPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var projectRoot = Path.GetFullPath(plan.ProjectRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var modulePsd1 = Path.Combine(stagingRoot, $"{plan.ModuleName}.psd1");
        var filesToSync = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            modulePsd1
        };

        if (plan.BuildSpec.RefreshManifestOnly)
        {
            var projectPsd1 = Path.Combine(projectRoot, $"{plan.ModuleName}.psd1");
            var staleGeneratedRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            filesToSync.Add(Path.Combine(stagingRoot, $"{plan.ModuleName}.psm1"));

            foreach (var relativePath in GetManifestControlledRelativePaths(projectPsd1))
                staleGeneratedRelativePaths.Add(relativePath);
            foreach (var relativePath in GetManifestControlledRelativePaths(modulePsd1))
            {
                filesToSync.Add(Path.Combine(stagingRoot, relativePath));
                staleGeneratedRelativePaths.Remove(relativePath);
            }

            foreach (var result in formattingStagingResults ?? Array.Empty<FormatterResult>())
            {
                if (result is null || !result.Changed) continue;
                if (!IsPowerShellSource(result.Path)) continue;
                filesToSync.Add(Path.GetFullPath(result.Path));
            }

            foreach (var relativePath in staleGeneratedRelativePaths)
            {
                if (string.IsNullOrWhiteSpace(relativePath)) continue;
                if (File.Exists(Path.Combine(stagingRoot, relativePath))) continue;

                var destinationPath = Path.Combine(projectRoot, relativePath);
                if (!File.Exists(destinationPath)) continue;

                File.Delete(destinationPath);
            }
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

        _logger.Info($"Project sync: synchronized {synced} build-controlled file(s) from staging back to project root.");

        static string[] GetManifestControlledRelativePaths(string manifestPath)
        {
            if (!File.Exists(manifestPath))
                return Array.Empty<string>();

            if (ManifestEditor.TryGetTopLevelStringArray(manifestPath, "ScriptsToProcess", out var scripts) &&
                scripts is not null)
            {
                return NormalizeRelativePaths(scripts);
            }

            if (ManifestEditor.TryGetTopLevelString(manifestPath, "ScriptsToProcess", out var singleScript) &&
                !string.IsNullOrWhiteSpace(singleScript))
            {
                return NormalizeRelativePaths(new[] { singleScript!.Trim() });
            }

            return Array.Empty<string>();
        }

        static string[] NormalizeRelativePaths(IEnumerable<string> relativePaths)
        {
            return (relativePaths ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .Where(path => !Path.IsPathRooted(path))
                .Select(path => path.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

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
