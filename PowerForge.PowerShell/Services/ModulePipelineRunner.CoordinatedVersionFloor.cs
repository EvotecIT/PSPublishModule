using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private string? ResolveCoordinatedVersionFloor(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        ReleaseVersionSource source,
        string checkpointKey,
        bool useAsReleaseVersionSource)
    {
        var release = plan.Release?.Configuration;
        if (!useAsReleaseVersionSource ||
            !ShouldSynchronizeModuleVersionForRun(plan.Release, plan.GateMode) ||
            release?.VersionSource != source)
        {
            return null;
        }

        var checkpointLane = state.SynchronizedReleaseCheckpoint?.Lanes.FirstOrDefault(candidate =>
            candidate.Source == source &&
            string.Equals(candidate.CheckpointKey, checkpointKey, StringComparison.OrdinalIgnoreCase));
        if (checkpointLane is not null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(state.CoordinatedVersionFloor))
        {
            return state.CoordinatedVersionFloor;
        }

        var localManifestPath = plan.UseLocalVersioning
            ? Path.Combine(plan.ProjectRoot, $"{plan.ModuleName}.psd1")
            : null;
        var step = new ModuleVersionStepper(_logger).Step(
            plan.ExpectedVersion,
            plan.ModuleName,
            localPsd1Path: localManifestPath,
            prerelease: !string.IsNullOrWhiteSpace(plan.PreRelease));
        var candidate = ModulePathTokenFormatter.FormatVersionWithPreRelease(
            step.Version,
            plan.PreRelease);
        if (!PackageVersionUtility.TryNormalizeExact(candidate, out var normalizedFloor))
        {
            throw new InvalidOperationException(
                $"Coordinated module version floor '{candidate}' is not a valid exact package version.");
        }

        state.CoordinatedVersionFloor = normalizedFloor;
        _logger.Info(
            $"Coordinated release version floor resolved from module '{plan.ModuleName}': {normalizedFloor}. " +
            $"Primary project '{release.PrimaryProject}' may resolve a higher package version.");
        return normalizedFloor;
    }
}
