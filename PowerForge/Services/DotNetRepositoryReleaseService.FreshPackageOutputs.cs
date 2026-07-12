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
            var outputRoots = ResolvePrimaryOutputCleanupRoots(projectDirectory, configuration);
            var intermediateRoots = new[] { Path.GetFullPath(Path.Combine(projectDirectory, "obj", configuration)) };
            if (RequiresEvaluatedCleanupMetadata(project.CsprojPath, projectDirectory))
            {
                if (!TryResolveEvaluatedCleanupRoots(
                        project,
                        projectDirectory,
                        configuration,
                        logger,
                        out var evaluatedOutputRoots,
                        out var evaluatedIntermediateRoots,
                        out var evaluatedAssemblyNames,
                        out _,
                        out error))
                {
                    watch.Stop();
                    duration = watch.Elapsed;
                    logger.Error(error);
                    return false;
                }

                // Configured projects must use only roots evaluated for the active build.
                // Raw conditional OutputPath values can point at another configuration.
                outputRoots = evaluatedOutputRoots;
                // Only an evaluated intermediate root proves that a centrally configured
                // compiler output was invalidated. An abandoned conventional obj tree must
                // not accidentally enable the incremental fast path.
                intermediateRoots = evaluatedIntermediateRoots;
                foreach (var evaluatedAssemblyName in evaluatedAssemblyNames)
                {
                    primaryFileNames.Add(evaluatedAssemblyName + ".dll");
                    primaryFileNames.Add(evaluatedAssemblyName + ".exe");
                }
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

    private static string[] ResolvePrimaryOutputCleanupRoots(string projectDirectory, string configuration)
    {
        return new[]
        {
            Path.GetFullPath(Path.Combine(projectDirectory, "bin", configuration))
        };
    }

    private static bool RequiresEvaluatedCleanupMetadata(string csproj, string projectDirectory)
    {
        var paths = new List<string> { csproj };
        for (var directory = new DirectoryInfo(projectDirectory); directory is not null; directory = directory.Parent)
        {
            paths.Add(Path.Combine(directory.FullName, "Directory.Build.props"));
            paths.Add(Path.Combine(directory.FullName, "Directory.Build.targets"));
        }

        var outputProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
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
                if (document.Descendants().Any(element => string.Equals(element.Name.LocalName, "Import", StringComparison.OrdinalIgnoreCase)))
                    return true;
                if (document.Descendants().Any(element => outputProperties.Contains(element.Name.LocalName)))
                    return true;

                var assemblyNames = document.Descendants()
                    .Where(element => string.Equals(element.Name.LocalName, "AssemblyName", StringComparison.OrdinalIgnoreCase));
                if (!string.Equals(path, csproj, StringComparison.OrdinalIgnoreCase) && assemblyNames.Any())
                    return true;
                if (assemblyNames.Any(element => element.Value.IndexOf("$(", StringComparison.Ordinal) >= 0))
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
        out string[] evaluatedAssemblyNames,
        out TimeSpan duration,
        out string error)
    {
        const string propertyNames = "AssemblyName,ArtifactsPath,ArtifactsPivots,UseArtifactsOutput,BaseOutputPath,BaseIntermediateOutputPath,OutputPath,OutDir,IntermediateOutputPath,TargetDir";
        var resolvedOutputRoots = new List<string>();
        var resolvedIntermediateRoots = new List<string>();
        var resolvedAssemblyNames = new List<string>();
        duration = TimeSpan.Zero;
        var targetFrameworks = ResolveConfiguredTargetFrameworks(
                project.CsprojPath,
                projectDirectory,
                configuration,
                project.ProjectName,
                logger)
            .Where(framework => string.IsNullOrWhiteSpace(framework) || framework!.IndexOf("$(", StringComparison.Ordinal) < 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targetFrameworks.Length == 0)
            targetFrameworks = new string?[] { null };

        foreach (var targetFramework in targetFrameworks)
        {
            var exitCode = RunDotnetMsBuildGetProperty(
                project.CsprojPath,
                projectDirectory,
                configuration,
                targetFramework,
                propertyNames,
                project.ProjectName,
                logger,
                out _,
                out var stdErr,
                out var stdOut,
                out var evaluationDuration);
            duration += evaluationDuration;
            if (exitCode != 0)
            {
                outputRoots = Array.Empty<string>();
                intermediateRoots = Array.Empty<string>();
                evaluatedAssemblyNames = Array.Empty<string>();
                error = $"Could not evaluate configured output paths for {project.ProjectName}{FormatTargetFrameworkContext(targetFramework)}. {SummarizeProcessFailureOutput(stdErr, stdOut)}".Trim();
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
                var frameworkOutputRoots = ResolveEvaluatedRoots(properties, projectDirectory, "TargetDir", "OutDir", "OutputPath");
                if (frameworkOutputRoots.Length == 0)
                    frameworkOutputRoots = ResolveEvaluatedRoots(properties, projectDirectory, "BaseOutputPath");
                var frameworkIntermediateRoots = ResolveEvaluatedRoots(properties, projectDirectory, "IntermediateOutputPath");
                if (frameworkIntermediateRoots.Length == 0)
                    frameworkIntermediateRoots = ResolveEvaluatedRoots(properties, projectDirectory, "BaseIntermediateOutputPath");

                frameworkOutputRoots = frameworkOutputRoots
                    .Select(root => ResolveConfigurationCleanupRoot(root, targetFramework))
                    .ToArray();
                frameworkIntermediateRoots = frameworkIntermediateRoots
                    .Select(root => ResolveConfigurationCleanupRoot(root, targetFramework))
                    .ToArray();

                var useArtifactsOutput = properties.TryGetProperty("UseArtifactsOutput", out var useArtifactsOutputProperty) &&
                                         bool.TryParse(useArtifactsOutputProperty.GetString(), out var parsedUseArtifactsOutput) &&
                                         parsedUseArtifactsOutput;
                if (useArtifactsOutput)
                {
                    frameworkOutputRoots = ExpandArtifactConfigurationRoots(
                        frameworkOutputRoots,
                        ResolveEvaluatedRoots(properties, projectDirectory, "BaseOutputPath"),
                        configuration);
                    frameworkIntermediateRoots = ExpandArtifactConfigurationRoots(
                        frameworkIntermediateRoots,
                        ResolveEvaluatedRoots(properties, projectDirectory, "BaseIntermediateOutputPath"),
                        configuration);
                }
                var evaluatedAssemblyName = properties.TryGetProperty("AssemblyName", out var assemblyNameProperty)
                    ? assemblyNameProperty.GetString() ?? string.Empty
                    : string.Empty;
                if (!frameworkOutputRoots.Any() || !frameworkIntermediateRoots.Any() || !IsUsableAssemblyName(evaluatedAssemblyName))
                {
                    outputRoots = Array.Empty<string>();
                    intermediateRoots = Array.Empty<string>();
                    evaluatedAssemblyNames = Array.Empty<string>();
                    error = $"Configured build metadata for {project.ProjectName}{FormatTargetFrameworkContext(targetFramework)} could not be resolved to output roots, intermediate roots, and an assembly name.";
                    return false;
                }

                resolvedOutputRoots.AddRange(frameworkOutputRoots);
                resolvedIntermediateRoots.AddRange(frameworkIntermediateRoots);
                resolvedAssemblyNames.Add(evaluatedAssemblyName.Trim());
            }
            catch (Exception ex)
            {
                outputRoots = Array.Empty<string>();
                intermediateRoots = Array.Empty<string>();
                evaluatedAssemblyNames = Array.Empty<string>();
                error = $"Could not parse configured output paths for {project.ProjectName}{FormatTargetFrameworkContext(targetFramework)}. {ex.Message}";
                return false;
            }
        }

        outputRoots = resolvedOutputRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        intermediateRoots = resolvedIntermediateRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        evaluatedAssemblyNames = resolvedAssemblyNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        error = string.Empty;
        logger.Verbose($"{project.ProjectName}: evaluated {outputRoots.Length} output, {intermediateRoots.Length} intermediate, and {evaluatedAssemblyNames.Length} assembly-name freshness value(s) across {targetFrameworks.Length} target framework(s) in {FormatDuration(duration)}.");
        return true;
    }

    private static string[] ResolveEvaluatedRoots(
        JsonElement properties,
        string projectDirectory,
        params string[] propertyNames)
    {
        var roots = new List<string>();
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
            roots.Add(Path.GetFullPath(resolved));
        }

        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsPathWithinRoot(string path, string root)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveConfigurationCleanupRoot(string root, string? targetFramework)
    {
        var resolved = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(targetFramework))
            return resolved;

        var normalized = resolved.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var framework = targetFramework!.Trim();
        var marker = Path.DirectorySeparatorChar + framework + Path.DirectorySeparatorChar;
        var markerIndex = normalized.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex >= 0)
            return normalized.Substring(0, markerIndex);

        var suffix = Path.DirectorySeparatorChar + framework;
        return normalized.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? normalized.Substring(0, normalized.Length - suffix.Length)
            : normalized;
    }

    private static string FormatTargetFrameworkContext(string? targetFramework)
        => string.IsNullOrWhiteSpace(targetFramework) ? string.Empty : $" ({targetFramework})";

    private static string[] ExpandArtifactConfigurationRoots(
        IEnumerable<string> evaluatedRoots,
        IEnumerable<string> artifactBaseRoots,
        string configuration)
    {
        var roots = new List<string>(evaluatedRoots);
        var pivotPrefix = configuration.Trim() + "_";
        foreach (var baseRoot in artifactBaseRoots.Where(Directory.Exists))
        {
            roots.AddRange(Directory.EnumerateDirectories(baseRoot, "*", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var name = Path.GetFileName(path);
                    return string.Equals(name, configuration, StringComparison.OrdinalIgnoreCase) ||
                           name.StartsWith(pivotPrefix, StringComparison.OrdinalIgnoreCase);
                }));
        }

        return roots
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
