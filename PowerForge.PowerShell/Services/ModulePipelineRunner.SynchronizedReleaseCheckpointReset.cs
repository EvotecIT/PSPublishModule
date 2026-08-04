using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void DeleteResettableSynchronizedReleaseCheckpoint(string path, string reason)
    {
        var payloadCachePath = ResolveSynchronizedReleasePayloadCachePath(path);
        if (Directory.Exists(payloadCachePath))
        {
            if ((File.GetAttributes(payloadCachePath) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(payloadCachePath, recursive: false);
            else
                DeleteDirectoryWithRetries(payloadCachePath);
        }
        else if (File.Exists(payloadCachePath))
            File.Delete(payloadCachePath);

        File.Delete(path);
        File.Delete(path + ".tmp");
        DeleteEmptySynchronizedReleaseCheckpointDirectories(path);
        _logger.Warn($"Discarded unused coordinated release checkpoint '{path}' {reason}.");
    }

    private static bool IsResettableSynchronizedReleaseCheckpoint(
        SynchronizedReleaseCheckpoint checkpoint,
        string checkpointPath)
    {
        var hasNoBoundPayload = checkpoint.SourceFingerprint is not null &&
                                checkpoint.SourceFingerprint.Length == 0 &&
                                checkpoint.SourceComponents is { Length: 0 } &&
                                checkpoint.PayloadFingerprint is not null &&
                                checkpoint.PayloadFingerprint.Length == 0 &&
                                checkpoint.PayloadComponents is { Length: 0 };
        var hasValidBoundPayload = checkpoint.SourceComponents is not null &&
                                   checkpoint.PayloadComponents is not null &&
                                   IsValidSynchronizedReleaseFingerprintState(
                                       checkpoint.SourceFingerprint,
                                       checkpoint.SourceComponents) &&
                                   IsValidSynchronizedReleaseFingerprintState(
                                       checkpoint.PayloadFingerprint,
                                       checkpoint.PayloadComponents) &&
                                   IsExactSynchronizedReleasePayloadCache(
                                       checkpointPath,
                                       checkpoint.PayloadComponents);

        return !string.IsNullOrWhiteSpace(checkpoint.ModuleName) &&
           Enum.IsDefined(typeof(ReleaseVersionSource), checkpoint.ReleaseSource) &&
           (checkpoint.PrimaryProject is null || !string.IsNullOrWhiteSpace(checkpoint.PrimaryProject)) &&
           checkpoint.Version is not null &&
           checkpoint.PlannedOperations is { Length: > 0 } &&
           checkpoint.PlannedOperations.All(IsSynchronizedReleaseFingerprint) &&
           checkpoint.PlannedOperations.Distinct(StringComparer.OrdinalIgnoreCase).Count() == checkpoint.PlannedOperations.Length &&
           checkpoint.AttemptedOperations is { Length: 0 } &&
           checkpoint.CompletedOperations is { Length: 0 } &&
           checkpoint.OperationFingerprints is { Length: > 0 } &&
           checkpoint.OperationFingerprints.All(IsSynchronizedReleaseFingerprint) &&
           (hasNoBoundPayload || hasValidBoundPayload) &&
           checkpoint.PlannedLanes is { Length: > 0 } &&
           checkpoint.PlannedLanes.All(IsSynchronizedReleaseFingerprint) &&
           checkpoint.PlannedLanes.Distinct(StringComparer.OrdinalIgnoreCase).Count() == checkpoint.PlannedLanes.Length &&
           checkpoint.CreatedUtc != default &&
           (checkpoint.Version.Length == 0 ||
            PackageVersionUtility.TryNormalizeExact(checkpoint.Version, out _)) &&
           checkpoint.AttemptedLanes is not null &&
           checkpoint.AttemptedLanes.Distinct(StringComparer.OrdinalIgnoreCase).Count() == checkpoint.AttemptedLanes.Length &&
           checkpoint.AttemptedLanes.All(lane =>
               !string.IsNullOrWhiteSpace(lane) &&
               checkpoint.PlannedLanes!.Contains(lane, StringComparer.OrdinalIgnoreCase)) &&
           checkpoint.Lanes is not null &&
           checkpoint.Lanes.Length == checkpoint.AttemptedLanes.Length &&
           checkpoint.Lanes
               .Select(lane => lane?.CheckpointKey)
               .Where(key => !string.IsNullOrWhiteSpace(key))
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .Count() == checkpoint.Lanes.Length &&
           checkpoint.Lanes.All(lane =>
               lane is not null &&
               Enum.IsDefined(typeof(ReleaseVersionSource), lane.Source) &&
               !string.IsNullOrWhiteSpace(lane.Label) &&
               IsSynchronizedReleaseFingerprint(lane.CheckpointKey) &&
               checkpoint.AttemptedLanes!.Contains(lane.CheckpointKey, StringComparer.OrdinalIgnoreCase) &&
               PackageVersionUtility.TryNormalizeExact(lane.DefaultVersion, out _) &&
               lane.VersionsByProject is not null &&
               lane.VersionsByProject.All(entry =>
                   !string.IsNullOrWhiteSpace(entry.Key) &&
                   PackageVersionUtility.TryNormalizeExact(entry.Value, out _))) &&
           !checkpoint.AuxiliaryRemoteSideEffectsObserved;
    }

    private static bool IsExactSynchronizedReleasePayloadCache(
        string checkpointPath,
        string[] expectedComponents)
    {
        try
        {
            var cachePath = ResolveSynchronizedReleasePayloadCachePath(checkpointPath);
            if (!IsNormalSynchronizedReleaseCacheDirectory(cachePath))
                return false;
            RequireNormalSynchronizedReleaseCacheTree(cachePath);
            var rootEntries = RequireOnlyChildDirectories(cachePath);
            var rootNames = rootEntries.Select(Path.GetFileName).ToArray();
            if (!rootNames.Contains("module", StringComparer.Ordinal) ||
                rootNames.Distinct(StringComparer.Ordinal).Count() != rootNames.Length ||
                rootNames.Any(name => name is not ("module" or "artefact" or "package-lane")))
            {
                return false;
            }

            var components = new List<string>();
            foreach (var identity in expectedComponents.Where(static component =>
                         component.EndsWith("|identity", StringComparison.Ordinal)))
            {
                if (!IsValidSynchronizedReleasePayloadIdentity(identity))
                    return false;
                components.Add(identity);
            }
            AddSynchronizedReleaseExactPayloadPath(
                components,
                "module",
                RequireSynchronizedReleaseCacheDirectory(cachePath, "module"));

            AddCachedSynchronizedReleaseArtefactComponents(cachePath, components);
            AddCachedSynchronizedReleasePackageComponents(cachePath, components);
            components.Sort(StringComparer.Ordinal);

            return expectedComponents.SequenceEqual(components, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private static void AddCachedSynchronizedReleaseArtefactComponents(
        string cachePath,
        ICollection<string> components)
    {
        var artefactRoot = Path.Combine(cachePath, "artefact");
        if (!Directory.Exists(artefactRoot))
            return;
        RequireNormalSynchronizedReleaseCacheTree(artefactRoot);

        var artefactPaths = RequireOnlyChildDirectories(artefactRoot);
        if (artefactPaths.Length == 0)
            throw new InvalidOperationException("The coordinated release artefact cache is empty.");
        foreach (var artefactPath in artefactPaths)
        {
            var cacheKey = Path.GetFileName(artefactPath);
            if (!IsSynchronizedReleaseFingerprint(cacheKey))
                throw new InvalidOperationException("The coordinated release artefact cache key is invalid.");

            AddSynchronizedReleasePayloadIdentity(components, $"artefact/{cacheKey}|identity");
            AddSynchronizedReleaseExactPayloadPath(
                components,
                $"artefact/{cacheKey}/payload",
                ResolveSingleCachedSynchronizedReleasePayloadPath(artefactPath, allowDirectory: true));
        }
    }

    private static void AddCachedSynchronizedReleasePackageComponents(
        string cachePath,
        ICollection<string> components)
    {
        var laneRoot = Path.Combine(cachePath, "package-lane");
        if (!Directory.Exists(laneRoot))
            return;
        RequireNormalSynchronizedReleaseCacheTree(laneRoot);

        var lanePaths = RequireOnlyChildDirectories(laneRoot);
        if (lanePaths.Length == 0)
            throw new InvalidOperationException("The coordinated release package lane cache is empty.");
        foreach (var lanePath in lanePaths)
        {
            var laneKey = Path.GetFileName(lanePath);
            if (!IsSynchronizedReleaseFingerprint(laneKey))
                throw new InvalidOperationException("The coordinated release package lane cache key is invalid.");
            AddSynchronizedReleasePayloadIdentity(components, $"package-lane/{laneKey}|identity");

            var projectRoot = RequireOnlySynchronizedReleaseCacheDirectory(lanePath, "project");
            var projectPaths = RequireOnlyChildDirectories(projectRoot);
            if (projectPaths.Length == 0)
                throw new InvalidOperationException("The coordinated release project cache is empty.");
            foreach (var projectPath in projectPaths)
            {
                var projectKey = Path.GetFileName(projectPath);
                if (!IsSynchronizedReleaseFingerprint(projectKey))
                    throw new InvalidOperationException("The coordinated release project cache key is invalid.");
                AddSynchronizedReleasePayloadIdentity(
                    components,
                    $"package-lane/{laneKey}/project/{projectKey}|identity");

                var kindPaths = RequireOnlyChildDirectories(projectPath);
                if (kindPaths.Length == 0)
                    throw new InvalidOperationException("The coordinated release project payload cache is empty.");
                foreach (var kindPath in kindPaths)
                {
                    var kind = Path.GetFileName(kindPath);
                    if (kind is not ("package" or "symbols" or "release-zip"))
                        throw new InvalidOperationException("The coordinated release package cache kind is invalid.");

                    var payloadPaths = RequireOnlyChildDirectories(kindPath);
                    if (payloadPaths.Length == 0)
                        throw new InvalidOperationException("The coordinated release package payload cache is empty.");
                    foreach (var payloadPath in payloadPaths)
                    {
                        var payloadKey = Path.GetFileName(payloadPath);
                        if (!IsSynchronizedReleaseFingerprint(payloadKey))
                            throw new InvalidOperationException("The coordinated release payload cache key is invalid.");
                        AddSynchronizedReleaseExactPayloadPath(
                            components,
                            $"package-lane/{laneKey}/project/{projectKey}/{kind}/{payloadKey}",
                            ResolveSingleCachedSynchronizedReleasePayloadPath(payloadPath, allowDirectory: false));
                    }
                }
            }
        }
    }

    private static string ResolveSingleCachedSynchronizedReleasePayloadPath(
        string entryPath,
        bool allowDirectory)
    {
        RequireNormalSynchronizedReleaseCacheTree(entryPath);
        var children = Directory.EnumerateFileSystemEntries(entryPath).ToArray();
        if (children.Length != 1)
            throw new InvalidOperationException("A coordinated release cache entry must contain exactly one payload.");

        var container = children[0];
        var containerName = Path.GetFileName(container);
        if (containerName == "directory")
        {
            if (!allowDirectory || !Directory.Exists(container))
                throw new InvalidOperationException("The coordinated release cache entry contains an invalid directory payload.");
            return container;
        }
        if (containerName != "file" || !Directory.Exists(container))
            throw new InvalidOperationException("The coordinated release cache entry contains an invalid file payload.");

        var files = Directory.GetFiles(container);
        if (files.Length != 1 || Directory.GetDirectories(container).Length != 0)
            throw new InvalidOperationException("A coordinated release file cache entry must contain exactly one file.");
        return files[0];
    }

    private static void AddSynchronizedReleasePayloadIdentity(
        ICollection<string> components,
        string identity)
    {
        if (!components.Contains(identity, StringComparer.Ordinal))
            components.Add(identity);
    }

    private static bool IsValidSynchronizedReleasePayloadIdentity(string component)
    {
        var label = component.Substring(0, component.Length - "|identity".Length);
        var parts = label.Split('/');
        return parts.Length switch
        {
            2 => parts[0] is "artefact" or "package-lane" &&
                 IsSynchronizedReleaseFingerprint(parts[1]),
            4 => parts[0] == "package-lane" &&
                 IsSynchronizedReleaseFingerprint(parts[1]) &&
                 parts[2] == "project" &&
                 IsSynchronizedReleaseFingerprint(parts[3]),
            _ => false
        };
    }

    private static string RequireOnlySynchronizedReleaseCacheDirectory(string parentPath, string name)
    {
        var children = Directory.EnumerateFileSystemEntries(parentPath).ToArray();
        var path = Path.Combine(parentPath, name);
        if (children.Length != 1 ||
            !Directory.Exists(path) ||
            !string.Equals(Path.GetFileName(children[0]), name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The coordinated release cache must contain only the '{name}' directory.");
        }
        return path;
    }

    private static string RequireSynchronizedReleaseCacheDirectory(string parentPath, string name)
    {
        var path = Path.Combine(parentPath, name);
        if (!Directory.Exists(path) ||
            !Directory.EnumerateDirectories(parentPath)
                .Any(child => string.Equals(Path.GetFileName(child), name, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"The coordinated release cache directory '{name}' is missing.");
        }
        return path;
    }

    private static string[] RequireOnlyChildDirectories(string path)
    {
        var entries = Directory.EnumerateFileSystemEntries(path).ToArray();
        if (entries.Any(entry => !Directory.Exists(entry)))
            throw new InvalidOperationException("The coordinated release cache contains an unexpected file.");
        return entries;
    }

    private static bool IsNormalSynchronizedReleaseCacheDirectory(string path)
        => Directory.Exists(path) &&
           (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0;

    private static void RequireNormalSynchronizedReleaseCacheTree(string rootPath)
    {
        if (!IsNormalSynchronizedReleaseCacheDirectory(rootPath))
            throw new InvalidOperationException("The coordinated release payload cache is missing or unsafe.");

        foreach (var path in Directory.EnumerateFileSystemEntries(rootPath, "*", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The coordinated release payload cache contains a reparse point.");
        }
    }

    private static bool IsSynchronizedReleaseFingerprint(string? value)
        => value?.Length == 64 && value.All(static character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

    private static bool IsValidSynchronizedReleaseFingerprintState(
        string? fingerprint,
        string[] components)
        => IsSynchronizedReleaseFingerprint(fingerprint) &&
           components.Length > 0 &&
           components.All(static component => !string.IsNullOrWhiteSpace(component)) &&
           string.Equals(
               fingerprint,
               CreateSynchronizedReleaseFingerprint(components),
               StringComparison.OrdinalIgnoreCase);
}
