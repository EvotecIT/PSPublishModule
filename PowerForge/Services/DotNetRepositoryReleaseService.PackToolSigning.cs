using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private static bool TryPreparePackToolPublishOutputs(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec,
        string? packageOutputPath,
        ILogger logger,
        out TimeSpan duration,
        out string error)
    {
        duration = TimeSpan.Zero;
        error = string.Empty;
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var packProperties = CreatePackToolStagingGlobalProperties(packageOutputPath);
        var plans = new List<PackToolPublishPlan>();

        foreach (var project in projects)
        {
            var workingDirectory = Path.GetDirectoryName(project.CsprojPath) ?? string.Empty;
            foreach (var targetFramework in ResolveConfiguredTargetFrameworks(
                         project.CsprojPath,
                         workingDirectory,
                         configuration,
                         project.ProjectName,
                         logger))
            {
                if (!TryReadPackToolProperty(
                        project,
                        workingDirectory,
                        configuration,
                        targetFramework,
                        packProperties,
                        "PackAsTool",
                        logger,
                        ref duration,
                        out var packAsToolValue,
                        out error))
                {
                    return false;
                }

                if (!bool.TryParse(packAsToolValue?.Trim(), out var packAsTool) || !packAsTool)
                    continue;

                if (!TryRejectRidSpecificPackTool(
                        project,
                        workingDirectory,
                        configuration,
                        targetFramework,
                        packProperties,
                        logger,
                        ref duration,
                        out error))
                {
                    return false;
                }

                if (!TryReadPackToolProperty(
                        project,
                        workingDirectory,
                        configuration,
                        targetFramework,
                        packProperties,
                        "PublishDir",
                        logger,
                        ref duration,
                        out var publishDirectory,
                        out error))
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(publishDirectory))
                {
                    error = $"Unable to resolve the pack-tool publish output for {project.ProjectName}.";
                    return false;
                }

                var resolvedPublishDirectory = ResolveProjectPath(workingDirectory, publishDirectory!);
                if (!TryValidatePackToolPublishDirectory(
                        project,
                        workingDirectory,
                        configuration,
                        targetFramework,
                        packProperties,
                        resolvedPublishDirectory,
                        logger,
                        ref duration,
                        out error))
                {
                    return false;
                }

                plans.Add(new PackToolPublishPlan(
                    project,
                    workingDirectory,
                    targetFramework,
                    resolvedPublishDirectory));
            }
        }

        if (!TryValidateDistinctPackToolPublishDirectories(plans, out error))
            return false;

        foreach (var plan in plans)
        {
            try
            {
                if (Directory.Exists(plan.PublishDirectory))
                    Directory.Delete(plan.PublishDirectory, recursive: true);
            }
            catch (Exception ex)
            {
                error = $"Unable to clean the pack-tool publish output for {plan.Project.ProjectName} at {plan.PublishDirectory}. {ex.Message}";
                return false;
            }

            var exitCode = RunDotnetPublishForPackTool(
                plan.Project.CsprojPath,
                plan.WorkingDirectory,
                configuration,
                plan.TargetFramework,
                packProperties,
                plan.Project.ProjectName,
                logger,
                out var stdErr,
                out var stdOut,
                out var publishDuration);
            duration += publishDuration;
            if (exitCode != 0)
            {
                error = $"dotnet publish staging failed for {plan.Project.ProjectName} (exit {exitCode}). {SummarizeProcessFailureOutput(stdErr, stdOut)}".Trim();
                return false;
            }

            if (!Directory.Exists(plan.PublishDirectory))
            {
                error = $"The pack-tool publish output for {plan.Project.ProjectName} was not created at {plan.PublishDirectory}.";
                return false;
            }

            logger.Success($"{plan.Project.ProjectName}: prepared clean pack-tool publish output for assembly signing in {FormatDuration(publishDuration)}.");
        }

        return true;
    }

    private static IReadOnlyDictionary<string, string> CreatePackToolStagingGlobalProperties(string? packageOutputPath = null)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["_IsPacking"] = "true",
            ["NoBuild"] = "true",
            ["BuildProjectReferences"] = "false"
        };
        if (!string.IsNullOrWhiteSpace(packageOutputPath))
            properties["PackageOutputPath"] = Path.GetFullPath(packageOutputPath!);
        return properties;
    }

    private static bool TryReadPackToolProperty(
        DotNetRepositoryProjectResult project,
        string workingDirectory,
        string configuration,
        string? targetFramework,
        IReadOnlyDictionary<string, string> globalProperties,
        string propertyName,
        ILogger logger,
        ref TimeSpan duration,
        out string? value,
        out string error)
    {
        var exitCode = RunDotnetMsBuildGetProperty(
            project.CsprojPath,
            workingDirectory,
            configuration,
            targetFramework,
            null,
            globalProperties,
            propertyName,
            project.ProjectName,
            logger,
            out value,
            out var stdErr,
            out var stdOut,
            out var propertyDuration);
        duration += propertyDuration;
        if (exitCode == 0)
        {
            error = string.Empty;
            return true;
        }

        error = $"Unable to evaluate {propertyName} for {project.ProjectName}. {SummarizeProcessFailureOutput(stdErr, stdOut)}".Trim();
        return false;
    }

    private static bool TryRejectRidSpecificPackTool(
        DotNetRepositoryProjectResult project,
        string workingDirectory,
        string configuration,
        string? targetFramework,
        IReadOnlyDictionary<string, string> globalProperties,
        ILogger logger,
        ref TimeSpan duration,
        out string error)
    {
        foreach (var propertyName in new[] { "RuntimeIdentifier", "CreateRidSpecificToolPackages" })
        {
            if (!TryReadPackToolProperty(
                    project,
                    workingDirectory,
                    configuration,
                    targetFramework,
                    globalProperties,
                    propertyName,
                    logger,
                    ref duration,
                    out var value,
                    out error))
            {
                return false;
            }

            var hasRidSpecificValue = string.Equals(propertyName, "RuntimeIdentifier", StringComparison.Ordinal)
                ? !string.IsNullOrWhiteSpace(value)
                : bool.TryParse(value?.Trim(), out var enabled) && enabled;
            if (!hasRidSpecificValue)
                continue;

            error = $"Dependency assembly signing does not support RID-specific PackAsTool output for {project.ProjectName}; remove the RID-specific tool configuration or disable SignDependencyAssemblies.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidatePackToolPublishDirectory(
        DotNetRepositoryProjectResult project,
        string workingDirectory,
        string configuration,
        string? targetFramework,
        IReadOnlyDictionary<string, string> globalProperties,
        string publishDirectory,
        ILogger logger,
        ref TimeSpan duration,
        out string error)
    {
        var allowedRoots = new List<string>();
        foreach (var propertyName in new[] { "OutputPath", "IntermediateOutputPath", "ArtifactsPath", "PackageOutputPath" })
        {
            if (!TryReadPackToolProperty(
                    project,
                    workingDirectory,
                    configuration,
                    targetFramework,
                    globalProperties,
                    propertyName,
                    logger,
                    ref duration,
                    out var value,
                    out error))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(value))
                allowedRoots.Add(ResolveProjectPath(workingDirectory, value!));
        }

        if (!TryValidateNoReparsePoints(publishDirectory, out error))
        {
            error = $"The pack-tool publish output for {project.ProjectName} cannot be cleaned safely. {error}";
            return false;
        }

        string? containingRoot = null;
        foreach (var allowedRoot in allowedRoots.OrderByDescending(static root => root.Length))
        {
            if (!TryResolveFileSystemPathComparison(allowedRoot, out var comparison, out error))
            {
                error = $"The pack-tool publish output for {project.ProjectName} cannot be cleaned safely. {error}";
                return false;
            }

            if (IsPathStrictlyWithin(publishDirectory, allowedRoot, comparison))
            {
                containingRoot = allowedRoot;
                break;
            }
        }
        if (string.IsNullOrWhiteSpace(containingRoot))
        {
            error = $"The pack-tool publish output for {project.ProjectName} must be contained by its evaluated output, intermediate, artifacts, or package output directory before PowerForge can clean it: {publishDirectory}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateDistinctPackToolPublishDirectories(
        IReadOnlyList<PackToolPublishPlan> plans,
        out string error)
    {
        for (var index = 0; index < plans.Count; index++)
        {
            for (var siblingIndex = index + 1; siblingIndex < plans.Count; siblingIndex++)
            {
                var left = plans[index];
                var right = plans[siblingIndex];
                if (!TryPackToolPathsOverlap(
                        left.PublishDirectory,
                        right.PublishDirectory,
                        out var overlap,
                        out error))
                {
                    return false;
                }

                if (!overlap)
                {
                    continue;
                }

                error = $"Pack-tool publish outputs must not overlap: {left.Project.ProjectName} ({left.PublishDirectory}) and {right.Project.ProjectName} ({right.PublishDirectory}).";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool TryValidateNoReparsePoints(string publishDirectory, out string error)
    {
        try
        {
            var fullPath = Path.GetFullPath(publishDirectory);
            var current = Path.GetPathRoot(fullPath) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(current) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                error = $"The path traverses a linked filesystem root: {current}";
                return false;
            }

            var relativePath = fullPath.Substring(current.Length);
            foreach (var segment in relativePath.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if (!Directory.Exists(current) && !File.Exists(current))
                    continue;
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0)
                    continue;

                error = $"The path traverses a linked file or directory: {current}";
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            error = $"The path could not be inspected: {ex.Message}";
            return false;
        }
    }

    private static bool IsPathStrictlyWithin(string path, string root, StringComparison comparison)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !string.Equals(normalizedPath, normalizedRoot, comparison) &&
               normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static bool IsPathAtOrWithin(string path, string root, StringComparison comparison)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedPath, normalizedRoot, comparison) ||
               normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }

    /// <summary>Determines whether two staging paths overlap using the case semantics of their actual filesystems.</summary>
    internal static bool TryPackToolPathsOverlap(string left, string right, out bool overlap, out string error)
    {
        overlap = false;
        if (!TryResolveFileSystemPathComparison(left, out var leftComparison, out error) ||
            !TryResolveFileSystemPathComparison(right, out var rightComparison, out error))
        {
            return false;
        }

        overlap = IsPathAtOrWithin(left, right, leftComparison) ||
                  IsPathAtOrWithin(right, left, leftComparison) ||
                  IsPathAtOrWithin(left, right, rightComparison) ||
                  IsPathAtOrWithin(right, left, rightComparison);
        error = string.Empty;
        return true;
    }

    private static bool TryResolveFileSystemPathComparison(
        string path,
        out StringComparison comparison,
        out string error)
    {
        comparison = StringComparison.Ordinal;
        string? probePath = null;
        try
        {
            var probeDirectory = Path.GetFullPath(path);
            while (!Directory.Exists(probeDirectory))
            {
                var parent = Directory.GetParent(probeDirectory);
                if (parent is null)
                {
                    error = $"Unable to find an existing directory for filesystem case-sensitivity validation: {path}";
                    return false;
                }

                probeDirectory = parent.FullName;
            }

            var probeName = $".powerforge-case-probe-{Guid.NewGuid():N}-a";
            probePath = Path.Combine(probeDirectory, probeName);
            var alternatePath = Path.Combine(
                probeDirectory,
                probeName.Substring(0, probeName.Length - 1) + "A");
            using (File.Open(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
            {
                comparison = File.Exists(alternatePath)
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;
            }
        }
        catch (Exception ex)
        {
            error = $"Unable to determine filesystem case sensitivity for {path}. {ex.Message}";
            return false;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(probePath))
            {
                try
                {
                    File.Delete(probePath);
                }
                catch
                {
                    // The staging cleanup remains fail-closed through the existence check below.
                }
            }
        }

        if (File.Exists(probePath))
        {
            error = $"Unable to remove the filesystem case-sensitivity probe for {path}: {probePath}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string ResolveProjectPath(string workingDirectory, string value)
    {
        var normalized = value.Trim().Trim('"');
        return Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(workingDirectory, normalized));
    }

    private static int RunDotnetPublishForPackTool(
        string csproj,
        string workingDirectory,
        string configuration,
        string? targetFramework,
        IReadOnlyDictionary<string, string> globalProperties,
        string projectName,
        ILogger logger,
        out string stdErr,
        out string stdOut,
        out TimeSpan duration)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory
        };
        ProcessStartInfoEncoding.TryApplyUtf8(psi);

        var args = new List<string>
        {
            "publish",
            csproj,
            "--configuration",
            configuration,
            "--no-build",
            "--no-restore"
        };
        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            args.Add("--framework");
            args.Add(targetFramework!.Trim());
        }
        foreach (var property in globalProperties)
            args.Add($"-p:{property.Key}={EscapeMsBuildPropertyValue(property.Value)}");

#if NET472
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
#endif

        var exitCode = RunProcessWithHeartbeat(
            psi,
            logger,
            elapsed => $"{projectName}: dotnet publish staging still running ({FormatDuration(elapsed)} elapsed).",
            out stdErr,
            out stdOut,
            out duration);
        LogProcessOutput(logger, projectName, "dotnet publish staging", stdOut, stdErr);
        return exitCode;
    }

    private sealed class PackToolPublishPlan
    {
        internal PackToolPublishPlan(
            DotNetRepositoryProjectResult project,
            string workingDirectory,
            string? targetFramework,
            string publishDirectory)
        {
            Project = project;
            WorkingDirectory = workingDirectory;
            TargetFramework = targetFramework;
            PublishDirectory = publishDirectory;
        }

        internal DotNetRepositoryProjectResult Project { get; }

        internal string WorkingDirectory { get; }

        internal string? TargetFramework { get; }

        internal string PublishDirectory { get; }
    }
}
