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
        state.PlannedSynchronizedDestinations.Clear();
        foreach (var destination in ResolvePlannedSynchronizedDestinations(plan))
            state.PlannedSynchronizedDestinations.Add(destination);

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

        if (checkpoint is null || checkpoint.SchemaVersion != 1)
        {
            throw new InvalidOperationException(
                $"Coordinated release checkpoint '{path}' has an unsupported schema. Delete it only if the incomplete release should be abandoned.");
        }

        ValidateSynchronizedReleaseCheckpoint(plan, state, checkpoint, path);
        state.SynchronizedReleaseCheckpoint = checkpoint;
        state.IsResumingSynchronizedRelease = true;
        _logger.Warn(
            $"Resuming incomplete coordinated release {checkpoint.Version} from '{path}'. Completed destinations: {DescribeCompletedDestinations(checkpoint)}.");
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

        if (state.SynchronizedReleaseCheckpoint is not null)
        {
            if (!string.Equals(state.SynchronizedReleaseCheckpoint.Version, normalizedVersion, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"The resumed package build resolved version '{normalizedVersion}', but the coordinated release checkpoint requires '{state.SynchronizedReleaseCheckpoint.Version}'.");
            }
            return;
        }

        var release = plan.Release!.Configuration;
        var lanes = state.ReleaseVersionCandidates
            .Where(static candidate => HasCheckpointableReleaseVersions(candidate.Result))
            .Select(CreateSynchronizedReleaseLaneCheckpoint)
            .ToArray();
        if (lanes.Length == 0)
        {
            throw new InvalidOperationException(
                "A coordinated release checkpoint could not be created because no selected release-source result was available.");
        }

        var checkpoint = new SynchronizedReleaseCheckpoint
        {
            ModuleName = plan.ModuleName,
            ReleaseSource = release.VersionSource,
            PrimaryProject = NormalizeCheckpointValue(release.PrimaryProject),
            Version = normalizedVersion,
            PlannedDestinations = state.PlannedSynchronizedDestinations
                .Select(static destination => destination.ToString())
                .OrderBy(static destination => destination, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            OperationFingerprints = ResolveSynchronizedReleaseOperationFingerprints(plan),
            Lanes = lanes,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        state.SynchronizedReleaseCheckpoint = checkpoint;
        state.SynchronizedReleaseCheckpointPath ??= ResolveSynchronizedReleaseCheckpointPath(plan);
        SaveSynchronizedReleaseCheckpoint(state);
        _logger.Info($"Coordinated release checkpoint created at '{state.SynchronizedReleaseCheckpointPath}'.");
    }

    private static void ApplySynchronizedReleaseCheckpointVersion(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        ReleaseVersionSource source,
        string laneLabel,
        string checkpointKey,
        bool useAsReleaseVersionSource,
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
            if (useAsReleaseVersionSource &&
                checkpoint.ReleaseSource == source &&
                plan.Release?.Configuration?.VersionSource == source)
            {
                throw new InvalidOperationException(
                    $"The incomplete coordinated release does not contain version state for selected {source} lane '{laneLabel}'. Delete the checkpoint only if that release should be abandoned.");
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

    private ReleasePublishDestination[] ResolvePlannedSynchronizedDestinations(ModulePipelinePlan plan)
    {
        if (plan.Release?.Configuration?.SynchronizeModuleVersion != true ||
            plan.GateMode is ConfigurationGateMode.Manifest or ConfigurationGateMode.Documentation or ConfigurationGateMode.Build)
        {
            return Array.Empty<ReleasePublishDestination>();
        }

        var planned = new HashSet<ReleasePublishDestination>();
        if ((plan.Publishes ?? Array.Empty<ConfigurationPublishSegment>())
            .Any(static publish => publish.Configuration.Destination == PublishDestination.PowerShellGallery))
        {
            planned.Add(ReleasePublishDestination.PowerShellGallery);
        }
        if ((plan.Publishes ?? Array.Empty<ConfigurationPublishSegment>())
            .Any(static publish => publish.Configuration.Destination == PublishDestination.GitHub))
        {
            planned.Add(ReleasePublishDestination.GitHub);
        }

        foreach (var segment in plan.ProjectBuilds ?? Array.Empty<ConfigurationProjectBuildSegment>())
        {
            if (ShouldExecuteProjectBuildPublish(plan, segment, PackageBuildPublishDestination.NuGet))
                planned.Add(ReleasePublishDestination.NuGet);
            if (ShouldExecuteProjectBuildPublish(plan, segment, PackageBuildPublishDestination.GitHub))
                planned.Add(ReleasePublishDestination.GitHub);
        }

        foreach (var segment in plan.PackageBuilds ?? Array.Empty<ConfigurationPackageBuildSegment>())
        {
            if (ShouldExecutePackageBuildPublish(plan, segment, PackageBuildPublishDestination.NuGet))
                planned.Add(ReleasePublishDestination.NuGet);
            if (ShouldExecutePackageBuildPublish(plan, segment, PackageBuildPublishDestination.GitHub))
                planned.Add(ReleasePublishDestination.GitHub);
        }

        return planned.ToArray();
    }

    private static bool ShouldUseSynchronizedReleaseCheckpoint(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
        => plan.Release?.Configuration?.SynchronizeModuleVersion == true &&
           state.PlannedSynchronizedDestinations.Count > 0;

    private static bool ShouldSkipSynchronizedReleaseDestination(
        ModulePipelineRunState state,
        ReleasePublishDestination destination)
        => state.SynchronizedReleaseCheckpoint is not null &&
           state.SynchronizedReleaseCheckpoint.CompletedDestinations.Contains(
               destination.ToString(),
               StringComparer.OrdinalIgnoreCase);

    private void MarkSynchronizedReleaseDestinationCompleted(
        ModulePipelineRunState state,
        ReleasePublishDestination destination)
    {
        var checkpoint = state.SynchronizedReleaseCheckpoint;
        if (checkpoint is null || !state.PlannedSynchronizedDestinations.Contains(destination))
            return;

        var destinationName = destination.ToString();
        if (checkpoint.CompletedDestinations.Contains(destinationName, StringComparer.OrdinalIgnoreCase))
            return;

        checkpoint.CompletedDestinations = checkpoint.CompletedDestinations
            .Append(destinationName)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SaveSynchronizedReleaseCheckpoint(state);
    }

    private void CompleteSynchronizedReleaseCheckpoint(ModulePipelineRunState state)
    {
        var checkpoint = state.SynchronizedReleaseCheckpoint;
        var path = state.SynchronizedReleaseCheckpointPath;
        if (checkpoint is null || string.IsNullOrWhiteSpace(path))
            return;

        var complete = checkpoint.PlannedDestinations.All(destination =>
            checkpoint.CompletedDestinations.Contains(destination, StringComparer.OrdinalIgnoreCase));
        if (!complete)
            return;

        if (File.Exists(path))
            File.Delete(path);
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
            if (!string.IsNullOrWhiteSpace(entry.Key) && !string.IsNullOrWhiteSpace(entry.Value))
                versions[entry.Key.Trim()] = entry.Value.Trim();
        }
        foreach (var project in release.Projects)
        {
            if (!string.IsNullOrWhiteSpace(project.ProjectName) && !string.IsNullOrWhiteSpace(project.NewVersion))
                versions[project.ProjectName.Trim()] = project.NewVersion!.Trim();
        }

        var defaultVersion = string.IsNullOrWhiteSpace(release.ResolvedVersion)
            ? versions.Values.FirstOrDefault()
            : release.ResolvedVersion;
        if (string.IsNullOrWhiteSpace(defaultVersion))
        {
            throw new InvalidOperationException(
                $"Package lane '{laneLabel}' did not return an exact release version.");
        }

        return new SynchronizedReleaseLaneCheckpoint
        {
            Source = source,
            Label = laneLabel,
            CheckpointKey = checkpointKey,
            DefaultVersion = defaultVersion!.Trim(),
            VersionsByProject = versions
        };
    }

    private static bool HasCheckpointableReleaseVersions(ProjectBuildHostExecutionResult result)
    {
        var release = result.Result.Release;
        return release is not null &&
               (!string.IsNullOrWhiteSpace(release.ResolvedVersion) ||
                release.ResolvedVersionsByProject.Count > 0 ||
                release.Projects.Any(static project => !string.IsNullOrWhiteSpace(project.NewVersion)));
    }

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
        if (checkpoint.PlannedDestinations is null ||
            checkpoint.CompletedDestinations is null ||
            checkpoint.OperationFingerprints is null ||
            checkpoint.OperationFingerprints.Length == 0 ||
            checkpoint.Lanes is null ||
            checkpoint.Lanes.Length == 0 ||
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

        var planned = state.PlannedSynchronizedDestinations
            .Select(static destination => destination.ToString())
            .OrderBy(static destination => destination, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var storedPlanned = checkpoint.PlannedDestinations
            .OrderBy(static destination => destination, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var currentOperations = ResolveSynchronizedReleaseOperationFingerprints(plan);
        var storedOperations = checkpoint.OperationFingerprints
            .OrderBy(static fingerprint => fingerprint, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var valid = string.Equals(checkpoint.ModuleName, plan.ModuleName, StringComparison.OrdinalIgnoreCase) &&
                    checkpoint.ReleaseSource == release.VersionSource &&
                    string.Equals(checkpoint.PrimaryProject, NormalizeCheckpointValue(release.PrimaryProject), StringComparison.OrdinalIgnoreCase) &&
                    planned.SequenceEqual(storedPlanned, StringComparer.OrdinalIgnoreCase) &&
                    currentOperations.SequenceEqual(storedOperations, StringComparer.OrdinalIgnoreCase) &&
                    checkpoint.CompletedDestinations.All(destination =>
                        storedPlanned.Contains(destination, StringComparer.OrdinalIgnoreCase)) &&
                    PackageVersionUtility.TryNormalizeExact(checkpoint.Version, out _);
        if (!valid)
        {
            throw new InvalidOperationException(
                $"Coordinated release configuration no longer matches incomplete checkpoint '{path}'. Restore the original release configuration or delete the checkpoint only if that release should be abandoned.");
        }
    }

    private static string ResolveSynchronizedReleaseCheckpointPath(ModulePipelinePlan plan)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safeModuleName = new string(plan.ModuleName
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        return Path.Combine(
            plan.ProjectRoot,
            "Artefacts",
            ".powerforge",
            "coordinated-release",
            $"{safeModuleName}.json");
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
        var powerForgeDirectory = string.IsNullOrWhiteSpace(releaseDirectory)
            ? null
            : Path.GetDirectoryName(releaseDirectory);
        DeleteDirectoryIfEmpty(releaseDirectory);
        DeleteDirectoryIfEmpty(powerForgeDirectory);
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

    private static string DescribeCompletedDestinations(SynchronizedReleaseCheckpoint checkpoint)
        => checkpoint.CompletedDestinations.Length == 0
            ? "none"
            : string.Join(", ", checkpoint.CompletedDestinations);

    private static string? NormalizeCheckpointValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private sealed class SynchronizedReleaseCheckpoint
    {
        public int SchemaVersion { get; set; } = 1;
        public string ModuleName { get; set; } = string.Empty;
        public ReleaseVersionSource ReleaseSource { get; set; }
        public string? PrimaryProject { get; set; }
        public string Version { get; set; } = string.Empty;
        public string[] PlannedDestinations { get; set; } = Array.Empty<string>();
        public string[] CompletedDestinations { get; set; } = Array.Empty<string>();
        public string[] OperationFingerprints { get; set; } = Array.Empty<string>();
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
