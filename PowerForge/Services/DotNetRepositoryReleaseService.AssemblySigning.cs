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
        foreach (var directory in ResolveConventionalBuildOutputDirectories(csproj, workingDirectory, configuration, targetFrameworks))
        {
            if (!Directory.Exists(directory))
                continue;

            if (includePatterns is { Count: > 0 } && !ContainsSignableFiles(directory, includePatterns))
                continue;

            directories.Add(Path.GetFullPath(directory));
        }

        foreach (var targetFramework in targetFrameworks)
        {
            if (directories.Count > 0 && HasOutputDirectoryForTargetFramework(directories, targetFramework))
                continue;

            var exitCode = RunDotnetMsBuildGetProperty(
                csproj,
                workingDirectory,
                configuration,
                targetFramework,
                "TargetDir",
                projectName,
                logger,
                out var value,
                out var stdErr,
                out var stdOut,
                out var duration);

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(value))
            {
                var resolved = Path.GetFullPath(value!.Trim().Trim('"'));
                if (Directory.Exists(resolved))
                    directories.Add(resolved);
            }
            else
            {
                logger.Verbose($"{projectName}: unable to resolve MSBuild TargetDir for signing in {FormatDuration(duration)}. {SummarizeProcessFailureOutput(stdErr, stdOut)}");
            }
        }

        if (directories.Count == 0)
        {
            var fallback = Path.Combine(workingDirectory, "bin", configuration);
            if (Directory.Exists(fallback))
                directories.Add(fallback);
        }

        if (directories.Count == 0)
            throw new DirectoryNotFoundException($"No build output directory found for {projectName}.");

        return directories.ToArray();
    }

    private static bool HasOutputDirectoryForTargetFramework(IEnumerable<string> directories, string? targetFramework)
    {
        if (string.IsNullOrWhiteSpace(targetFramework))
            return directories.Any();

        var normalizedTargetFramework = targetFramework!.Trim();
        return directories.Any(directory =>
        {
            var parts = directory.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Any(part => string.Equals(part, normalizedTargetFramework, StringComparison.OrdinalIgnoreCase));
        });
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
                out _,
                out _,
                out _);
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
                logger.Verbose($"{projectName}: unable to resolve the pack-tool publish output in {FormatDuration(duration)}. {SummarizeProcessFailureOutput(stdErr, stdOut)}");
                continue;
            }

            var normalizedPublishDirectory = publishDirectory!.Trim().Trim('"');
            var resolvedPublishDirectory = Path.IsPathRooted(normalizedPublishDirectory)
                ? Path.GetFullPath(normalizedPublishDirectory)
                : Path.GetFullPath(Path.Combine(workingDirectory, normalizedPublishDirectory));
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

    private static string[] ResolveConventionalBuildOutputDirectories(
        string csproj,
        string workingDirectory,
        string configuration,
        IReadOnlyList<string?> targetFrameworkValues)
    {
        var directories = new List<string>();
        var targetFrameworks = targetFrameworkValues
            .Where(static framework => !string.IsNullOrWhiteSpace(framework))
            .Select(static framework => framework!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var targetFramework in targetFrameworks)
            directories.Add(Path.Combine(workingDirectory, "bin", configuration, targetFramework));

        foreach (var outputPath in ReadOutputPaths(csproj))
        {
            var resolved = Path.IsPathRooted(outputPath)
                ? outputPath
                : Path.GetFullPath(Path.Combine(workingDirectory, outputPath));
            directories.Add(resolved);

            foreach (var targetFramework in targetFrameworks)
                directories.Add(Path.Combine(resolved, targetFramework));
        }

        if (targetFrameworks.Length == 0)
            directories.Add(Path.Combine(workingDirectory, "bin", configuration));

        return directories
            .Where(static directory => !string.IsNullOrWhiteSpace(directory))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
