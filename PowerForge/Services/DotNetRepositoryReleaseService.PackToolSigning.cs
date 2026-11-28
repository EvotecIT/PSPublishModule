using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    /// <summary>
    /// Publishes pack-tool projects into the staging folders that <c>dotnet pack</c> reads, so the packages carry
    /// the files that are signed there. A RID-specific tool gets one build and publish per runtime identifier, because
    /// <c>dotnet pack --no-build</c> has no per-runtime output to publish from. With <paramref name="ridSpecificToolsOnly"/>
    /// only those tools are prepared; RID-neutral tools keep packing from their build output.
    /// </summary>
    private static bool TryPreparePackToolPublishOutputs(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        DotNetRepositoryReleaseSpec spec,
        string? packageOutputPath,
        ILogger logger,
        bool ridSpecificToolsOnly,
        out TimeSpan duration,
        out string error,
        out HashSet<DotNetRepositoryProjectResult> onlyPackToolProjects)
    {
        duration = TimeSpan.Zero;
        error = string.Empty;
        onlyPackToolProjects = new HashSet<DotNetRepositoryProjectResult>();
        var configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration.Trim();
        var packProperties = CreatePackToolStagingGlobalProperties(packageOutputPath);
        var plans = new List<PackToolPublishPlan>();

        foreach (var project in projects)
        {
            var runtimeIdentifiers = project.ToolPackageRuntimeIdentifiers;
            if (ridSpecificToolsOnly && runtimeIdentifiers.Count == 0)
                continue;

            var onlyPackToolFrameworks = true;
            var hasPackToolFramework = false;
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
                {
                    onlyPackToolFrameworks = false;
                    continue;
                }

                if (runtimeIdentifiers.Count == 0 &&
                    !TryRejectUnsupportedRidSpecificPackTool(
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

                foreach (var runtimeIdentifier in runtimeIdentifiers.Count == 0 ? new string?[] { null } : runtimeIdentifiers.ToArray<string?>())
                {
                    var planProperties = CreatePackToolRuntimeProperties(packProperties, runtimeIdentifier);
                    if (!TryReadPackToolProperty(
                            project,
                            workingDirectory,
                            configuration,
                            targetFramework,
                            planProperties,
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
                        error = $"Unable to resolve the pack-tool publish output for {project.ProjectName}{FormatRuntimeIdentifierSuffix(runtimeIdentifier)}.";
                        return false;
                    }

                    var resolvedPublishDirectory = ResolveProjectPath(workingDirectory, publishDirectory!);
                    if (!TryValidatePackToolPublishDirectory(
                            project,
                            workingDirectory,
                            configuration,
                            targetFramework,
                            planProperties,
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
                        resolvedPublishDirectory,
                        runtimeIdentifier,
                        planProperties));
                }

                hasPackToolFramework = true;
            }

            if (onlyPackToolFrameworks && hasPackToolFramework)
                onlyPackToolProjects.Add(project);
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

            var target = plan.Project.ProjectName + FormatRuntimeIdentifierSuffix(plan.RuntimeIdentifier);
            if (plan.RuntimeIdentifier is not null)
            {
                // The release build is RID-neutral. Build this runtime's tool assembly without the pack-only
                // NoBuild switch, then publish it with exactly the properties the pack evaluates.
                var buildExitCode = RunDotnetForPackTool(
                    "build",
                    plan.Project.CsprojPath,
                    plan.WorkingDirectory,
                    configuration,
                    plan.TargetFramework,
                    CreatePackToolRuntimeBuildProperties(plan.RuntimeIdentifier),
                    noBuild: false,
                    target,
                    logger,
                    out var buildStdErr,
                    out var buildStdOut,
                    out var buildDuration);
                duration += buildDuration;
                if (buildExitCode != 0)
                {
                    error = $"dotnet build staging failed for {target} (exit {buildExitCode}). {SummarizeProcessFailureOutput(buildStdErr, buildStdOut)}".Trim();
                    return false;
                }
            }

            var exitCode = RunDotnetForPackTool(
                "publish",
                plan.Project.CsprojPath,
                plan.WorkingDirectory,
                configuration,
                plan.TargetFramework,
                plan.GlobalProperties,
                noBuild: true,
                target,
                logger,
                out var stdErr,
                out var stdOut,
                out var publishDuration);
            duration += publishDuration;
            if (exitCode != 0)
            {
                error = $"dotnet publish staging failed for {target} (exit {exitCode}). {SummarizeProcessFailureOutput(stdErr, stdOut)}".Trim();
                return false;
            }

            if (!Directory.Exists(plan.PublishDirectory))
            {
                error = $"The pack-tool publish output for {target} was not created at {plan.PublishDirectory}.";
                return false;
            }

            logger.Success($"{target}: prepared clean pack-tool publish output for assembly signing in {FormatDuration(publishDuration)}.");
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

    private static bool TryRejectUnsupportedRidSpecificPackTool(
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

            // RID-specific tools that list ToolPackageRuntimeIdentifiers (or RuntimeIdentifiers) are prepared per
            // runtime. A tool fixed to one RuntimeIdentifier, or a NativeAOT tool that packs each runtime separately,
            // has no runtime list to prepare.
            error = $"{project.ProjectName} packs a RID-specific tool without a ToolPackageRuntimeIdentifiers or RuntimeIdentifiers list. Pack-tool staging supports RID-neutral tools and tools that list their runtimes.";
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

    private static int RunDotnetForPackTool(
        string verb,
        string csproj,
        string workingDirectory,
        string configuration,
        string? targetFramework,
        IReadOnlyDictionary<string, string> globalProperties,
        bool noBuild,
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
            verb,
            csproj,
            "--configuration",
            configuration,
            "--no-restore"
        };
        if (noBuild)
            args.Add("--no-build");
        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            args.Add("--framework");
            args.Add(targetFramework!.Trim());
        }
        foreach (var property in globalProperties)
            args.Add($"-p:{property.Key}={EscapeMsBuildPropertyValue(property.Value)}");
        args.Add(PackagingPropertyArgument);

#if NET472
        psi.Arguments = BuildWindowsArgumentString(args);
#else
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
#endif

        var exitCode = RunProcessWithHeartbeat(
            psi,
            logger,
            elapsed => $"{projectName}: dotnet {verb} staging still running ({FormatDuration(elapsed)} elapsed).",
            out stdErr,
            out stdOut,
            out duration);
        LogProcessOutput(logger, projectName, $"dotnet {verb} staging", stdOut, stdErr);
        return exitCode;
    }

    private sealed class PackToolPublishPlan
    {
        internal PackToolPublishPlan(
            DotNetRepositoryProjectResult project,
            string workingDirectory,
            string? targetFramework,
            string publishDirectory,
            string? runtimeIdentifier,
            IReadOnlyDictionary<string, string> globalProperties)
        {
            Project = project;
            WorkingDirectory = workingDirectory;
            TargetFramework = targetFramework;
            PublishDirectory = publishDirectory;
            RuntimeIdentifier = runtimeIdentifier;
            GlobalProperties = globalProperties;
        }

        internal DotNetRepositoryProjectResult Project { get; }

        internal string WorkingDirectory { get; }

        internal string? TargetFramework { get; }

        internal string PublishDirectory { get; }

        internal string? RuntimeIdentifier { get; }

        internal IReadOnlyDictionary<string, string> GlobalProperties { get; }
    }
}
