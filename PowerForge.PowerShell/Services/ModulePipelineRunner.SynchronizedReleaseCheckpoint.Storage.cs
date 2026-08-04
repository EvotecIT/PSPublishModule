using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    internal static string ResolveSynchronizedReleaseCheckpointPath(ModulePipelinePlan plan)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safeModuleName = new string(plan.ModuleName
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        var projectIdentity = CreateSynchronizedReleaseProjectIdentity(plan.ProjectRoot);
        return Path.Combine(
            ResolveSynchronizedReleaseStateRoot(plan.ProjectRoot),
            "coordinated-release",
            $"{safeModuleName}-{projectIdentity}.json");
    }

    internal static string ResolveSynchronizedReleaseStateRoot(string projectRoot)
    {
        var current = new DirectoryInfo(Path.GetFullPath(projectRoot));
        while (current is not null)
        {
            var gitMarker = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(gitMarker))
                return Path.Combine(gitMarker, "powerforge");

            if (File.Exists(gitMarker))
            {
                var marker = File.ReadLines(gitMarker).FirstOrDefault();
                const string prefix = "gitdir:";
                if (!string.IsNullOrWhiteSpace(marker) && marker.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    var gitDirectory = marker.Substring(prefix.Length).Trim();
                    if (!Path.IsPathRooted(gitDirectory))
                        gitDirectory = Path.GetFullPath(Path.Combine(current.FullName, gitDirectory));
                    return Path.Combine(gitDirectory, "powerforge");
                }
            }

            current = current.Parent;
        }

        var localStateRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localStateRoot))
            localStateRoot = Path.GetTempPath();

        return Path.Combine(
            localStateRoot,
            "PowerForge",
            "coordinated-release-projects",
            CreateSynchronizedReleaseProjectIdentity(projectRoot));
    }

    private static string CreateSynchronizedReleaseProjectIdentity(string projectRoot)
    {
        var canonicalProjectRoot = Path.GetFullPath(projectRoot);
        var filesystemRoot = Path.GetPathRoot(canonicalProjectRoot);
        if (!string.Equals(
                canonicalProjectRoot,
                filesystemRoot,
                FrameworkCompatibility.PathStringComparison()))
        {
            canonicalProjectRoot = canonicalProjectRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
        }
        if (Path.DirectorySeparatorChar == '\\')
            canonicalProjectRoot = canonicalProjectRoot.ToUpperInvariant();

        return CreateSynchronizedReleaseFingerprint("ProjectRoot", canonicalProjectRoot);
    }

    private void EnterSynchronizedReleaseCheckpointScope(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        state.PlannedSynchronizedOperationCount = ResolvePlannedSynchronizedPublishOperationKeys(plan).Length;
        var checkpointPath = ResolveSynchronizedReleaseCheckpointPath(plan);
        if (state.PlannedSynchronizedOperationCount == 0 && !HasSynchronizedReleaseCheckpoint(checkpointPath))
            return;

        var lockPath = checkpointPath + ".lock";
        var directory = Path.GetDirectoryName(lockPath) ?? throw new InvalidOperationException(
            $"Coordinated release lock path '{lockPath}' has no parent directory.");
        Directory.CreateDirectory(directory);
        try
        {
            state.SynchronizedReleaseCheckpointLock = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException(
                $"Another coordinated release is already active for module '{plan.ModuleName}' and project '{plan.ProjectRoot}'.",
                ex);
        }
    }

    private static void ExitSynchronizedReleaseCheckpointScope(ModulePipelineRunState state)
    {
        state.SynchronizedReleaseCheckpointLock?.Dispose();
        state.SynchronizedReleaseCheckpointLock = null;
    }

    private static void SaveSynchronizedReleaseCheckpoint(ModulePipelineRunState state)
    {
        var checkpoint = state.SynchronizedReleaseCheckpoint ?? throw new InvalidOperationException(
            "Coordinated release checkpoint is not initialized.");
        var path = state.SynchronizedReleaseCheckpointPath ?? throw new InvalidOperationException(
            "Coordinated release checkpoint path is not initialized.");
        var directory = Path.GetDirectoryName(path) ?? throw new InvalidOperationException(
            $"Coordinated release checkpoint path '{path}' has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        WriteSynchronizedReleaseCheckpointFile(
            temporaryPath,
            JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true }));
        PromoteSynchronizedReleaseCheckpointFile(temporaryPath, path);
    }

    private static bool HasSynchronizedReleaseCheckpoint(string path)
        => File.Exists(path) || File.Exists(path + ".tmp");

    private static SynchronizedReleaseCheckpointRead ReadSynchronizedReleaseCheckpoint(string path)
    {
        var temporaryPath = path + ".tmp";
        var primaryExists = File.Exists(path);
        var temporaryExists = File.Exists(temporaryPath);
        if (!primaryExists && !temporaryExists)
        {
            throw new InvalidOperationException(
                $"Coordinated release checkpoint '{path}' disappeared while the release lock was being acquired.");
        }

        var primary = primaryExists ? DeserializeSynchronizedReleaseCheckpoint(path) : null;
        if (!temporaryExists)
            return new SynchronizedReleaseCheckpointRead(primary!, temporaryPath: null);

        var temporary = DeserializeSynchronizedReleaseCheckpoint(temporaryPath);
        if (primary is not null && !IsMonotonicSynchronizedReleaseCheckpointSuccessor(primary, temporary))
        {
            throw new InvalidOperationException(
                $"Temporary coordinated release checkpoint '{temporaryPath}' cannot be proven to be a newer state of '{path}'. Preserve both files and inspect the release state before retrying.");
        }

        return new SynchronizedReleaseCheckpointRead(temporary, temporaryPath);
    }

    private static SynchronizedReleaseCheckpoint DeserializeSynchronizedReleaseCheckpoint(string path)
    {
        try
        {
            RequireNormalSynchronizedReleaseCheckpointFile(path);
            var checkpoint = JsonSerializer.Deserialize<SynchronizedReleaseCheckpoint>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (checkpoint is null || checkpoint.SchemaVersion != 5)
            {
                throw new InvalidOperationException(
                    $"Coordinated release checkpoint '{path}' has an unsupported schema.");
            }
            return checkpoint;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException(
                $"Coordinated release checkpoint '{path}' could not be read. Preserve it and inspect the incomplete release state before retrying. {ex.Message}",
                ex);
        }
    }

    private static bool IsMonotonicSynchronizedReleaseCheckpointSuccessor(
        SynchronizedReleaseCheckpoint previous,
        SynchronizedReleaseCheckpoint candidate)
    {
        if (previous.PlannedOperations is null || candidate.PlannedOperations is null ||
            previous.AttemptedOperations is null || candidate.AttemptedOperations is null ||
            previous.CompletedOperations is null || candidate.CompletedOperations is null ||
            previous.OperationFingerprints is null || candidate.OperationFingerprints is null ||
            previous.SourceComponents is null || candidate.SourceComponents is null ||
            previous.PayloadComponents is null || candidate.PayloadComponents is null ||
            previous.PlannedLanes is null || candidate.PlannedLanes is null ||
            previous.AttemptedLanes is null || candidate.AttemptedLanes is null ||
            previous.Lanes is null || candidate.Lanes is null)
        {
            return false;
        }

        var sameIdentity = previous.SchemaVersion == candidate.SchemaVersion &&
                           previous.SchemaVersion == 5 &&
                           string.Equals(previous.ModuleName, candidate.ModuleName, StringComparison.OrdinalIgnoreCase) &&
                           previous.ReleaseSource == candidate.ReleaseSource &&
                           string.Equals(previous.PrimaryProject, candidate.PrimaryProject, StringComparison.OrdinalIgnoreCase) &&
                           previous.CreatedUtc == candidate.CreatedUtc &&
                           SetsEqual(previous.PlannedOperations, candidate.PlannedOperations) &&
                           SetsEqual(previous.OperationFingerprints, candidate.OperationFingerprints) &&
                           SetsEqual(previous.PlannedLanes, candidate.PlannedLanes);
        if (!sameIdentity ||
            !CanAdvanceSynchronizedReleaseVersion(previous.Version, candidate.Version) ||
            !CanAdvanceSynchronizedReleaseFingerprint(
                previous.SourceFingerprint,
                previous.SourceComponents,
                candidate.SourceFingerprint,
                candidate.SourceComponents) ||
            !CanAdvanceSynchronizedReleaseFingerprint(
                previous.PayloadFingerprint,
                previous.PayloadComponents,
                candidate.PayloadFingerprint,
                candidate.PayloadComponents))
        {
            return false;
        }

        if (!IsValidCheckpointSet(candidate.AttemptedOperations, candidate.PlannedOperations) ||
            !IsValidCheckpointSet(candidate.CompletedOperations, candidate.AttemptedOperations) ||
            !IsValidCheckpointSet(candidate.AttemptedLanes, candidate.PlannedLanes) ||
            !IsSetSubset(previous.AttemptedOperations, candidate.AttemptedOperations) ||
            !IsSetSubset(previous.CompletedOperations, candidate.CompletedOperations) ||
            !IsSetSubset(previous.AttemptedLanes, candidate.AttemptedLanes) ||
            (previous.AuxiliaryRemoteSideEffectsObserved && !candidate.AuxiliaryRemoteSideEffectsObserved))
        {
            return false;
        }

        if (candidate.Lanes.Length != candidate.AttemptedLanes.Length ||
            candidate.Lanes.Any(static lane => lane is null) ||
            candidate.Lanes.Select(static lane => lane.CheckpointKey)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != candidate.Lanes.Length ||
            candidate.Lanes.Any(lane =>
                !candidate.AttemptedLanes.Contains(lane.CheckpointKey, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        foreach (var previousLane in previous.Lanes)
        {
            var candidateLane = candidate.Lanes.FirstOrDefault(lane =>
                string.Equals(lane.CheckpointKey, previousLane.CheckpointKey, StringComparison.OrdinalIgnoreCase));
            if (candidateLane is null ||
                candidateLane.Source != previousLane.Source ||
                !string.Equals(candidateLane.Label, previousLane.Label, StringComparison.Ordinal) ||
                !SynchronizedReleaseLaneVersionsEqual(previousLane, candidateLane))
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanAdvanceSynchronizedReleaseVersion(string? previous, string? candidate)
    {
        if (string.Equals(previous, candidate, StringComparison.OrdinalIgnoreCase))
            return true;
        return string.IsNullOrWhiteSpace(previous) &&
               PackageVersionUtility.TryNormalizeExact(candidate, out _);
    }

    private static bool CanAdvanceSynchronizedReleaseFingerprint(
        string? previousFingerprint,
        string[] previousComponents,
        string? candidateFingerprint,
        string[] candidateComponents)
    {
        if (string.Equals(previousFingerprint, candidateFingerprint, StringComparison.OrdinalIgnoreCase) &&
            previousComponents.SequenceEqual(candidateComponents, StringComparer.Ordinal))
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(previousFingerprint) &&
               previousComponents.Length == 0 &&
               IsValidSynchronizedReleaseFingerprintState(candidateFingerprint, candidateComponents);
    }

    private static bool IsValidCheckpointSet(string[] values, string[] allowed)
        => values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == values.Length &&
           values.All(value => allowed.Contains(value, StringComparer.OrdinalIgnoreCase));

    private static bool IsSetSubset(string[] subset, string[] superset)
        => subset.All(value => superset.Contains(value, StringComparer.OrdinalIgnoreCase));

    private static bool SetsEqual(string[] first, string[] second)
        => first.Length == second.Length && IsSetSubset(first, second);

    private static void WriteSynchronizedReleaseCheckpointFile(string path, string content)
    {
        if (File.Exists(path))
            RequireNormalSynchronizedReleaseCheckpointFile(path);
        using var stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 4096, leaveOpen: true);
        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static void PromoteSynchronizedReleaseCheckpointFile(string temporaryPath, string path)
    {
        RequireNormalSynchronizedReleaseCheckpointFile(temporaryPath);
        if (File.Exists(path))
            RequireNormalSynchronizedReleaseCheckpointFile(path);

        const int maximumAttempts = 6;
        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            try
            {
                if (File.Exists(path))
                    File.Replace(temporaryPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                else
                    File.Move(temporaryPath, path);
                return;
            }
            catch (Exception ex) when (
                attempt < maximumAttempts &&
                ex is IOException or UnauthorizedAccessException)
            {
                System.Threading.Thread.Sleep(50 * (1 << (attempt - 1)));
            }
        }
    }

    private static void RequireNormalSynchronizedReleaseCheckpointFile(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Coordinated release checkpoint file '{path}' is missing.");

        var attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
        {
            throw new InvalidOperationException(
                $"Coordinated release checkpoint file '{path}' is not a normal file.");
        }
    }

    private sealed class SynchronizedReleaseCheckpointRead
    {
        internal SynchronizedReleaseCheckpointRead(
            SynchronizedReleaseCheckpoint checkpoint,
            string? temporaryPath)
        {
            Checkpoint = checkpoint;
            TemporaryPath = temporaryPath;
        }

        internal SynchronizedReleaseCheckpoint Checkpoint { get; }

        internal string? TemporaryPath { get; }
    }

    private static void DeleteEmptySynchronizedReleaseCheckpointDirectories(string checkpointPath)
    {
        var releaseDirectory = Path.GetDirectoryName(checkpointPath);
        DeleteDirectoryIfEmpty(releaseDirectory);
    }

    private static void DeleteDirectoryIfEmpty(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            return;

        try
        {
            if (!Directory.EnumerateFileSystemEntries(path!).Any())
                Directory.Delete(path!);
        }
        catch (IOException)
        {
            // The checkpoint itself is already removed; directory cleanup is best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // The checkpoint itself is already removed; directory cleanup is best effort.
        }
    }

    private sealed class SynchronizedReleaseCheckpoint
    {
        public int SchemaVersion { get; set; } = 5;
        public string ModuleName { get; set; } = string.Empty;
        public ReleaseVersionSource ReleaseSource { get; set; }
        public string? PrimaryProject { get; set; }
        public string Version { get; set; } = string.Empty;
        public string[] PlannedOperations { get; set; } = Array.Empty<string>();
        public string[] AttemptedOperations { get; set; } = Array.Empty<string>();
        public string[] CompletedOperations { get; set; } = Array.Empty<string>();
        public bool AuxiliaryRemoteSideEffectsObserved { get; set; }
        public string[] OperationFingerprints { get; set; } = Array.Empty<string>();
        public string SourceFingerprint { get; set; } = string.Empty;
        public string[] SourceComponents { get; set; } = Array.Empty<string>();
        public string PayloadFingerprint { get; set; } = string.Empty;
        public string[] PayloadComponents { get; set; } = Array.Empty<string>();
        public string[] PlannedLanes { get; set; } = Array.Empty<string>();
        public string[] AttemptedLanes { get; set; } = Array.Empty<string>();
        public SynchronizedReleaseLaneCheckpoint[] Lanes { get; set; } = Array.Empty<SynchronizedReleaseLaneCheckpoint>();
        public DateTimeOffset CreatedUtc { get; set; }
    }

    private sealed class SynchronizedReleaseLaneCheckpoint
    {
        public ReleaseVersionSource Source { get; set; }
        public string Label { get; set; } = string.Empty;
        public string CheckpointKey { get; set; } = string.Empty;
        public string DefaultVersion { get; set; } = string.Empty;
        public Dictionary<string, string> VersionsByProject { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
