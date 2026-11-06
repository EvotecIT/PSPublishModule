using System.Collections.Generic;
using System.IO;
using System.Linq;
using NuGet.Packaging;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static bool ShouldReusePreparedPackageLane(ModulePipelinePlan plan, PackageBuildExecutionMode mode)
        => plan.Release?.Configuration?.ReusePreparedPackages == true &&
           mode is PackageBuildExecutionMode.DependencyBuild or PackageBuildExecutionMode.BuildOnly;

    /// <summary>Checks the complete persisted payload before restoration. The final bind checks live source before upload.</summary>
    private static void ValidatePreparedPackageRecovery(ModulePipelinePlan plan, ModulePipelineRunState state)
    {
        if (plan.Release?.Configuration?.ReusePreparedPackages != true)
            return;

        var checkpoint = state.SynchronizedReleaseCheckpoint;
        if (!state.IsResumingSynchronizedRelease || checkpoint is null ||
            string.IsNullOrWhiteSpace(checkpoint.PayloadFingerprint))
        {
            throw new InvalidOperationException(
                "ReusePreparedPackages requires ResumeIncompleteRelease and its complete coordinated release payload cache.");
        }
        if (plan.GenerateReleaseProvenance || plan.RequireReleaseSourceUnchanged)
            throw new InvalidOperationException("ReusePreparedPackages does not support rebuilding controlled release provenance.");

        // Schema 5 already records every stable cache identity and content hash. Check the
        // complete snapshot before restoring any package, including older descriptor-free caches.
        string cachePath = ResolveSynchronizedReleasePayloadCachePath(state);
        var components = new List<string>();
        foreach (string record in checkpoint.PayloadComponents)
        {
            string[] fields = record.Split('|');
            if (fields.Length < 2)
                throw new InvalidOperationException("The coordinated release payload cache has an invalid component record.");
            string entry = ResolvePreparedCacheEntry(cachePath, fields[0]);
            switch (fields[1])
            {
                case "identity" when fields.Length == 2:
                    // Non-packable projects have a stable identity but no payload directory.
                    // Any recorded child payload is still verified independently below.
                    components.Add(record);
                    break;
                case "directory" when fields.Length == 3:
                    string directory = fields[0] == "module" ? entry : Path.Combine(entry, "directory");
                    FileSystemPathSafety.RejectReparsePoints(directory, cachePath, "Prepared release payload");
                    AddSynchronizedReleaseExactPayloadPath(components, fields[0], directory);
                    break;
                case "file" when fields.Length == 4:
                    string file = ResolveCachedSynchronizedReleasePayloadPath(entry, directory: false);
                    FileSystemPathSafety.RejectReparsePoints(file, cachePath, "Prepared release payload");
                    AddSynchronizedReleaseExactPayloadPath(components, fields[0], file);
                    break;
                default:
                    throw new InvalidOperationException("The coordinated release payload cache has an invalid or missing component.");
            }
        }
        components.Sort(StringComparer.Ordinal);
        if (!string.Equals(checkpoint.PayloadFingerprint, CreateSynchronizedReleaseFingerprint(components.ToArray()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The exact signed payload cache for the incomplete coordinated release is missing or corrupt.");
    }

    private static string ResolvePreparedCacheEntry(string cachePath, string label)
    {
        string entry = Path.GetFullPath(Path.Combine(cachePath, label.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsSameOrChildPath(cachePath, entry) || string.Equals(Path.GetFullPath(cachePath), entry, FrameworkCompatibility.PathStringComparison()))
            throw new InvalidOperationException("A prepared release payload component escapes its cache root.");
        return entry;
    }

    /// <summary>Rehydrates package results from a schema-5 cache and restores their exact files to the configured output paths.</summary>
    private ProjectBuildHostExecutionResult RestorePreparedPackageLane(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        ProjectBuildConfiguration configuration,
        string configPath,
        string checkpointKey)
    {
        var checkpoint = state.SynchronizedReleaseCheckpoint!;
        var lane = checkpoint.Lanes.Single(candidate =>
            string.Equals(candidate.CheckpointKey, checkpointKey, StringComparison.OrdinalIgnoreCase));
        string cachePath = ResolveSynchronizedReleasePayloadCachePath(state);
        string laneKey = CreateSynchronizedReleaseFingerprint("payload-lane", checkpointKey.ToUpperInvariant());
        string projectsPath = ResolvePreparedCacheEntry(cachePath, $"package-lane/{laneKey}/project");
        var preparation = new ProjectBuildPreparationService(_logger).Prepare(
            configuration,
            Path.GetDirectoryName(configPath)!,
            planPath: null,
            new ProjectBuildRequestedActions
            {
                PlanOnly = true,
                Build = false,
                UpdateVersions = false,
                PublishNuget = false,
                PublishGitHub = false
            });
        string packageOutput = preparation.OutputPath ?? throw new InvalidOperationException(
            "ReusePreparedPackages requires an explicit package OutputPath or StagingPath.");
        string archiveOutput = preparation.ReleaseZipOutputPath ?? packageOutput;
        var release = new DotNetRepositoryReleaseResult
        {
            ResolvedVersion = lane.DefaultVersion,
            SourceCommitSha = checkpoint.PackageSourceCommitShas.TryGetValue(checkpointKey, out string? commit) ? commit : null
        };
        foreach (var version in lane.VersionsByProject)
            release.ResolvedVersionsByProject.Add(version.Key, version.Value);

        foreach (string projectPath in (Directory.Exists(projectsPath) ? Directory.EnumerateDirectories(projectsPath) : Array.Empty<string>())
                     .OrderBy(static path => path, StringComparer.Ordinal))
        {
            string[] packages = ReadPreparedPackageFiles(projectPath, "package");
            if (packages.Length == 0)
                throw new InvalidOperationException("ReusePreparedPackages requires a cached primary package for every release project.");
            using var reader = new PackageArchiveReader(packages[0]);
            string id = reader.NuspecReader.GetId();
            string version = reader.NuspecReader.GetVersion().ToNormalizedString();
            string projectKey = Path.GetFileName(projectPath);
            string projectName = lane.VersionsByProject.Keys.Single(name => string.Equals(
                CreateSynchronizedReleaseFingerprint("payload-project", name.ToUpperInvariant(), id.ToUpperInvariant()),
                projectKey, StringComparison.OrdinalIgnoreCase));
            if (!PackageVersionUtility.TryNormalizeExact(lane.VersionsByProject[projectName], out string expectedVersion) ||
                !string.Equals(version, expectedVersion, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Prepared package '{id}' does not match its checkpoint version.");
            var project = new DotNetRepositoryProjectResult
            {
                ProjectName = projectName,
                PackageId = id,
                IsPackable = true,
                NewVersion = version
            };
            foreach (string package in packages)
                project.Packages.Add(RestorePreparedPackageFile(package, packageOutput));
            foreach (string symbols in ReadPreparedPackageFiles(projectPath, "symbols"))
                project.SymbolPackages.Add(RestorePreparedPackageFile(symbols, packageOutput));
            string[] archives = ReadPreparedPackageFiles(projectPath, "release-zip");
            if (archives.Length > 1)
                throw new InvalidOperationException($"Prepared project '{projectName}' has multiple release archives.");
            if (archives.Length == 1)
                project.ReleaseZipPath = RestorePreparedPackageFile(archives[0], archiveOutput);
            release.Projects.Add(project);
        }
        RestorePreparedPayloadFreeProjects(state, configuration, configPath, checkpointKey, laneKey, release);
        if (lane.VersionsByProject.Keys.Any(name => !release.Projects.Any(project =>
                string.Equals(project.ProjectName, name, StringComparison.OrdinalIgnoreCase))))
            throw new InvalidOperationException("A version-bearing project is missing from the prepared package identities.");

        // Cache-directory hashes are storage identities, not a NuGet dependency order.
        release.Projects = new DotNetRepositoryReleaseService(_logger)
            .SortProjectsForPublish(release.Projects.Where(static project => project.IsPackable).ToArray())
            .Concat(release.Projects.Where(static project => !project.IsPackable))
            .ToList();

        _logger.Info($"Reusing {release.Projects.Count} exact prepared package projects without rebuilding or signing: {configPath}");
        return new ProjectBuildHostExecutionResult
        {
            Success = true,
            ConfigPath = configPath,
            RootPath = preparation.RootPath,
            StagingPath = preparation.StagingPath,
            OutputPath = packageOutput,
            ReleaseZipOutputPath = archiveOutput,
            PlanOutputPath = preparation.PlanOutputPath,
            Result = new ProjectBuildResult { Success = true, Release = release }
        };
    }

    /// <summary>Resolves descriptor-free, non-packable identities through the normal read-only project plan.</summary>
    private void RestorePreparedPayloadFreeProjects(
        ModulePipelineRunState state,
        ProjectBuildConfiguration configuration,
        string configPath,
        string checkpointKey,
        string laneKey,
        DotNetRepositoryReleaseResult release)
    {
        var checkpoint = state.SynchronizedReleaseCheckpoint!;
        string prefix = $"package-lane/{laneKey}/project/";
        var remaining = checkpoint.PayloadComponents
            .Where(record => record.StartsWith(prefix, StringComparison.Ordinal) && record.EndsWith("|identity", StringComparison.Ordinal))
            .Select(record => record.Substring(prefix.Length, record.Length - prefix.Length - "|identity".Length))
            .Where(static key => !key.Contains('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var project in release.Projects)
        {
            if (!remaining.Remove(CreateSynchronizedReleaseFingerprint("payload-project", project.ProjectName.ToUpperInvariant(), project.PackageId.ToUpperInvariant())))
                throw new InvalidOperationException("A prepared package project is missing from the persisted identity set.");
        }
        if (remaining.Count == 0)
            return;

        var planned = _packageBuildExecutor(new ProjectBuildHostRequest
        {
            ConfigPath = configPath,
            ExecuteBuild = false,
            PlanOnly = true,
            UpdateVersions = false,
            Build = false,
            PublishNuget = false,
            PublishGitHub = false
        }, configuration, configPath);
        if (!planned.Success || planned.Result.Release is not { Success: true, IsPlan: true } metadata)
            throw new InvalidOperationException("Prepared payload-free project metadata could not be resolved through a read-only plan.");
        foreach (var project in metadata.Projects.Where(static project => !project.IsPackable))
        {
            string key = CreateSynchronizedReleaseFingerprint("payload-project", project.ProjectName.ToUpperInvariant(), project.PackageId.ToUpperInvariant());
            if (!remaining.Remove(key))
                continue;
            // Native checkpoints omit versions for non-packable projects. Older/custom
            // producers may retain one; preserve it when present without requiring it.
            release.ResolvedVersionsByProject.TryGetValue(project.ProjectName, out string? version);
            release.Projects.Add(new DotNetRepositoryProjectResult
            {
                ProjectName = project.ProjectName,
                PackageId = project.PackageId,
                CsprojPath = project.CsprojPath,
                IsPackable = false,
                NewVersion = version
            });
        }
        if (remaining.Count != 0)
            throw new InvalidOperationException($"Prepared package lane '{checkpointKey}' has identities without matching payloads or non-packable project metadata.");
    }

    private static string[] ReadPreparedPackageFiles(string projectPath, string kind)
    {
        string kindPath = Path.Combine(projectPath, kind);
        return Directory.Exists(kindPath)
            ? Directory.EnumerateDirectories(kindPath).OrderBy(static path => path, StringComparer.Ordinal)
                .Select(path => ResolveCachedSynchronizedReleasePayloadPath(path, directory: false)).ToArray()
            : Array.Empty<string>();
    }

    private static string RestorePreparedPackageFile(string source, string outputPath)
    {
        string destination = Path.Combine(outputPath, Path.GetFileName(source));
        RestoreSynchronizedReleaseFile(source, destination);
        return destination;
    }
}
