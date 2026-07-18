using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private ModuleBuildResult BindSynchronizedReleasePayload(
        ModulePipelinePlan plan,
        ModuleBuildResult buildResult,
        ModulePipelineRunState state)
    {
        var checkpoint = state.SynchronizedReleaseCheckpoint;
        if (checkpoint is null)
            return buildResult;

        var sourceComponents = CreateSynchronizedReleaseSourceComponents(plan, state);
        var sourceFingerprint = CreateSynchronizedReleaseFingerprint(sourceComponents);
        var cachePath = ResolveSynchronizedReleasePayloadCachePath(state);
        if (string.IsNullOrWhiteSpace(checkpoint.PayloadFingerprint))
        {
            CreateSynchronizedReleasePayloadCache(cachePath, buildResult, state);
            var payloadComponents = CreateCachedSynchronizedReleasePayloadComponents(
                cachePath,
                state);
            checkpoint.SourceFingerprint = sourceFingerprint;
            checkpoint.SourceComponents = sourceComponents;
            checkpoint.PayloadFingerprint = CreateSynchronizedReleaseFingerprint(payloadComponents);
            checkpoint.PayloadComponents = payloadComponents;
            SaveSynchronizedReleaseCheckpoint(state);
            _logger.Info($"Exact coordinated release payload cached at '{cachePath}'.");
            return buildResult;
        }

        if (!string.Equals(
                checkpoint.SourceFingerprint,
                sourceFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            var changedComponents = DescribeChangedSynchronizedReleaseComponents(
                checkpoint.SourceComponents,
                sourceComponents);
            throw new InvalidOperationException(
                $"The coordinated release source differs from the snapshot bound before its first publish attempt ({changedComponents}). Restore the original source or abandon the incomplete release checkpoint explicitly.");
        }

        var cachedPayloadComponents = CreateCachedSynchronizedReleasePayloadComponents(cachePath, state);
        var cachedPayloadFingerprint = CreateSynchronizedReleaseFingerprint(cachedPayloadComponents);
        if (!string.Equals(
                checkpoint.PayloadFingerprint,
                cachedPayloadFingerprint,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The exact signed payload cache for the incomplete coordinated release is missing or corrupt. Restore that cache or abandon the incomplete release checkpoint explicitly.");
        }

        RestoreCachedSynchronizedReleasePayloadPaths(cachePath, state);
        _logger.Info($"Using exact cached payload for the resumed coordinated release from '{cachePath}'.");
        var cachedModulePath = Path.Combine(cachePath, "module");
        return new ModuleBuildResult(
            cachedModulePath,
            Path.Combine(cachedModulePath, Path.GetFileName(buildResult.ManifestPath)),
            buildResult.Exports,
            buildResult.BuildNotes);
    }

    private static string ResolveSynchronizedReleasePayloadCachePath(ModulePipelineRunState state)
    {
        var checkpointPath = state.SynchronizedReleaseCheckpointPath ?? throw new InvalidOperationException(
            "Coordinated release checkpoint path is not initialized.");
        return ResolveSynchronizedReleasePayloadCachePath(checkpointPath);
    }

    private static string ResolveSynchronizedReleasePayloadCachePath(string checkpointPath)
        => Path.Combine(
            Path.GetDirectoryName(checkpointPath) ?? throw new InvalidOperationException(
                $"Coordinated release checkpoint path '{checkpointPath}' has no parent directory."),
            Path.GetFileNameWithoutExtension(checkpointPath) + ".payload");

    private static string[] CreateSynchronizedReleaseSourceComponents(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        var projectRoot = Path.GetFullPath(plan.ProjectRoot);
        var exclusions = ResolveSynchronizedReleaseSourceExclusions(plan, state);
        var components = new List<string>();
        foreach (var entry in Directory.EnumerateFileSystemEntries(projectRoot)
                     .OrderBy(static entry => entry, StringComparer.Ordinal))
        {
            AddSynchronizedReleaseSourceComponent(
                components,
                $"source/{Path.GetFileName(entry)}",
                entry,
                exclusions);
        }

        if (components.Count == 0)
            components.Add("source|empty");
        components.Sort(StringComparer.Ordinal);
        return components.ToArray();
    }

    private static string[] ResolveSynchronizedReleaseSourceExclusions(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        var exclusions = new HashSet<string>(
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal);
        AddSynchronizedReleaseSourceExclusion(exclusions, plan.BuildSpec.StagingPath);
        if (plan.Release?.Configuration is not null)
        {
            AddSynchronizedReleaseSourceExclusion(
                exclusions,
                ResolveReleaseStageRoot(plan, plan.Release.Configuration));
        }
        foreach (var artefact in state.ArtefactResults)
            AddSynchronizedReleaseSourceExclusion(exclusions, artefact.OutputPath);
        foreach (var execution in state.ProjectBuildResults)
        {
            AddSynchronizedReleaseSourceExclusion(exclusions, execution.StagingPath);
            AddSynchronizedReleaseSourceExclusion(exclusions, execution.OutputPath);
            AddSynchronizedReleaseSourceExclusion(exclusions, execution.ReleaseZipOutputPath);
            AddSynchronizedReleaseSourceExclusion(exclusions, execution.PlanOutputPath);
            var release = execution.Result.Release;
            if (release is null)
                continue;
            foreach (var project in release.Projects)
            {
                foreach (var package in project.Packages)
                    AddSynchronizedReleaseSourceExclusion(exclusions, package);
                foreach (var package in project.SymbolPackages)
                    AddSynchronizedReleaseSourceExclusion(exclusions, package);
                AddSynchronizedReleaseSourceExclusion(exclusions, project.ReleaseZipPath);
            }
        }

        return exclusions.ToArray();
    }

    private static void AddSynchronizedReleaseSourceExclusion(
        ISet<string> exclusions,
        string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            exclusions.Add(Path.GetFullPath(path!));
    }

    private static void AddSynchronizedReleaseSourceComponent(
        ICollection<string> components,
        string label,
        string path,
        IReadOnlyCollection<string> exclusions)
    {
        if (ShouldExcludeSynchronizedReleaseSourcePath(path, exclusions))
            return;
        if (File.Exists(path))
        {
            components.Add($"{label}|file|{ComputeSynchronizedReleaseFileHash(path)}");
            return;
        }
        if (!Directory.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            return;
        }

        var records = new List<string>();
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if (ShouldExcludeSynchronizedReleaseSourcePath(entry, exclusions))
                    continue;
                if (File.Exists(entry))
                {
                    var relativePath = FrameworkCompatibility.GetRelativePath(path, entry)
                        .Replace('\\', '/');
                    records.Add($"{relativePath}|{ComputeSynchronizedReleaseFileHash(entry)}");
                }
                else if (Directory.Exists(entry) &&
                         (File.GetAttributes(entry) & FileAttributes.ReparsePoint) == 0)
                {
                    pending.Push(entry);
                }
            }
        }

        if (records.Count == 0)
            return;
        records.Sort(StringComparer.Ordinal);
        components.Add($"{label}|directory|{CreateSynchronizedReleaseFingerprint(records.ToArray())}");
    }

    private static bool ShouldExcludeSynchronizedReleaseSourcePath(
        string path,
        IReadOnlyCollection<string> exclusions)
    {
        var name = Path.GetFileName(path);
        if (Directory.Exists(path) && SynchronizedReleaseSourceExcludedDirectoryNames.Contains(name))
            return true;

        var fullPath = Path.GetFullPath(path);
        foreach (var exclusion in exclusions)
        {
            if (IsSameOrBelowSynchronizedReleaseSourcePath(fullPath, exclusion))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSameOrBelowSynchronizedReleaseSourcePath(string path, string root)
    {
        var comparison = FrameworkCompatibility.PathStringComparison();
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(root);
        if (string.Equals(fullPath, fullRoot, comparison))
            return true;

        var rootPrefix = fullRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(rootPrefix, comparison);
    }

    private static readonly HashSet<string> SynchronizedReleaseSourceExcludedDirectoryNames = new(
        new[]
        {
            ".git", ".vs", ".vscode", "bin", "obj", "packages", "node_modules",
            "Artefacts", "Artifacts", "TestResults"
        },
        StringComparer.OrdinalIgnoreCase);

    private static void CreateSynchronizedReleasePayloadCache(
        string cachePath,
        ModuleBuildResult buildResult,
        ModulePipelineRunState state)
    {
        var temporaryPath = cachePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            CopySynchronizedReleaseDirectory(buildResult.StagingPath, Path.Combine(temporaryPath, "module"));
            for (int index = 0; index < state.ArtefactResults.Count; index++)
            {
                CopySynchronizedReleasePayloadPath(
                    state.ArtefactResults[index].OutputPath,
                    Path.Combine(temporaryPath, "artefact", index.ToString()));
            }
            for (int laneIndex = 0; laneIndex < state.ProjectBuildResults.Count; laneIndex++)
            {
                var release = state.ProjectBuildResults[laneIndex].Result.Release;
                if (release is null)
                    continue;
                for (int projectIndex = 0; projectIndex < release.Projects.Count; projectIndex++)
                {
                    var project = release.Projects[projectIndex];
                    for (int packageIndex = 0; packageIndex < project.Packages.Count; packageIndex++)
                    {
                        CopySynchronizedReleasePayloadPath(
                            project.Packages[packageIndex],
                            ResolveSynchronizedReleasePackageCacheEntry(
                                temporaryPath, laneIndex, projectIndex, "package", packageIndex));
                    }
                    for (int packageIndex = 0; packageIndex < project.SymbolPackages.Count; packageIndex++)
                    {
                        CopySynchronizedReleasePayloadPath(
                            project.SymbolPackages[packageIndex],
                            ResolveSynchronizedReleasePackageCacheEntry(
                                temporaryPath, laneIndex, projectIndex, "symbols", packageIndex));
                    }
                    if (!string.IsNullOrWhiteSpace(project.ReleaseZipPath))
                    {
                        CopySynchronizedReleasePayloadPath(
                            project.ReleaseZipPath!,
                            ResolveSynchronizedReleasePackageCacheEntry(
                                temporaryPath, laneIndex, projectIndex, "release-zip", 0));
                    }
                }
            }

            if (Directory.Exists(cachePath))
                DeleteDirectoryWithRetries(cachePath);
            Directory.Move(temporaryPath, cachePath);
        }
        finally
        {
            if (Directory.Exists(temporaryPath))
                DeleteDirectoryWithRetries(temporaryPath);
        }
    }

    private static void CopySynchronizedReleasePayloadPath(string sourcePath, string cacheEntryPath)
    {
        if (File.Exists(sourcePath))
        {
            var destinationDirectory = Path.Combine(cacheEntryPath, "file");
            Directory.CreateDirectory(destinationDirectory);
            File.Copy(sourcePath, Path.Combine(destinationDirectory, Path.GetFileName(sourcePath)), overwrite: true);
            return;
        }
        if (Directory.Exists(sourcePath))
        {
            CopySynchronizedReleaseDirectory(sourcePath, Path.Combine(cacheEntryPath, "directory"));
            return;
        }

        throw new InvalidOperationException(
            $"Coordinated release payload '{sourcePath}' does not exist and cannot be cached.");
    }

    private static void CopySynchronizedReleaseDirectory(string sourcePath, string destinationPath)
    {
        var pending = new Stack<(string Source, string Destination)>();
        pending.Push((sourcePath, destinationPath));
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            Directory.CreateDirectory(current.Destination);
            foreach (var directory in Directory.EnumerateDirectories(current.Source))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Coordinated release payload directory '{directory}' is a reparse point and cannot be cached safely.");
                }
                pending.Push((directory, Path.Combine(current.Destination, Path.GetFileName(directory))));
            }
            foreach (var file in Directory.EnumerateFiles(current.Source))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Coordinated release payload file '{file}' is a reparse point and cannot be cached safely.");
                }
                File.Copy(file, Path.Combine(current.Destination, Path.GetFileName(file)), overwrite: true);
            }
        }
    }

    private static string[] CreateCachedSynchronizedReleasePayloadComponents(
        string cachePath,
        ModulePipelineRunState state)
    {
        var components = new List<string>();
        AddSynchronizedReleaseExactPayloadPath(components, "module", Path.Combine(cachePath, "module"));
        for (int index = 0; index < state.ArtefactResults.Count; index++)
        {
            var artefact = state.ArtefactResults[index];
            AddSynchronizedReleaseExactPayloadPath(
                components,
                $"artefact/{index}",
                ResolveCachedSynchronizedReleasePayloadPath(
                    Path.Combine(cachePath, "artefact", index.ToString()),
                    Directory.Exists(artefact.OutputPath)));
        }
        for (int laneIndex = 0; laneIndex < state.ProjectBuildResults.Count; laneIndex++)
        {
            var release = state.ProjectBuildResults[laneIndex].Result.Release;
            if (release is null)
                continue;
            for (int projectIndex = 0; projectIndex < release.Projects.Count; projectIndex++)
            {
                var project = release.Projects[projectIndex];
                for (int packageIndex = 0; packageIndex < project.Packages.Count; packageIndex++)
                {
                    AddSynchronizedReleaseExactPayloadPath(
                        components,
                        $"package-lane/{laneIndex}/project/{projectIndex}/package/{packageIndex}",
                        ResolveCachedSynchronizedReleasePayloadPath(
                            ResolveSynchronizedReleasePackageCacheEntry(
                                cachePath, laneIndex, projectIndex, "package", packageIndex),
                            directory: false));
                }
                for (int packageIndex = 0; packageIndex < project.SymbolPackages.Count; packageIndex++)
                {
                    AddSynchronizedReleaseExactPayloadPath(
                        components,
                        $"package-lane/{laneIndex}/project/{projectIndex}/symbols/{packageIndex}",
                        ResolveCachedSynchronizedReleasePayloadPath(
                            ResolveSynchronizedReleasePackageCacheEntry(
                                cachePath, laneIndex, projectIndex, "symbols", packageIndex),
                            directory: false));
                }
                if (!string.IsNullOrWhiteSpace(project.ReleaseZipPath))
                {
                    AddSynchronizedReleaseExactPayloadPath(
                        components,
                        $"package-lane/{laneIndex}/project/{projectIndex}/release-zip",
                        ResolveCachedSynchronizedReleasePayloadPath(
                            ResolveSynchronizedReleasePackageCacheEntry(
                                cachePath, laneIndex, projectIndex, "release-zip", 0),
                            directory: false));
                }
            }
        }

        components.Sort(StringComparer.Ordinal);
        return components.ToArray();
    }

    private static void RestoreCachedSynchronizedReleasePayloadPaths(
        string cachePath,
        ModulePipelineRunState state)
    {
        for (int index = 0; index < state.ArtefactResults.Count; index++)
        {
            var artefact = state.ArtefactResults[index];
            state.ArtefactResults[index] = new ArtefactBuildResult(
                artefact.Type,
                artefact.Id,
                ResolveCachedSynchronizedReleasePayloadPath(
                    Path.Combine(cachePath, "artefact", index.ToString()),
                    Directory.Exists(artefact.OutputPath)),
                artefact.Modules,
                artefact.CopiedItems);
        }
        for (int laneIndex = 0; laneIndex < state.ProjectBuildResults.Count; laneIndex++)
        {
            var release = state.ProjectBuildResults[laneIndex].Result.Release;
            if (release is null)
                continue;
            for (int projectIndex = 0; projectIndex < release.Projects.Count; projectIndex++)
            {
                var project = release.Projects[projectIndex];
                for (int packageIndex = 0; packageIndex < project.Packages.Count; packageIndex++)
                {
                    project.Packages[packageIndex] = ResolveCachedSynchronizedReleasePayloadPath(
                        ResolveSynchronizedReleasePackageCacheEntry(
                            cachePath, laneIndex, projectIndex, "package", packageIndex),
                        directory: false);
                }
                for (int packageIndex = 0; packageIndex < project.SymbolPackages.Count; packageIndex++)
                {
                    project.SymbolPackages[packageIndex] = ResolveCachedSynchronizedReleasePayloadPath(
                        ResolveSynchronizedReleasePackageCacheEntry(
                            cachePath, laneIndex, projectIndex, "symbols", packageIndex),
                        directory: false);
                }
                if (!string.IsNullOrWhiteSpace(project.ReleaseZipPath))
                {
                    project.ReleaseZipPath = ResolveCachedSynchronizedReleasePayloadPath(
                        ResolveSynchronizedReleasePackageCacheEntry(
                            cachePath, laneIndex, projectIndex, "release-zip", 0),
                        directory: false);
                }
            }
        }
        state.ReleaseCoordinationResult = null;
    }

    private static string ResolveSynchronizedReleasePackageCacheEntry(
        string cachePath,
        int laneIndex,
        int projectIndex,
        string kind,
        int itemIndex)
        => Path.Combine(
            cachePath,
            "package-lane",
            laneIndex.ToString(),
            "project",
            projectIndex.ToString(),
            kind,
            itemIndex.ToString());

    private static string ResolveCachedSynchronizedReleasePayloadPath(
        string cacheEntryPath,
        bool directory)
    {
        if (directory)
        {
            var directoryPath = Path.Combine(cacheEntryPath, "directory");
            if (!Directory.Exists(directoryPath))
                throw new InvalidOperationException($"Coordinated release payload cache '{directoryPath}' is missing.");
            return directoryPath;
        }

        var fileDirectory = Path.Combine(cacheEntryPath, "file");
        if (!Directory.Exists(fileDirectory))
            throw new InvalidOperationException($"Coordinated release payload cache '{fileDirectory}' is missing.");
        var files = Directory.GetFiles(fileDirectory);
        if (files.Length != 1)
        {
            throw new InvalidOperationException(
                $"Coordinated release payload cache '{fileDirectory}' must contain exactly one file.");
        }
        return files[0];
    }

    private static void AddSynchronizedReleaseExactPayloadPath(
        ICollection<string> components,
        string label,
        string path)
    {
        if (File.Exists(path))
        {
            components.Add($"{label}|file|{Path.GetFileName(path)}|{CreateSynchronizedReleaseFileFingerprint(path)}");
            return;
        }
        if (!Directory.Exists(path))
            throw new InvalidOperationException($"Coordinated release payload cache '{path}' is missing.");

        var records = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Select(file =>
                $"{FrameworkCompatibility.GetRelativePath(path, file).Replace('\\', '/')}|{CreateSynchronizedReleaseFileFingerprint(file)}")
            .OrderBy(static record => record, StringComparer.Ordinal)
            .ToArray();
        components.Add(records.Length == 0
            ? $"{label}|directory|empty"
            : $"{label}|directory|{CreateSynchronizedReleaseFingerprint(records)}");
    }

    private static string CreateSynchronizedReleaseFileFingerprint(string path)
        => ComputeSynchronizedReleaseFileHash(path);

    private static string ComputeSynchronizedReleaseFileHash(string path)
    {
        using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ComputeSynchronizedReleaseStreamHash(file);
    }

    private static string ComputeSynchronizedReleaseStreamHash(Stream stream)
    {
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static string DescribeChangedSynchronizedReleaseComponents(
        IReadOnlyCollection<string>? expected,
        IReadOnlyCollection<string> actual)
    {
        if (expected is null || expected.Count == 0)
            return "stored source components are unavailable";

        var expectedByLabel = expected.ToDictionary(
            GetSynchronizedReleaseComponentLabel,
            static value => value,
            StringComparer.Ordinal);
        var actualByLabel = actual.ToDictionary(
            GetSynchronizedReleaseComponentLabel,
            static value => value,
            StringComparer.Ordinal);
        var changed = expectedByLabel.Keys
            .Union(actualByLabel.Keys, StringComparer.Ordinal)
            .Where(label =>
                !expectedByLabel.TryGetValue(label, out var expectedValue) ||
                !actualByLabel.TryGetValue(label, out var actualValue) ||
                !string.Equals(expectedValue, actualValue, StringComparison.Ordinal))
            .OrderBy(static label => label, StringComparer.Ordinal)
            .Take(5)
            .ToArray();

        return changed.Length == 0
            ? "source fingerprint changed"
            : $"changed component(s): {string.Join(", ", changed)}";
    }

    private static string GetSynchronizedReleaseComponentLabel(string record)
    {
        var separator = record.IndexOf('|');
        return separator < 0 ? record : record.Substring(0, separator);
    }
}
