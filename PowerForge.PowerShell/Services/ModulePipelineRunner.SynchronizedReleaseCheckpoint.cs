using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private void RestoreSynchronizedReleaseCheckpoint(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        var path = ResolveSynchronizedReleaseCheckpointPath(plan);
        var checkpointExists = File.Exists(path);
        if (!ShouldUseSynchronizedReleaseCheckpoint(plan, state))
        {
            if (plan.GateMode is ConfigurationGateMode.Manifest or
                ConfigurationGateMode.Documentation or
                ConfigurationGateMode.Build)
            {
                return;
            }
            if (checkpointExists)
            {
                throw new InvalidOperationException(
                    $"Coordinated release configuration no longer matches incomplete checkpoint '{path}'. Restore synchronization and publishing or delete the checkpoint only if that release should be abandoned.");
            }
            return;
        }

        state.SynchronizedReleaseCheckpointPath = path;
        if (!checkpointExists)
            return;

        SynchronizedReleaseCheckpoint? checkpoint;
        try
        {
            checkpoint = JsonSerializer.Deserialize<SynchronizedReleaseCheckpoint>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Coordinated release checkpoint '{path}' could not be read. Delete it only if the incomplete release should be abandoned. {ex.Message}",
                ex);
        }

        if (checkpoint is null || checkpoint.SchemaVersion != 4)
        {
            throw new InvalidOperationException(
                $"Coordinated release checkpoint '{path}' has an unsupported schema. Delete it only if the incomplete release should be abandoned.");
        }

        try
        {
            ValidateSynchronizedReleaseCheckpoint(plan, state, checkpoint, path);
        }
        catch (InvalidOperationException) when (IsPristineSynchronizedReleaseCheckpoint(checkpoint))
        {
            File.Delete(path);
            DeleteEmptySynchronizedReleaseCheckpointDirectories(path);
            _logger.Warn(
                $"Discarded unused coordinated release checkpoint '{path}' because release configuration changed before any package lane or publish operation started.");
            return;
        }

        state.SynchronizedReleaseCheckpoint = checkpoint;
        state.IsResumingSynchronizedRelease = true;
        var versionDescription = string.IsNullOrWhiteSpace(checkpoint.Version)
            ? "pending version resolution"
            : checkpoint.Version;
        _logger.Warn(
            $"Resuming incomplete coordinated release ({versionDescription}) from '{path}'. Completed publish operations: {DescribeCompletedOperations(checkpoint)}.");
    }

    private void InitializeSynchronizedReleaseCheckpoint(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        if (!ShouldUseSynchronizedReleaseCheckpoint(plan, state))
            return;

        var synchronizedVersion = ModulePathTokenFormatter.FormatVersionWithPreRelease(
            plan.ResolvedVersion,
            plan.PreRelease);
        if (!PackageVersionUtility.TryNormalizeExact(synchronizedVersion, out var normalizedVersion))
        {
            throw new InvalidOperationException(
                $"Coordinated release version '{synchronizedVersion}' is not a valid exact package version.");
        }

        PrepareSynchronizedReleaseCheckpoint(plan, state);
        var checkpoint = state.SynchronizedReleaseCheckpoint ?? throw new InvalidOperationException(
            "Coordinated release checkpoint was not prepared before version synchronization.");
        if (!string.IsNullOrWhiteSpace(checkpoint.Version) &&
            !string.Equals(checkpoint.Version, normalizedVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The resumed package build resolved version '{normalizedVersion}', but the coordinated release checkpoint requires '{checkpoint.Version}'.");
        }

        checkpoint.Version = normalizedVersion;
        foreach (var candidate in state.ReleaseVersionCandidates)
        {
            RecordSynchronizedReleaseLaneCheckpoint(
                state,
                candidate.Source,
                candidate.Label,
                candidate.CheckpointKey,
                candidate.Result);
        }

        if (checkpoint.Lanes.Length == 0)
        {
            throw new InvalidOperationException(
                "A coordinated release checkpoint could not be finalized because no release-source version result was available.");
        }

        SaveSynchronizedReleaseCheckpoint(state);
    }

    private void PrepareSynchronizedReleaseCheckpoint(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        if (!ShouldUseSynchronizedReleaseCheckpoint(plan, state) ||
            state.SynchronizedReleaseCheckpoint is not null)
        {
            return;
        }

        var release = plan.Release!.Configuration;
        var plannedOperations = ResolvePlannedSynchronizedPublishOperationKeys(plan);
        var checkpoint = new SynchronizedReleaseCheckpoint
        {
            ModuleName = plan.ModuleName,
            ReleaseSource = release.VersionSource,
            PrimaryProject = NormalizeCheckpointValue(release.PrimaryProject),
            PlannedOperations = plannedOperations
                .OrderBy(static operation => operation, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            PlannedLanes = ResolveSynchronizedReleaseLaneKeys(plan),
            OperationFingerprints = ResolveSynchronizedReleaseOperationFingerprints(plan),
            CreatedUtc = DateTimeOffset.UtcNow
        };

        state.SynchronizedReleaseCheckpoint = checkpoint;
        state.SynchronizedReleaseCheckpointPath ??= ResolveSynchronizedReleaseCheckpointPath(plan);
        SaveSynchronizedReleaseCheckpoint(state);
        _logger.Info($"Coordinated release checkpoint created at '{state.SynchronizedReleaseCheckpointPath}'.");
    }

    private static string[] ResolveSynchronizedReleaseLaneKeys(ModulePipelinePlan plan)
    {
        var keys = new List<string>();
        foreach (var segment in plan.ProjectBuilds ?? Array.Empty<ConfigurationProjectBuildSegment>())
        {
            if (segment?.Configuration is null)
                continue;

            var reference = segment.Configuration;
            var laneLabel = reference.Name ?? ResolvePackageBuildPath(plan.ProjectRoot, reference.ConfigPath);
            keys.Add(ResolveSynchronizedReleaseLaneKey(
                plan,
                ReleaseVersionSource.ProjectBuild,
                segment,
                laneLabel));
        }

        foreach (var segment in plan.PackageBuilds ?? Array.Empty<ConfigurationPackageBuildSegment>())
        {
            if (segment?.Configuration is null)
                continue;

            var packageBuild = segment.Configuration;
            var laneLabel = packageBuild.Name ?? Path.Combine(plan.ProjectRoot, "module.packagebuild.inline.json");
            keys.Add(ResolveSynchronizedReleaseLaneKey(
                plan,
                ReleaseVersionSource.PackageBuild,
                segment,
                laneLabel));
        }

        return keys
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ApplySynchronizedReleaseCheckpointVersion(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        ReleaseVersionSource source,
        string laneLabel,
        string checkpointKey,
        ProjectBuildConfiguration configuration)
    {
        var checkpoint = state.SynchronizedReleaseCheckpoint;
        if (checkpoint is null)
        {
            return;
        }

        var lane = checkpoint.Lanes.FirstOrDefault(candidate =>
            candidate.Source == source &&
            string.Equals(candidate.CheckpointKey, checkpointKey, StringComparison.OrdinalIgnoreCase));
        if (lane is null)
        {
            if (state.IsResumingSynchronizedRelease &&
                checkpoint.AttemptedLanes.Contains(checkpointKey, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The incomplete coordinated release records an interrupted {source} lane '{laneLabel}' without exact version state. Delete the checkpoint only if that release should be abandoned.");
            }
            return;
        }

        configuration.ExpectedVersion = lane.DefaultVersion;
        configuration.ExpectedVersionMap ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in lane.VersionsByProject)
            configuration.ExpectedVersionMap[entry.Key] = entry.Value;
    }

    private static string ResolveSynchronizedReleaseLaneKey(
        ModulePipelinePlan plan,
        ReleaseVersionSource source,
        object segment,
        string laneLabel)
    {
        var index = segment switch
        {
            ConfigurationProjectBuildSegment projectBuild => Array.IndexOf(plan.ProjectBuilds, projectBuild),
            ConfigurationPackageBuildSegment packageBuild => Array.IndexOf(plan.PackageBuilds, packageBuild),
            _ => -1
        };
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"Package lane '{laneLabel}' is not present in the active module pipeline plan.");
        }

        return CreateSynchronizedReleaseFingerprint(
            "Lane",
            source.ToString(),
            index.ToString(),
            laneLabel);
    }

    private void RecordSynchronizedReleaseLaneCheckpoint(
        ModulePipelineRunState state,
        ReleaseVersionSource source,
        string laneLabel,
        string checkpointKey,
        ProjectBuildHostExecutionResult result)
    {
        var checkpoint = state.SynchronizedReleaseCheckpoint;
        if (checkpoint is null || !HasCheckpointableReleaseVersions(result))
            return;

        EnsureSynchronizedReleaseLaneIsPlanned(checkpoint, checkpointKey);
        if (!checkpoint.AttemptedLanes.Contains(checkpointKey, StringComparer.OrdinalIgnoreCase))
        {
            checkpoint.AttemptedLanes = checkpoint.AttemptedLanes
                .Append(checkpointKey)
                .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var updatedLane = CreateSynchronizedReleaseLaneCheckpoint(
            source,
            laneLabel,
            checkpointKey,
            result);
        var existingIndex = Array.FindIndex(
            checkpoint.Lanes,
            lane => lane.Source == source &&
                    string.Equals(lane.CheckpointKey, checkpointKey, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            var existingLane = checkpoint.Lanes[existingIndex];
            if (!SynchronizedReleaseLaneVersionsEqual(existingLane, updatedLane))
            {
                throw new InvalidOperationException(
                    $"Package lane '{laneLabel}' resolved versions that do not match incomplete coordinated release {checkpoint.Version}.");
            }
            return;
        }

        checkpoint.Lanes = checkpoint.Lanes
            .Append(updatedLane)
            .OrderBy(static lane => lane.Source)
            .ThenBy(static lane => lane.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SaveSynchronizedReleaseCheckpoint(state);
    }

    private void MarkSynchronizedReleaseLaneAttempted(
        ModulePipelineRunState state,
        string checkpointKey)
    {
        var checkpoint = state.SynchronizedReleaseCheckpoint;
        if (checkpoint is null)
            return;

        EnsureSynchronizedReleaseLaneIsPlanned(checkpoint, checkpointKey);
        if (checkpoint.AttemptedLanes.Contains(checkpointKey, StringComparer.OrdinalIgnoreCase))
            return;

        checkpoint.AttemptedLanes = checkpoint.AttemptedLanes
            .Append(checkpointKey)
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SaveSynchronizedReleaseCheckpoint(state);
    }

    private static void EnsureSynchronizedReleaseLaneIsPlanned(
        SynchronizedReleaseCheckpoint checkpoint,
        string checkpointKey)
    {
        if (!checkpoint.PlannedLanes.Contains(checkpointKey, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "A coordinated release package lane does not match the initialized checkpoint.");
        }
    }

    private string[] ResolvePlannedSynchronizedPublishOperationKeys(ModulePipelinePlan plan)
    {
        if (plan.Release?.Configuration?.SynchronizeModuleVersion != true ||
            plan.GateMode is ConfigurationGateMode.Manifest or ConfigurationGateMode.Documentation or ConfigurationGateMode.Build)
        {
            return Array.Empty<string>();
        }

        return ResolveSynchronizedReleasePublishOperationKeys(plan);
    }

    private static bool ShouldUseSynchronizedReleaseCheckpoint(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
        => plan.Release?.Configuration?.SynchronizeModuleVersion == true &&
           state.PlannedSynchronizedOperationCount > 0;

    private static bool ShouldSkipSynchronizedReleaseOperation(
        ModulePipelineRunState state,
        string operationKey)
        => state.SynchronizedReleaseCheckpoint is not null &&
           state.SynchronizedReleaseCheckpoint.CompletedOperations.Contains(
               operationKey,
               StringComparer.OrdinalIgnoreCase);

    private static bool WasSynchronizedReleaseOperationAttempted(
        ModulePipelineRunState state,
        string operationKey)
        => state.SynchronizedReleaseCheckpoint is not null &&
           (state.SynchronizedReleaseCheckpoint.AttemptedOperations.Contains(
                operationKey,
                StringComparer.OrdinalIgnoreCase) ||
            state.SynchronizedReleaseCheckpoint.CompletedOperations.Contains(
                operationKey,
                StringComparer.OrdinalIgnoreCase));

    private void MarkSynchronizedReleaseOperationAttempted(
        ModulePipelineRunState state,
        string operationKey)
    {
        var checkpoint = state.SynchronizedReleaseCheckpoint;
        if (checkpoint is null)
            return;

        EnsureSynchronizedReleaseOperationIsPlanned(checkpoint, operationKey);
        if (checkpoint.AttemptedOperations.Contains(operationKey, StringComparer.OrdinalIgnoreCase))
            return;

        checkpoint.AttemptedOperations = checkpoint.AttemptedOperations
            .Append(operationKey)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SaveSynchronizedReleaseCheckpoint(state);
    }

    private void MarkSynchronizedReleaseOperationCompleted(
        ModulePipelineRunState state,
        string operationKey)
    {
        var checkpoint = state.SynchronizedReleaseCheckpoint;
        if (checkpoint is null)
            return;

        EnsureSynchronizedReleaseOperationIsPlanned(checkpoint, operationKey);
        if (!checkpoint.AttemptedOperations.Contains(operationKey, StringComparer.OrdinalIgnoreCase))
        {
            checkpoint.AttemptedOperations = checkpoint.AttemptedOperations
                .Append(operationKey)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        if (checkpoint.CompletedOperations.Contains(operationKey, StringComparer.OrdinalIgnoreCase))
            return;

        checkpoint.CompletedOperations = checkpoint.CompletedOperations
            .Append(operationKey)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SaveSynchronizedReleaseCheckpoint(state);
    }

    private static void EnsureSynchronizedReleaseOperationIsPlanned(
        SynchronizedReleaseCheckpoint checkpoint,
        string operationKey)
    {
        if (!checkpoint.PlannedOperations.Contains(operationKey, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "A coordinated release publish operation does not match the initialized checkpoint.");
        }
    }

    private void CompleteSynchronizedReleaseCheckpoint(ModulePipelineRunState state)
    {
        var checkpoint = state.SynchronizedReleaseCheckpoint;
        var path = state.SynchronizedReleaseCheckpointPath;
        if (checkpoint is null || string.IsNullOrWhiteSpace(path))
            return;

        var complete = checkpoint.PlannedOperations.All(operation =>
            checkpoint.CompletedOperations.Contains(operation, StringComparer.OrdinalIgnoreCase));
        if (!complete)
            return;

        if (File.Exists(path))
            File.Delete(path);
        var payloadCachePath = ResolveSynchronizedReleasePayloadCachePath(path!);
        if (Directory.Exists(payloadCachePath))
        {
            try
            {
                DeleteDirectoryWithRetries(payloadCachePath);
            }
            catch (Exception ex)
            {
                _logger.Warn($"Failed to delete completed coordinated release payload cache '{payloadCachePath}': {ex.Message}");
            }
        }
        DeleteEmptySynchronizedReleaseCheckpointDirectories(path!);
        state.SynchronizedReleaseCheckpoint = null;
        _logger.Success($"Coordinated release {checkpoint.Version} completed; checkpoint removed.");
    }

    private static SynchronizedReleaseLaneCheckpoint CreateSynchronizedReleaseLaneCheckpoint(
        ReleaseVersionCandidate candidate)
        => CreateSynchronizedReleaseLaneCheckpoint(
            candidate.Source,
            candidate.Label,
            candidate.CheckpointKey,
            candidate.Result);

    private static SynchronizedReleaseLaneCheckpoint CreateSynchronizedReleaseLaneCheckpoint(
        ReleaseVersionSource source,
        string laneLabel,
        string checkpointKey,
        ProjectBuildHostExecutionResult result)
    {
        var release = result.Result.Release ?? throw new InvalidOperationException(
            $"Package lane '{laneLabel}' did not return release version details.");
        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in release.ResolvedVersionsByProject)
        {
            if (string.IsNullOrWhiteSpace(entry.Key))
                continue;
            if (!PackageVersionUtility.TryNormalizeExact(entry.Value, out var normalizedEntryVersion))
            {
                throw new InvalidOperationException(
                    $"Package lane '{laneLabel}' returned invalid version state for project '{entry.Key}'.");
            }

            versions[entry.Key.Trim()] = normalizedEntryVersion;
        }
        foreach (var project in release.Projects.Where(static project => project.IsPackable))
        {
            var projectVersion = ResolveExactSynchronizedReleaseProjectVersion(release, project);
            if (string.IsNullOrWhiteSpace(projectVersion))
            {
                throw new InvalidOperationException(
                    $"Package lane '{laneLabel}' did not return exact version state for project '{ResolveSynchronizedReleaseProjectLabel(project)}'.");
            }

            if (!string.IsNullOrWhiteSpace(project.ProjectName))
                versions[project.ProjectName.Trim()] = projectVersion!;
            if (!string.IsNullOrWhiteSpace(project.PackageId))
                versions[project.PackageId.Trim()] = projectVersion!;
        }

        var defaultVersionCandidate = string.IsNullOrWhiteSpace(release.ResolvedVersion)
            ? versions.Values.FirstOrDefault()
            : release.ResolvedVersion;
        if (!PackageVersionUtility.TryNormalizeExact(defaultVersionCandidate, out var defaultVersion))
        {
            throw new InvalidOperationException(
                $"Package lane '{laneLabel}' did not return an exact release version.");
        }

        return new SynchronizedReleaseLaneCheckpoint
        {
            Source = source,
            Label = laneLabel,
            CheckpointKey = checkpointKey,
            DefaultVersion = defaultVersion,
            VersionsByProject = versions
        };
    }

    private static bool HasCheckpointableReleaseVersions(ProjectBuildHostExecutionResult result)
    {
        var release = result.Result.Release;
        if (release is null)
            return false;

        if ((!string.IsNullOrWhiteSpace(release.ResolvedVersion) &&
             !PackageVersionUtility.TryNormalizeExact(release.ResolvedVersion, out _)) ||
            release.ResolvedVersionsByProject.Any(static entry =>
                !string.IsNullOrWhiteSpace(entry.Key) &&
                !PackageVersionUtility.TryNormalizeExact(entry.Value, out _)))
        {
            return false;
        }

        var participatingProjects = release.Projects
            .Where(static project => project.IsPackable)
            .ToArray();
        if (participatingProjects.Length == 0)
            return PackageVersionUtility.TryNormalizeExact(release.ResolvedVersion, out _);

        return participatingProjects.All(project =>
            (!string.IsNullOrWhiteSpace(project.ProjectName) || !string.IsNullOrWhiteSpace(project.PackageId)) &&
            !string.IsNullOrWhiteSpace(ResolveExactSynchronizedReleaseProjectVersion(release, project)));
    }

    private static string? ResolveExactSynchronizedReleaseProjectVersion(
        DotNetRepositoryReleaseResult release,
        DotNetRepositoryProjectResult project)
    {
        string? version = null;
        if (!string.IsNullOrWhiteSpace(project.ProjectName) &&
            release.ResolvedVersionsByProject.TryGetValue(project.ProjectName, out var projectVersion))
        {
            version = projectVersion;
        }
        else if (!string.IsNullOrWhiteSpace(project.PackageId) &&
                 release.ResolvedVersionsByProject.TryGetValue(project.PackageId, out var packageVersion))
        {
            version = packageVersion;
        }
        else
        {
            version = project.NewVersion;
        }

        return PackageVersionUtility.TryNormalizeExact(version, out var normalizedVersion)
            ? normalizedVersion
            : null;
    }

    private static string ResolveSynchronizedReleaseProjectLabel(DotNetRepositoryProjectResult project)
        => !string.IsNullOrWhiteSpace(project.ProjectName)
            ? project.ProjectName.Trim()
            : !string.IsNullOrWhiteSpace(project.PackageId)
                ? project.PackageId.Trim()
                : "<unknown>";

    private static bool SynchronizedReleaseLaneVersionsEqual(
        SynchronizedReleaseLaneCheckpoint first,
        SynchronizedReleaseLaneCheckpoint second)
    {
        if (!string.Equals(first.DefaultVersion, second.DefaultVersion, StringComparison.OrdinalIgnoreCase) ||
            first.VersionsByProject.Count != second.VersionsByProject.Count)
        {
            return false;
        }

        foreach (var entry in first.VersionsByProject)
        {
            if (!second.VersionsByProject.TryGetValue(entry.Key, out var otherVersion) ||
                !string.Equals(entry.Value, otherVersion, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private void ValidateSynchronizedReleaseCheckpoint(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        SynchronizedReleaseCheckpoint checkpoint,
        string path)
    {
        var release = plan.Release!.Configuration;
        if (checkpoint.PlannedOperations is null ||
            checkpoint.PlannedOperations.Length == 0 ||
            checkpoint.AttemptedOperations is null ||
            checkpoint.CompletedOperations is null ||
            checkpoint.OperationFingerprints is null ||
            checkpoint.OperationFingerprints.Length == 0 ||
            checkpoint.SourceFingerprint is null ||
            checkpoint.SourceComponents is null ||
            checkpoint.PayloadFingerprint is null ||
            checkpoint.PayloadComponents is null ||
            checkpoint.PlannedLanes is null ||
            checkpoint.PlannedLanes.Length == 0 ||
            checkpoint.AttemptedLanes is null ||
            checkpoint.Lanes is null ||
            checkpoint.Lanes.Any(static lane =>
                lane is null ||
                string.IsNullOrWhiteSpace(lane.Label) ||
                string.IsNullOrWhiteSpace(lane.CheckpointKey) ||
                !PackageVersionUtility.TryNormalizeExact(lane.DefaultVersion, out _) ||
                lane.VersionsByProject is null ||
                lane.VersionsByProject.Any(static entry =>
                    string.IsNullOrWhiteSpace(entry.Key) ||
                    !PackageVersionUtility.TryNormalizeExact(entry.Value, out _))))
        {
            throw new InvalidOperationException(
                $"Coordinated release checkpoint '{path}' is incomplete or invalid. Delete it only if the incomplete release should be abandoned.");
        }

        var planned = ResolvePlannedSynchronizedPublishOperationKeys(plan)
            .OrderBy(static operation => operation, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var storedPlanned = checkpoint.PlannedOperations
            .OrderBy(static operation => operation, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var currentOperations = ResolveSynchronizedReleaseOperationFingerprints(plan);
        var storedOperations = checkpoint.OperationFingerprints
            .OrderBy(static fingerprint => fingerprint, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var currentLanes = ResolveSynchronizedReleaseLaneKeys(plan);
        var storedLanes = checkpoint.PlannedLanes
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var hasExactVersion = PackageVersionUtility.TryNormalizeExact(checkpoint.Version, out _);
        var hasBoundPayload = IsValidSynchronizedReleaseFingerprintState(
                                  checkpoint.SourceFingerprint,
                                  checkpoint.SourceComponents) &&
                              IsValidSynchronizedReleaseFingerprintState(
                                  checkpoint.PayloadFingerprint,
                                  checkpoint.PayloadComponents);
        var hasNoBoundPayload = string.IsNullOrWhiteSpace(checkpoint.SourceFingerprint) &&
                                checkpoint.SourceComponents.Length == 0 &&
                                string.IsNullOrWhiteSpace(checkpoint.PayloadFingerprint) &&
                                checkpoint.PayloadComponents.Length == 0;
        var valid = string.Equals(checkpoint.ModuleName, plan.ModuleName, StringComparison.OrdinalIgnoreCase) &&
                    checkpoint.ReleaseSource == release.VersionSource &&
                    string.Equals(checkpoint.PrimaryProject, NormalizeCheckpointValue(release.PrimaryProject), StringComparison.OrdinalIgnoreCase) &&
                    planned.SequenceEqual(storedPlanned, StringComparer.OrdinalIgnoreCase) &&
                    currentLanes.SequenceEqual(storedLanes, StringComparer.OrdinalIgnoreCase) &&
                    currentOperations.SequenceEqual(storedOperations, StringComparer.OrdinalIgnoreCase) &&
                    checkpoint.AttemptedLanes.All(lane =>
                        storedLanes.Contains(lane, StringComparer.OrdinalIgnoreCase)) &&
                    checkpoint.Lanes.All(lane =>
                        checkpoint.AttemptedLanes.Contains(lane.CheckpointKey, StringComparer.OrdinalIgnoreCase)) &&
                    checkpoint.AttemptedOperations.All(operation =>
                        storedPlanned.Contains(operation, StringComparer.OrdinalIgnoreCase)) &&
                    checkpoint.CompletedOperations.All(operation =>
                        checkpoint.AttemptedOperations.Contains(operation, StringComparer.OrdinalIgnoreCase)) &&
                    (hasBoundPayload ||
                     (hasNoBoundPayload && checkpoint.AttemptedOperations.Length == 0)) &&
                    (hasExactVersion ||
                     (string.IsNullOrWhiteSpace(checkpoint.Version) &&
                      checkpoint.AttemptedOperations.Length == 0 &&
                      checkpoint.CompletedOperations.Length == 0));
        if (!valid)
        {
            throw new InvalidOperationException(
                $"Coordinated release configuration no longer matches incomplete checkpoint '{path}'. Restore the original release configuration or delete the checkpoint only if that release should be abandoned.");
        }
    }

    private static string DescribeCompletedOperations(SynchronizedReleaseCheckpoint checkpoint)
        => checkpoint.CompletedOperations.Length == 0
            ? "none"
            : $"{checkpoint.CompletedOperations.Length} of {checkpoint.PlannedOperations.Length}";

    private static bool IsPristineSynchronizedReleaseCheckpoint(SynchronizedReleaseCheckpoint checkpoint)
        => (checkpoint.PlannedOperations?.Length ?? 0) > 0 &&
           (checkpoint.PlannedLanes?.Length ?? 0) > 0 &&
           (checkpoint.OperationFingerprints?.Length ?? 0) > 0 &&
           string.IsNullOrWhiteSpace(checkpoint.SourceFingerprint) &&
           (checkpoint.SourceComponents?.Length ?? 0) == 0 &&
           string.IsNullOrWhiteSpace(checkpoint.PayloadFingerprint) &&
           (checkpoint.PayloadComponents?.Length ?? 0) == 0 &&
           string.IsNullOrWhiteSpace(checkpoint.Version) &&
           (checkpoint.AttemptedLanes?.Length ?? 0) == 0 &&
           (checkpoint.Lanes?.Length ?? 0) == 0 &&
           (checkpoint.AttemptedOperations?.Length ?? 0) == 0 &&
           (checkpoint.CompletedOperations?.Length ?? 0) == 0;

    private static string? NormalizeCheckpointValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

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
