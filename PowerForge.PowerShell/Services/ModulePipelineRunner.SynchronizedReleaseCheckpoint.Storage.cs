using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static string ResolveSynchronizedReleaseCheckpointPath(ModulePipelinePlan plan)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safeModuleName = new string(plan.ModuleName
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        return Path.Combine(
            ResolveSynchronizedReleaseStateRoot(plan.ProjectRoot),
            "coordinated-release",
            $"{safeModuleName}.json");
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

        var localStateRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localStateRoot))
            localStateRoot = Path.GetTempPath();

        return Path.Combine(
            localStateRoot,
            "PowerForge",
            "coordinated-release-projects",
            CreateSynchronizedReleaseFingerprint("ProjectRoot", canonicalProjectRoot));
    }

    private void EnterSynchronizedReleaseCheckpointScope(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        state.PlannedSynchronizedOperationCount = ResolvePlannedSynchronizedPublishOperationKeys(plan).Length;
        var checkpointPath = ResolveSynchronizedReleaseCheckpointPath(plan);
        if (state.PlannedSynchronizedOperationCount == 0 && !File.Exists(checkpointPath))
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
        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(checkpoint, new JsonSerializerOptions { WriteIndented = true }));
        if (File.Exists(path))
            File.Replace(temporaryPath, path, destinationBackupFileName: null);
        else
            File.Move(temporaryPath, path);
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
        public int SchemaVersion { get; set; } = 4;
        public string ModuleName { get; set; } = string.Empty;
        public ReleaseVersionSource ReleaseSource { get; set; }
        public string? PrimaryProject { get; set; }
        public string Version { get; set; } = string.Empty;
        public string[] PlannedOperations { get; set; } = Array.Empty<string>();
        public string[] AttemptedOperations { get; set; } = Array.Empty<string>();
        public string[] CompletedOperations { get; set; } = Array.Empty<string>();
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
