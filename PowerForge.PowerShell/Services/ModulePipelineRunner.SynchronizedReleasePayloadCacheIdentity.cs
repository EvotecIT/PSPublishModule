using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static SynchronizedReleasePayloadLane[] ResolveSynchronizedReleasePayloadLanes(
        ModulePipelineRunState state)
    {
        var lanes = new List<SynchronizedReleasePayloadLane>();
        var checkpointKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var execution in state.ProjectBuildResults)
        {
            var candidates = state.ReleaseVersionCandidates
                .Where(candidate => ReferenceEquals(candidate.Result, execution))
                .ToArray();
            if (candidates.Length != 1 || string.IsNullOrWhiteSpace(candidates[0].CheckpointKey))
            {
                throw new InvalidOperationException(
                    "A coordinated release package result does not have exactly one stable lane identity.");
            }

            var checkpointKey = candidates[0].CheckpointKey.Trim();
            if (!checkpointKeys.Add(checkpointKey))
            {
                throw new InvalidOperationException(
                    $"Coordinated release package lane identity '{checkpointKey}' is duplicated.");
            }

            lanes.Add(new SynchronizedReleasePayloadLane(
                execution,
                checkpointKey,
                CreateSynchronizedReleaseFingerprint("payload-lane", checkpointKey.ToUpperInvariant())));
        }

        return lanes
            .OrderBy(static lane => lane.CheckpointKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SynchronizedReleasePayloadProject[] ResolveSynchronizedReleasePayloadProjects(
        SynchronizedReleasePayloadLane lane,
        DotNetRepositoryReleaseResult release)
    {
        var projects = new List<SynchronizedReleasePayloadProject>();
        var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in release.Projects)
        {
            var projectName = project.ProjectName?.Trim();
            var packageId = project.PackageId?.Trim();
            if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(packageId))
            {
                throw new InvalidOperationException(
                    $"Coordinated release package lane '{lane.CheckpointKey}' contains a project without ProjectName or PackageId identity.");
            }

            var stableProjectName = projectName!;
            var stablePackageId = packageId!;
            var identity = stableProjectName + "\n" + stablePackageId;
            if (!identities.Add(identity))
            {
                throw new InvalidOperationException(
                    $"Coordinated release package lane '{lane.CheckpointKey}' contains duplicate project identity '{stableProjectName}'/'{stablePackageId}'.");
            }

            projects.Add(new SynchronizedReleasePayloadProject(
                project,
                stableProjectName,
                stablePackageId,
                CreateSynchronizedReleaseFingerprint(
                    "payload-project",
                    stableProjectName.ToUpperInvariant(),
                    stablePackageId.ToUpperInvariant())));
        }

        return projects
            .OrderBy(static project => project.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static project => project.PackageId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static SynchronizedReleasePayloadFile[] ResolveSynchronizedReleasePayloadFiles(
        SynchronizedReleasePayloadProject project,
        IReadOnlyList<string> paths,
        string kind)
    {
        var files = new List<SynchronizedReleasePayloadFile>();
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < paths.Count; index++)
        {
            var sourcePath = paths[index];
            var fileName = string.IsNullOrWhiteSpace(sourcePath)
                ? string.Empty
                : Path.GetFileName(sourcePath.Trim());
            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException(
                    $"Coordinated release project '{project.ProjectName}'/'{project.PackageId}' contains a {kind} payload without a file identity.");
            }
            if (!fileNames.Add(fileName))
            {
                throw new InvalidOperationException(
                    $"Coordinated release project '{project.ProjectName}'/'{project.PackageId}' contains duplicate {kind} payload filename '{fileName}'.");
            }

            var cacheKey = CreateSynchronizedReleaseFingerprint(
                "payload-file",
                kind.ToUpperInvariant(),
                fileName);
            files.Add(new SynchronizedReleasePayloadFile(
                sourcePath.Trim(),
                index,
                kind,
                cacheKey));
        }

        return files
            .OrderBy(static file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string ResolveSynchronizedReleasePackageCacheEntry(
        string cachePath,
        SynchronizedReleasePayloadLane lane,
        SynchronizedReleasePayloadProject project,
        SynchronizedReleasePayloadFile file)
        => Path.Combine(
            cachePath,
            "package-lane",
            lane.CacheKey,
            "project",
            project.CacheKey,
            file.Kind,
            file.CacheKey);

    private static string ResolveSynchronizedReleasePayloadComponentLabel(
        SynchronizedReleasePayloadLane lane,
        SynchronizedReleasePayloadProject project,
        SynchronizedReleasePayloadFile file)
        => $"package-lane/{lane.CacheKey}/project/{project.CacheKey}/{file.Kind}/{file.CacheKey}";

    private static void DeleteStaleSynchronizedReleasePayloadCaches(string cachePath)
    {
        var parentPath = Path.GetDirectoryName(cachePath);
        if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath))
            return;

        var fullParentPath = Path.GetFullPath(parentPath);
        var prefix = Path.GetFileName(cachePath) + ".tmp-";
        var comparison = FrameworkCompatibility.PathStringComparison();
        foreach (var candidatePath in Directory.EnumerateDirectories(
                     fullParentPath,
                     prefix + "*",
                     SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(candidatePath);
            if (!name.StartsWith(prefix, comparison) ||
                !Guid.TryParseExact(name.Substring(prefix.Length), "N", out _))
            {
                continue;
            }

            var fullCandidatePath = Path.GetFullPath(candidatePath);
            if (!string.Equals(Path.GetDirectoryName(fullCandidatePath), fullParentPath, comparison))
                continue;

            if ((File.GetAttributes(fullCandidatePath) & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(fullCandidatePath, recursive: false);
            else
                DeleteDirectoryWithRetries(fullCandidatePath);
        }
    }

    private static void DeleteStaleSynchronizedReleaseRestorePaths(string destinationPath)
    {
        var parentPath = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(parentPath) || !Directory.Exists(parentPath))
            return;

        var fullParentPath = Path.GetFullPath(parentPath);
        var prefix = Path.GetFileName(destinationPath) + ".restore-";
        var comparison = FrameworkCompatibility.PathStringComparison();
        foreach (var candidatePath in Directory.EnumerateFileSystemEntries(
                     fullParentPath,
                     prefix + "*",
                     SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(candidatePath);
            if (!name.StartsWith(prefix, comparison) ||
                !Guid.TryParseExact(name.Substring(prefix.Length), "N", out _))
            {
                continue;
            }

            var fullCandidatePath = Path.GetFullPath(candidatePath);
            if (!string.Equals(Path.GetDirectoryName(fullCandidatePath), fullParentPath, comparison))
                continue;

            var attributes = File.GetAttributes(fullCandidatePath);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    Directory.Delete(fullCandidatePath, recursive: false);
                else
                    DeleteDirectoryWithRetries(fullCandidatePath);
            }
            else
            {
                File.Delete(fullCandidatePath);
            }
        }
    }

    private sealed class SynchronizedReleasePayloadLane
    {
        public SynchronizedReleasePayloadLane(
            ProjectBuildHostExecutionResult execution,
            string checkpointKey,
            string cacheKey)
        {
            Execution = execution;
            CheckpointKey = checkpointKey;
            CacheKey = cacheKey;
        }

        public ProjectBuildHostExecutionResult Execution { get; }
        public string CheckpointKey { get; }
        public string CacheKey { get; }
    }

    private sealed class SynchronizedReleasePayloadProject
    {
        public SynchronizedReleasePayloadProject(
            DotNetRepositoryProjectResult project,
            string projectName,
            string packageId,
            string cacheKey)
        {
            Project = project;
            ProjectName = projectName;
            PackageId = packageId;
            CacheKey = cacheKey;
        }

        public DotNetRepositoryProjectResult Project { get; }
        public string ProjectName { get; }
        public string PackageId { get; }
        public string CacheKey { get; }
    }

    private sealed class SynchronizedReleasePayloadFile
    {
        public SynchronizedReleasePayloadFile(
            string sourcePath,
            int itemIndex,
            string kind,
            string cacheKey)
        {
            SourcePath = sourcePath;
            ItemIndex = itemIndex;
            Kind = kind;
            CacheKey = cacheKey;
            FileName = Path.GetFileName(sourcePath);
        }

        public string SourcePath { get; }
        public int ItemIndex { get; }
        public string Kind { get; }
        public string CacheKey { get; }
        public string FileName { get; }
    }
}
