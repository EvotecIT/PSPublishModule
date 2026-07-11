using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        out bool removedIntermediatePrimaryOutput,
        out TimeSpan duration,
        out string error)
    {
        var watch = Stopwatch.StartNew();
        removedFileCount = 0;
        removedIntermediatePrimaryOutput = false;
        error = string.Empty;

        try
        {
            var projectDirectory = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
            var primaryFileNames = ResolvePrimaryAssemblyFileNamesForCleanup(project)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var outputRoots = ResolvePrimaryOutputCleanupRoots(project.CsprojPath, projectDirectory, configuration);
            var intermediateRoots = new[] { Path.GetFullPath(Path.Combine(projectDirectory, "obj", configuration)) };
            if (HasConfiguredOutputPathProperties(project.CsprojPath, projectDirectory))
            {
                if (!TryResolveEvaluatedCleanupRoots(
                        project,
                        projectDirectory,
                        configuration,
                        logger,
                        out var evaluatedOutputRoots,
                        out var evaluatedIntermediateRoots,
                        out _,
                        out error))
                {
                    watch.Stop();
                    duration = watch.Elapsed;
                    logger.Error(error);
                    return false;
                }

                outputRoots = outputRoots
                    .Concat(evaluatedOutputRoots)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                // Only an evaluated intermediate root proves that a centrally configured
                // compiler output was invalidated. An abandoned conventional obj tree must
                // not accidentally enable the incremental fast path.
                intermediateRoots = evaluatedIntermediateRoots;
            }

            var searchRoots = outputRoots
                .Concat(intermediateRoots)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var intermediateRootSet = intermediateRoots.ToHashSet(StringComparer.OrdinalIgnoreCase);

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
                    if (intermediateRootSet.Any(root => IsPathWithinRoot(path, root)))
                        removedIntermediatePrimaryOutput = true;
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
            Path.Combine(projectDirectory, "bin", configuration)
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

    private static bool HasConfiguredOutputPathProperties(string csproj, string projectDirectory)
    {
        var paths = new List<string> { csproj };
        for (var directory = new DirectoryInfo(projectDirectory); directory is not null; directory = directory.Parent)
        {
            paths.Add(Path.Combine(directory.FullName, "Directory.Build.props"));
            paths.Add(Path.Combine(directory.FullName, "Directory.Build.targets"));
        }

        var relevantProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ArtifactsPath",
            "BaseIntermediateOutputPath",
            "BaseOutputPath",
            "IntermediateOutputPath",
            "OutDir",
            "OutputPath",
            "UseArtifactsOutput"
        };

        foreach (var path in paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var document = XDocument.Load(path);
                if (document.Descendants().Any(element => relevantProperties.Contains(element.Name.LocalName)))
                    return true;
            }
            catch
            {
                // MSBuild evaluation below remains unnecessary when configuration metadata cannot be inspected.
            }
        }

        return false;
    }

    private static bool TryResolveEvaluatedCleanupRoots(
        DotNetRepositoryProjectResult project,
        string projectDirectory,
        string configuration,
        ILogger logger,
        out string[] outputRoots,
        out string[] intermediateRoots,
        out TimeSpan duration,
        out string error)
    {
        const string propertyNames = "BaseOutputPath,BaseIntermediateOutputPath,OutputPath,OutDir,IntermediateOutputPath,TargetDir";
        var exitCode = RunDotnetMsBuildGetProperty(
            project.CsprojPath,
            projectDirectory,
            configuration,
            null,
            propertyNames,
            project.ProjectName,
            logger,
            out _,
            out var stdErr,
            out var stdOut,
            out duration);
        if (exitCode != 0)
        {
            outputRoots = Array.Empty<string>();
            intermediateRoots = Array.Empty<string>();
            error = $"Could not evaluate configured output paths for {project.ProjectName}. {SummarizeProcessFailureOutput(stdErr, stdOut)}".Trim();
            return false;
        }

        try
        {
            var jsonStart = stdOut.IndexOf('{');
            var jsonEnd = stdOut.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
                throw new InvalidDataException("MSBuild property output did not contain a JSON object.");

            using var document = JsonDocument.Parse(stdOut.Substring(jsonStart, jsonEnd - jsonStart + 1));
            var properties = document.RootElement.GetProperty("Properties");
            outputRoots = ResolveEvaluatedRoots(properties, projectDirectory, "OutputPath", "OutDir", "TargetDir", "BaseOutputPath");
            intermediateRoots = ResolveEvaluatedRoots(properties, projectDirectory, "IntermediateOutputPath", "BaseIntermediateOutputPath");
            if (outputRoots.Length == 0 || intermediateRoots.Length == 0)
            {
                error = $"Configured output paths for {project.ProjectName} could not be resolved to both output and intermediate roots.";
                return false;
            }

            error = string.Empty;
            logger.Verbose($"{project.ProjectName}: evaluated {outputRoots.Length} output and {intermediateRoots.Length} intermediate freshness root(s) in {FormatDuration(duration)}.");
            return true;
        }
        catch (Exception ex)
        {
            outputRoots = Array.Empty<string>();
            intermediateRoots = Array.Empty<string>();
            error = $"Could not parse configured output paths for {project.ProjectName}. {ex.Message}";
            return false;
        }
    }

    private static string[] ResolveEvaluatedRoots(
        JsonElement properties,
        string projectDirectory,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!properties.TryGetProperty(propertyName, out var property))
                continue;

            var value = property.GetString();
            if (string.IsNullOrWhiteSpace(value) || value!.IndexOf("$(", StringComparison.Ordinal) >= 0)
                continue;

            var resolved = Path.IsPathRooted(value)
                ? value
                : Path.Combine(projectDirectory, value);
            return new[] { Path.GetFullPath(resolved) };
        }

        return Array.Empty<string>();
    }

    private static bool IsPathWithinRoot(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
