using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static string[] ResolveBuildOutputDirectories(
        string csproj,
        string workingDirectory,
        string configuration,
        string projectName,
        ILogger logger,
        IReadOnlyList<string>? includePatterns = null)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetFrameworks = ResolveConfiguredTargetFrameworks(csproj, workingDirectory, configuration, projectName, logger);
        // Resolve every framework through MSBuild before returning a signing set.
        // Conventional folders may contain stale assemblies or only a dependency.
        foreach (var targetFramework in targetFrameworks)
        {
            string? directory = null;
            foreach (var propertyName in new[] { "TargetDir", "TargetPath" })
            {
                var exitCode = RunDotnetMsBuildGetProperty(
                    csproj,
                    workingDirectory,
                    configuration,
                    targetFramework,
                    propertyName,
                    projectName,
                    logger,
                    out var value,
                    out var stdErr,
                    out var stdOut,
                    out var duration);

                if (exitCode != 0 || string.IsNullOrWhiteSpace(value))
                {
                    logger.Verbose($"{projectName}: unable to resolve MSBuild {propertyName} for {targetFramework ?? "the default framework"} in {FormatDuration(duration)}. {SummarizeProcessFailureOutput(stdErr, stdOut)}");
                    continue;
                }

                var outputPath = value!.Trim().Trim('"');
                var resolved = Path.IsPathRooted(outputPath)
                    ? Path.GetFullPath(outputPath)
                    : Path.GetFullPath(Path.Combine(workingDirectory, outputPath));
                if (propertyName == "TargetPath")
                {
                    if (!File.Exists(resolved))
                        continue;
                    resolved = Path.GetDirectoryName(resolved)!;
                }

                if (Directory.Exists(resolved) &&
                    (includePatterns is not { Count: > 0 } || ContainsSignableFiles(resolved, includePatterns)))
                {
                    directory = resolved;
                    break;
                }
            }

            if (directory is null)
            {
                var fallback = Path.Combine(workingDirectory, "bin", configuration, targetFramework ?? string.Empty);
                if (Directory.Exists(fallback) &&
                    (includePatterns is not { Count: > 0 } || ContainsSignableFiles(fallback, includePatterns)))
                    directory = Path.GetFullPath(fallback);
            }

            if (directory is null)
                throw new DirectoryNotFoundException($"No matching build output directory found for {projectName} ({targetFramework ?? "default framework"}). Refusing to return partial framework outputs.");
            directories.Add(directory);
        }

        return directories.ToArray();
    }

    private static string[] ResolvePackToolIntermediateAssemblyPaths(
        string csproj,
        string workingDirectory,
        string configuration,
        string projectName,
        ILogger logger,
        IReadOnlyList<string> includePatterns,
        bool includePreparedPublishOutput = false,
        string? packageOutputPath = null)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var packProperties = CreatePackToolStagingGlobalProperties(packageOutputPath);
        foreach (var targetFramework in ResolveConfiguredTargetFrameworks(csproj, workingDirectory, configuration, projectName, logger))
        {
            var packAsToolExitCode = RunDotnetMsBuildGetProperty(
                csproj,
                workingDirectory,
                configuration,
                targetFramework,
                null,
                packProperties,
                "PackAsTool",
                projectName,
                logger,
                out var packAsToolValue,
                out var packAsToolStdErr,
                out var packAsToolStdOut,
                out _);
            if (packAsToolExitCode != 0 && includePreparedPublishOutput)
                throw new InvalidOperationException($"Unable to evaluate PackAsTool for prepared assembly signing of {projectName}. {SummarizeProcessFailureOutput(packAsToolStdErr, packAsToolStdOut)}".Trim());
            if (packAsToolExitCode != 0 || !bool.TryParse(packAsToolValue?.Trim(), out var packAsTool) || !packAsTool)
                continue;

            var intermediateExitCode = RunDotnetMsBuildGetProperty(
                csproj,
                workingDirectory,
                configuration,
                targetFramework,
                null,
                packProperties,
                "IntermediateOutputPath",
                projectName,
                logger,
                out var intermediateOutputPath,
                out var stdErr,
                out var stdOut,
                out var duration);
            if (intermediateExitCode != 0 || string.IsNullOrWhiteSpace(intermediateOutputPath))
            {
                if (includePreparedPublishOutput)
                    throw new InvalidOperationException($"Unable to evaluate IntermediateOutputPath for prepared assembly signing of {projectName}. {SummarizeProcessFailureOutput(stdErr, stdOut)}".Trim());
                logger.Verbose($"{projectName}: unable to resolve the pack-tool intermediate output in {FormatDuration(duration)}. {SummarizeProcessFailureOutput(stdErr, stdOut)}");
                continue;
            }

            var normalizedIntermediateOutputPath = intermediateOutputPath!.Trim().Trim('"');
            var resolvedDirectory = Path.IsPathRooted(normalizedIntermediateOutputPath)
                ? Path.GetFullPath(normalizedIntermediateOutputPath)
                : Path.GetFullPath(Path.Combine(workingDirectory, normalizedIntermediateOutputPath));
            AddMatchingAssemblyPaths(files, resolvedDirectory, includePatterns, SearchOption.TopDirectoryOnly);

            if (!includePreparedPublishOutput)
                continue;

            var publishExitCode = RunDotnetMsBuildGetProperty(
                csproj,
                workingDirectory,
                configuration,
                targetFramework,
                null,
                packProperties,
                "PublishDir",
                projectName,
                logger,
                out var publishDirectory,
                out stdErr,
                out stdOut,
                out duration);
            if (publishExitCode != 0 || string.IsNullOrWhiteSpace(publishDirectory))
            {
                throw new InvalidOperationException($"Unable to evaluate PublishDir for prepared assembly signing of {projectName}. {SummarizeProcessFailureOutput(stdErr, stdOut)}".Trim());
            }

            var normalizedPublishDirectory = publishDirectory!.Trim().Trim('"');
            var resolvedPublishDirectory = Path.IsPathRooted(normalizedPublishDirectory)
                ? Path.GetFullPath(normalizedPublishDirectory)
                : Path.GetFullPath(Path.Combine(workingDirectory, normalizedPublishDirectory));
            if (!Directory.Exists(resolvedPublishDirectory))
                throw new DirectoryNotFoundException($"The prepared pack-tool publish output for {projectName} is missing: {resolvedPublishDirectory}");
            AddMatchingAssemblyPaths(files, resolvedPublishDirectory, includePatterns, SearchOption.AllDirectories);
        }

        return files.ToArray();
    }

    private static void AddMatchingAssemblyPaths(
        HashSet<string> files,
        string directory,
        IReadOnlyList<string> includePatterns,
        SearchOption searchOption)
    {
        if (!Directory.Exists(directory))
            return;

        foreach (var includePattern in includePatterns.Where(static pattern => !string.IsNullOrWhiteSpace(pattern)))
        {
            foreach (var path in Directory.EnumerateFiles(directory, includePattern, searchOption))
                files.Add(Path.GetFullPath(path));
        }
    }

    private static bool ContainsSignableFiles(string directory, IReadOnlyList<string> includePatterns)
    {
        foreach (var includePattern in includePatterns)
        {
            if (string.IsNullOrWhiteSpace(includePattern))
                continue;

            if (Directory.EnumerateFiles(directory, includePattern, SearchOption.TopDirectoryOnly).Any())
                return true;
        }

        return false;
    }

    private static string[] ResolveAssemblySigningIncludePatterns(
        DotNetRepositoryProjectResult project,
        DotNetRepositoryReleaseSpec spec,
        string? workingDirectory = null,
        string? configuration = null,
        ILogger? logger = null)
    {
        if (spec.SignDependencyAssemblies)
            return new[] { "*.dll", "*.exe" };

        var assemblyNames = ResolveAssemblyNames(project.CsprojPath, project.ProjectName, workingDirectory, configuration, logger)
            .Concat(new[] { project.ProjectName })
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return assemblyNames
            .SelectMany(static assemblyName => new[]
            {
                $"{assemblyName}.dll",
                $"{assemblyName}.exe"
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static AssemblySigningPlan BuildAssemblySigningPlan(
        IReadOnlyList<string> outputDirectories,
        IReadOnlyList<string> includePatterns,
        IReadOnlyList<string>? additionalFiles = null,
        bool recursiveBuildOutputs = true)
    {
        var files = new List<string>();
        var outputDirectoryCount = 0;

        foreach (var outputDirectory in outputDirectories)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory) || !Directory.Exists(outputDirectory))
                continue;

            outputDirectoryCount++;
            foreach (var includePattern in includePatterns)
            {
                if (string.IsNullOrWhiteSpace(includePattern))
                    continue;

                // Clean PackAsTool publish inputs supply nested dependencies and runtime assets.
                // Older RID builds beside those inputs must not expand the signing request.
                var searchOption = recursiveBuildOutputs ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                files.AddRange(Directory.EnumerateFiles(outputDirectory, includePattern, searchOption));
            }
        }

        if (additionalFiles is not null)
        {
            foreach (var path in additionalFiles.Where(File.Exists))
                files.Add(path);
        }

        return new AssemblySigningPlan
        {
            IncludePatterns = includePatterns.ToArray(),
            Files = files
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            OutputDirectoryCount = outputDirectoryCount + (additionalFiles ?? Array.Empty<string>())
                .Where(File.Exists)
                .Select(Path.GetDirectoryName)
                .Where(static directory => !string.IsNullOrWhiteSpace(directory))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
        };
    }

    private static bool SamePatterns(IReadOnlyList<string> left, IReadOnlyList<string> right)
        => left.Count == right.Count && left
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .SequenceEqual(right.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

}
