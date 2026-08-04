using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private const string SynchronizedReleaseArchiveTransactionSuffix = ".archive.json";

    private void ArchiveSynchronizedReleaseCheckpoint(string path, string reason)
    {
        var temporaryPath = path + ".tmp";
        var payloadCachePath = ResolveSynchronizedReleasePayloadCachePath(path);
        var checkpointDirectory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException(
            $"Coordinated release checkpoint path '{path}' has no parent directory.");
        if (!Directory.Exists(checkpointDirectory) ||
            (File.GetAttributes(checkpointDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"Coordinated release checkpoint directory '{checkpointDirectory}' is not a normal directory.");
        }

        var archiveRoot = Path.Combine(checkpointDirectory, "archive");
        if (Directory.Exists(archiveRoot) &&
            (File.GetAttributes(archiveRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"Coordinated release checkpoint archive '{archiveRoot}' is not a normal directory.");
        }

        Directory.CreateDirectory(archiveRoot);
        var transactionPath = path + SynchronizedReleaseArchiveTransactionSuffix;
        DeleteStaleSynchronizedReleaseArchiveTransactionWrites(transactionPath);
        var transaction = File.Exists(transactionPath)
            ? ReadSynchronizedReleaseArchiveTransaction(transactionPath, archiveRoot)
            : CreateSynchronizedReleaseArchiveTransaction(
                path,
                temporaryPath,
                payloadCachePath,
                transactionPath,
                archiveRoot);
        if (transaction is null)
            return;

        var pendingArchivePath = Path.Combine(archiveRoot, transaction.PendingDirectoryName);
        var finalArchivePath = Path.Combine(archiveRoot, transaction.FinalDirectoryName);
        if (Directory.Exists(finalArchivePath))
        {
            if (Directory.Exists(pendingArchivePath) ||
                HasSynchronizedReleaseArchiveSource(path, temporaryPath, payloadCachePath, transaction))
            {
                throw new InvalidOperationException(
                    $"Coordinated release checkpoint archive transaction '{transactionPath}' has conflicting source and destination state.");
            }
            RequireCompleteSynchronizedReleaseArchive(finalArchivePath, transaction);
            File.Delete(transactionPath);
            _logger.Warn($"Completed interrupted coordinated release checkpoint archival to '{finalArchivePath}' {reason}.");
            return;
        }

        if (!Directory.Exists(pendingArchivePath))
            Directory.CreateDirectory(pendingArchivePath);
        RequireNormalSynchronizedReleaseCacheTree(pendingArchivePath);

        MoveSynchronizedReleaseArchiveDirectory(
            payloadCachePath,
            Path.Combine(pendingArchivePath, "payload"),
            transaction.PayloadKind == "directory");
        MoveSynchronizedReleaseArchiveFile(
            payloadCachePath,
            Path.Combine(pendingArchivePath, "payload.invalid"),
            transaction.PayloadKind == "file");
        MoveSynchronizedReleaseArchiveFile(
            temporaryPath,
            Path.Combine(pendingArchivePath, "checkpoint.json.tmp"),
            transaction.TemporaryCheckpointExists);
        MoveSynchronizedReleaseArchiveFile(
            path,
            Path.Combine(pendingArchivePath, "checkpoint.json"),
            transaction.PrimaryCheckpointExists);

        RequireCompleteSynchronizedReleaseArchive(pendingArchivePath, transaction);
        Directory.Move(pendingArchivePath, finalArchivePath);
        File.Delete(transactionPath);
        _logger.Warn($"Archived incomplete coordinated release checkpoint '{path}' to '{finalArchivePath}' {reason}.");
    }

    private static bool HasSynchronizedReleaseCheckpointArchiveTransaction(string path)
        => File.Exists(path + SynchronizedReleaseArchiveTransactionSuffix);

    private static void DeleteStaleSynchronizedReleaseArchiveTransactionWrites(string transactionPath)
    {
        var directory = Path.GetDirectoryName(transactionPath) ?? throw new InvalidOperationException(
            $"Coordinated release checkpoint archive transaction '{transactionPath}' has no parent directory.");
        var prefix = Path.GetFileName(transactionPath) + ".";
        foreach (var candidate in Directory.EnumerateFiles(directory))
        {
            var name = Path.GetFileName(candidate);
            if (!name.StartsWith(prefix, StringComparison.Ordinal) ||
                !name.EndsWith(".tmp", StringComparison.Ordinal))
            {
                continue;
            }

            RequireNormalSynchronizedReleaseCheckpointFile(candidate);
            File.Delete(candidate);
        }
    }

    private static SynchronizedReleaseArchiveTransaction? CreateSynchronizedReleaseArchiveTransaction(
        string primaryPath,
        string temporaryPath,
        string payloadCachePath,
        string transactionPath,
        string archiveRoot)
    {
        var primaryExists = File.Exists(primaryPath);
        var temporaryExists = File.Exists(temporaryPath);
        if (!primaryExists && !temporaryExists)
            return null;

        if (primaryExists)
            RequireNormalSynchronizedReleaseCheckpointFile(primaryPath);
        if (temporaryExists)
            RequireNormalSynchronizedReleaseCheckpointFile(temporaryPath);

        var payloadKind = "none";
        if (Directory.Exists(payloadCachePath))
        {
            RequireNormalSynchronizedReleaseCacheTree(payloadCachePath);
            payloadKind = "directory";
        }
        else if (File.Exists(payloadCachePath))
        {
            RequireNormalSynchronizedReleaseCheckpointFile(payloadCachePath);
            payloadKind = "file";
        }

        var identity = $"{Path.GetFileNameWithoutExtension(primaryPath)}-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffffffZ}-{Guid.NewGuid():N}";
        var transaction = new SynchronizedReleaseArchiveTransaction
        {
            PendingDirectoryName = ".pending-" + identity,
            FinalDirectoryName = identity,
            PrimaryCheckpointExists = primaryExists,
            TemporaryCheckpointExists = temporaryExists,
            PayloadKind = payloadKind
        };
        ValidateSynchronizedReleaseArchiveTransaction(transaction, archiveRoot);

        var transactionWritePath = transactionPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(transactionWritePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, transaction);
                stream.Flush(flushToDisk: true);
            }
            File.Move(transactionWritePath, transactionPath);
        }
        finally
        {
            if (File.Exists(transactionWritePath))
                File.Delete(transactionWritePath);
        }
        return transaction;
    }

    private static SynchronizedReleaseArchiveTransaction ReadSynchronizedReleaseArchiveTransaction(
        string transactionPath,
        string archiveRoot)
    {
        RequireNormalSynchronizedReleaseCheckpointFile(transactionPath);
        SynchronizedReleaseArchiveTransaction? transaction;
        try
        {
            using var stream = new FileStream(transactionPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            transaction = JsonSerializer.Deserialize<SynchronizedReleaseArchiveTransaction>(stream);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Coordinated release checkpoint archive transaction '{transactionPath}' is invalid.",
                ex);
        }

        if (transaction is null)
            throw new InvalidOperationException(
                $"Coordinated release checkpoint archive transaction '{transactionPath}' is empty.");
        ValidateSynchronizedReleaseArchiveTransaction(transaction, archiveRoot);
        return transaction;
    }

    private static void ValidateSynchronizedReleaseArchiveTransaction(
        SynchronizedReleaseArchiveTransaction transaction,
        string archiveRoot)
    {
        if (!IsSafeSynchronizedReleaseArchiveDirectoryName(transaction.PendingDirectoryName, ".pending-") ||
            !IsSafeSynchronizedReleaseArchiveDirectoryName(transaction.FinalDirectoryName, string.Empty) ||
            !string.Equals(transaction.PendingDirectoryName, ".pending-" + transaction.FinalDirectoryName, StringComparison.Ordinal) ||
            transaction.PayloadKind is not ("none" or "directory" or "file") ||
            (!transaction.PrimaryCheckpointExists && !transaction.TemporaryCheckpointExists))
        {
            throw new InvalidOperationException("The coordinated release checkpoint archive transaction is invalid.");
        }

        _ = Path.GetFullPath(Path.Combine(archiveRoot, transaction.PendingDirectoryName));
        _ = Path.GetFullPath(Path.Combine(archiveRoot, transaction.FinalDirectoryName));
    }

    private static bool IsSafeSynchronizedReleaseArchiveDirectoryName(string? value, string prefix)
        => value is not null &&
           !string.IsNullOrWhiteSpace(value) &&
           value.StartsWith(prefix, StringComparison.Ordinal) &&
           value.Length > prefix.Length &&
           string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal) &&
           value.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) < 0;

    private static bool HasSynchronizedReleaseArchiveSource(
        string primaryPath,
        string temporaryPath,
        string payloadCachePath,
        SynchronizedReleaseArchiveTransaction transaction)
        => transaction.PrimaryCheckpointExists && File.Exists(primaryPath) ||
           transaction.TemporaryCheckpointExists && File.Exists(temporaryPath) ||
           transaction.PayloadKind == "directory" && Directory.Exists(payloadCachePath) ||
           transaction.PayloadKind == "file" && File.Exists(payloadCachePath);

    private static void MoveSynchronizedReleaseArchiveFile(string source, string destination, bool expected)
    {
        if (!expected)
            return;
        if (File.Exists(source))
        {
            RequireNormalSynchronizedReleaseCheckpointFile(source);
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new InvalidOperationException($"Coordinated release archive destination '{destination}' already exists.");
            File.Move(source, destination);
        }
        else if (!File.Exists(destination))
        {
            throw new InvalidOperationException($"Coordinated release archive source '{source}' and destination '{destination}' are both missing.");
        }
    }

    private static void MoveSynchronizedReleaseArchiveDirectory(string source, string destination, bool expected)
    {
        if (!expected)
            return;
        if (Directory.Exists(source))
        {
            RequireNormalSynchronizedReleaseCacheTree(source);
            if (File.Exists(destination) || Directory.Exists(destination))
                throw new InvalidOperationException($"Coordinated release archive destination '{destination}' already exists.");
            Directory.Move(source, destination);
        }
        else if (!Directory.Exists(destination))
        {
            throw new InvalidOperationException($"Coordinated release archive source '{source}' and destination '{destination}' are both missing.");
        }
    }

    private static void RequireCompleteSynchronizedReleaseArchive(
        string archivePath,
        SynchronizedReleaseArchiveTransaction transaction)
    {
        RequireNormalSynchronizedReleaseCacheTree(archivePath);
        var expected = new List<string>();
        if (transaction.PayloadKind == "directory") expected.Add("payload");
        if (transaction.PayloadKind == "file") expected.Add("payload.invalid");
        if (transaction.TemporaryCheckpointExists) expected.Add("checkpoint.json.tmp");
        if (transaction.PrimaryCheckpointExists) expected.Add("checkpoint.json");
        var actual = Directory.EnumerateFileSystemEntries(archivePath)
            .Select(Path.GetFileName)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray();
        expected.Sort(StringComparer.Ordinal);
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
            throw new InvalidOperationException($"Coordinated release checkpoint archive '{archivePath}' is incomplete.");
    }

    private sealed class SynchronizedReleaseArchiveTransaction
    {
        public string PendingDirectoryName { get; set; } = string.Empty;
        public string FinalDirectoryName { get; set; } = string.Empty;
        public bool PrimaryCheckpointExists { get; set; }
        public bool TemporaryCheckpointExists { get; set; }
        public string PayloadKind { get; set; } = "none";
    }

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
