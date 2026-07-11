using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    /// <summary>
    /// Removes only primary project assemblies that could survive in an obsolete
    /// framework/RID output or intermediate directory. Removing the intermediate
    /// compiler output also forces a normal incremental build to recompile the
    /// package producer before a later no-build pack.
    /// </summary>
    internal static bool TryRemoveStalePrimaryPackageOutputs(
        DotNetRepositoryProjectResult project,
        string configuration,
        ILogger logger,
        out int removedFileCount,
        out TimeSpan duration,
        out string error)
    {
        var watch = Stopwatch.StartNew();
        removedFileCount = 0;
        error = string.Empty;

        try
        {
            var projectDirectory = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
            var primaryFileNames = ResolvePrimaryAssemblyFileNamesForCleanup(project)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var searchRoots = ResolvePrimaryOutputCleanupRoots(project.CsprojPath, projectDirectory, configuration);

            foreach (var searchRoot in searchRoots)
            {
                if (!Directory.Exists(searchRoot))
                    continue;

                foreach (var path in Directory.EnumerateFiles(searchRoot, "*", SearchOption.AllDirectories))
                {
                    if (!primaryFileNames.Contains(Path.GetFileName(path)))
                        continue;

                    File.Delete(path);
                    removedFileCount++;
                }
            }

            watch.Stop();
            duration = watch.Elapsed;
            var message = $"{project.ProjectName}: freshness cleanup removed {removedFileCount} stale primary output(s) from {searchRoots.Length} scoped root(s) in {FormatDuration(duration)}.";
            if (removedFileCount > 0)
                logger.Success(message);
            else
                logger.Info(message);
            return true;
        }
        catch (Exception ex)
        {
            watch.Stop();
            duration = watch.Elapsed;
            error = $"Freshness cleanup failed for {project.ProjectName} after removing {removedFileCount} file(s). {ex.Message}";
            logger.Error(error);
            return false;
        }
    }

    private static string[] ResolvePrimaryAssemblyFileNamesForCleanup(DotNetRepositoryProjectResult project)
    {
        var assemblyNames = new List<string> { project.ProjectName };
        try
        {
            var document = XDocument.Load(project.CsprojPath);
            assemblyNames.AddRange(document.Descendants()
                .Where(static element => string.Equals(element.Name.LocalName, "AssemblyName", StringComparison.OrdinalIgnoreCase))
                .Select(static element => element.Value)
                .Where(IsUsableAssemblyName)
                .Select(static value => value.Trim()));
        }
        catch
        {
            // The project filename remains the SDK default assembly name.
        }

        return assemblyNames
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(static value => new[] { value.Trim() + ".dll", value.Trim() + ".exe" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ResolvePrimaryOutputCleanupRoots(
        string csproj,
        string projectDirectory,
        string configuration)
    {
        var roots = new List<string>
        {
            Path.Combine(projectDirectory, "bin", configuration),
            Path.Combine(projectDirectory, "obj", configuration)
        };

        foreach (var configuredOutputPath in ReadOutputPaths(csproj))
        {
            if (configuredOutputPath.IndexOf("$(", StringComparison.Ordinal) >= 0)
                continue;

            roots.Add(Path.IsPathRooted(configuredOutputPath)
                ? configuredOutputPath
                : Path.Combine(projectDirectory, configuredOutputPath));
        }

        return roots
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
