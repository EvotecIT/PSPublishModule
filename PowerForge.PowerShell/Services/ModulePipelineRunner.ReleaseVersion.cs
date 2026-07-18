using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static void ValidateSynchronizedModuleVersionConfiguration(
        ConfigurationReleaseSegment? releaseSegment,
        IReadOnlyList<ConfigurationProjectBuildSegment> projectBuilds,
        IReadOnlyList<ConfigurationPackageBuildSegment> packageBuilds,
        ConfigurationGateMode? gateMode)
    {
        var release = releaseSegment?.Configuration;
        if (release?.SynchronizeModuleVersion != true)
        {
            return;
        }

        if (release.VersionSource != ReleaseVersionSource.ProjectBuild &&
            release.VersionSource != ReleaseVersionSource.PackageBuild)
        {
            throw new InvalidOperationException(
                "SynchronizeModuleVersion requires Release VersionSource ProjectBuild or PackageBuild.");
        }

        var sourceLanes = release.VersionSource == ReleaseVersionSource.ProjectBuild
            ? projectBuilds.Select(static segment => new SynchronizedReleaseLane(
                segment.Configuration.Enabled,
                segment.Configuration.BuildBeforeModule,
                segment.Configuration.UseAsReleaseVersionSource)).ToArray()
            : packageBuilds.Select(static segment => new SynchronizedReleaseLane(
                segment.Configuration.Enabled,
                segment.Configuration.BuildBeforeModule,
                segment.Configuration.UseAsReleaseVersionSource)).ToArray();
        var explicitLanes = sourceLanes.Where(static lane => lane.UseAsReleaseVersionSource).ToArray();
        if (explicitLanes.Length == 0)
        {
            throw new InvalidOperationException(
                $"SynchronizeModuleVersion requires a {release.VersionSource} lane marked with UseAsReleaseVersionSource.");
        }
        if (explicitLanes.Any(lane =>
                !ShouldRunPackageBuildBeforeModule(releaseSegment, lane.BuildBeforeModule)))
        {
            throw new InvalidOperationException(
                "SynchronizeModuleVersion requires every selected release source lane to run before the module build. Set BuildBeforeModule or configure Release BuildOrder accordingly.");
        }

        var gateEnablesLanes = gateMode is ConfigurationGateMode.Build or ConfigurationGateMode.Publish;
        if (!gateEnablesLanes &&
            gateMode is not ConfigurationGateMode.Manifest and not ConfigurationGateMode.Documentation &&
            !explicitLanes.Any(static lane => lane.Enabled))
        {
            throw new InvalidOperationException(
                "SynchronizeModuleVersion requires an enabled release source lane when no Build or Publish gate is active.");
        }
    }

    private static bool ShouldSynchronizeModuleVersionForRun(
        ConfigurationReleaseSegment? release,
        ConfigurationGateMode? gateMode)
        => release?.Configuration?.SynchronizeModuleVersion == true &&
           gateMode is not ConfigurationGateMode.Manifest and not ConfigurationGateMode.Documentation;

    private static string ResolveProvisionalSynchronizedModuleVersion(string expectedVersion)
    {
        if (Version.TryParse(expectedVersion, out _))
        {
            return expectedVersion;
        }

        return VersionPatternStepper.Step(expectedVersion, currentVersion: null);
    }

    private void RegisterReleaseVersionCandidate(
        ModulePipelineRunState state,
        ReleaseVersionSource source,
        string label,
        bool explicitSource,
        ProjectBuildHostExecutionResult result)
    {
        state.ReleaseVersionCandidates.Add(new ReleaseVersionCandidate(
            source,
            string.IsNullOrWhiteSpace(label) ? source.ToString() : label.Trim(),
            explicitSource,
            result));
    }

    private static string? ResolveRequestedPackageReleaseVersion(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        var release = plan.Release?.Configuration;
        if (release is not null)
        {
            return release.VersionSource switch
            {
                ReleaseVersionSource.Module => ResolveCandidateVersion(
                    state.ReleaseVersionCandidates,
                    source: null,
                    primaryProject: release.PrimaryProject,
                    explicitOnly: true,
                    required: false) ?? ModulePathTokenFormatter.FormatVersionWithPreRelease(plan.ResolvedVersion, plan.PreRelease),
                ReleaseVersionSource.Manual => ResolveManualReleaseVersion(release),
                ReleaseVersionSource.ProjectBuild => ResolveCandidateVersion(
                    state.ReleaseVersionCandidates,
                    ReleaseVersionSource.ProjectBuild,
                    release.PrimaryProject,
                    explicitOnly: false,
                    required: true),
                ReleaseVersionSource.PackageBuild => ResolveCandidateVersion(
                    state.ReleaseVersionCandidates,
                    ReleaseVersionSource.PackageBuild,
                    release.PrimaryProject,
                    explicitOnly: false,
                    required: true),
                _ => null
            };
        }

        return ResolveCandidateVersion(
            state.ReleaseVersionCandidates,
            source: null,
            primaryProject: null,
            explicitOnly: true,
            required: false);
    }

    private static void ValidateRequestedReleaseVersion(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        if (plan.Release?.Configuration is null)
            return;

        _ = ResolveRequestedPackageReleaseVersion(plan, state);
    }

    private void SynchronizeModuleVersionFromReleaseSource(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        var releaseSegment = plan.Release;
        if (!ShouldSynchronizeModuleVersionForRun(releaseSegment, plan.GateMode))
        {
            return;
        }
        var release = releaseSegment!.Configuration;

        if (release.VersionSource != ReleaseVersionSource.ProjectBuild &&
            release.VersionSource != ReleaseVersionSource.PackageBuild)
        {
            throw new InvalidOperationException(
                "SynchronizeModuleVersion requires Release VersionSource ProjectBuild or PackageBuild.");
        }

        var releaseVersion = ResolveCandidateVersion(
            state.ReleaseVersionCandidates,
            release.VersionSource,
            release.PrimaryProject,
            explicitOnly: true,
            required: true)!;

        if (releaseVersion.IndexOf('+') >= 0)
        {
            throw new InvalidOperationException(
                $"Release version '{releaseVersion}' contains build metadata, which cannot be represented by a PowerShell module version.");
        }

        if (!PackageVersionUtility.TryNormalizeExact(releaseVersion, out var normalizedVersion))
        {
            throw new InvalidOperationException(
                $"Release version '{releaseVersion}' is not a valid exact package version and cannot be used as the module version.");
        }

        plan.ResolvedVersion = PackageVersionUtility.GetNumericVersion(normalizedVersion);
        var preRelease = PackageVersionUtility.GetPrereleaseVersion(normalizedVersion);
        plan.PreRelease = string.IsNullOrWhiteSpace(preRelease) ? null : preRelease;
        plan.BuildSpec.Version = plan.ResolvedVersion;

        _logger.Info(
            $"Module version synchronized to release source '{release.VersionSource}': {normalizedVersion}.");
    }

    private static string ResolveManualReleaseVersion(ReleaseConfiguration release)
    {
        if (string.IsNullOrWhiteSpace(release.Version))
        {
            throw new InvalidOperationException("Release VersionSource Manual requires Version.");
        }

        return release.Version!.Trim();
    }

    private static string? ResolveCandidateVersion(
        IReadOnlyList<ReleaseVersionCandidate> candidates,
        ReleaseVersionSource? source,
        string? primaryProject,
        bool explicitOnly,
        bool required)
    {
        var filtered = candidates
            .Where(candidate => source is null || candidate.Source == source.Value)
            .Where(candidate => !explicitOnly || candidate.ExplicitSource)
            .ToArray();

        if (filtered.Length == 0 && !required)
            return null;
        if (filtered.Length == 0)
            throw new InvalidOperationException(BuildMissingReleaseVersionMessage(source, primaryProject, explicitOnly));

        var versions = filtered
            .Select(candidate => new
            {
                Candidate = candidate,
                Version = ResolveVersionFromProjectBuildResult(candidate.Result, primaryProject)
            })
            .Where(static item => !string.IsNullOrWhiteSpace(item.Version))
            .ToArray();

        if (versions.Length == 0)
            throw new InvalidOperationException(BuildMissingReleaseVersionMessage(source, primaryProject, explicitOnly));

        var distinctVersions = versions
            .Select(static item => item.Version!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctVersions.Length > 1)
        {
            var primaryText = string.IsNullOrWhiteSpace(primaryProject)
                ? string.Empty
                : $" for PrimaryProject '{primaryProject}'";
            throw new InvalidOperationException(
                $"Release version source resolved multiple versions{primaryText} ({string.Join(", ", distinctVersions)}). Configure a single package/project lane with UseAsReleaseVersionSource or align the selected release versions.");
        }

        return distinctVersions[0];
    }

    private static string? ResolveVersionFromProjectBuildResult(ProjectBuildHostExecutionResult result, string? primaryProject)
    {
        var release = result.Result?.Release;
        if (release is null)
            return null;

        if (!string.IsNullOrWhiteSpace(primaryProject))
        {
            var primary = primaryProject!.Trim();
            if (release.ResolvedVersionsByProject.TryGetValue(primary, out var mapped) &&
                !string.IsNullOrWhiteSpace(mapped))
            {
                return mapped.Trim();
            }

            var project = release.Projects.FirstOrDefault(candidate =>
                string.Equals(candidate.ProjectName, primary, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.PackageId, primary, StringComparison.OrdinalIgnoreCase));

            return NormalizeProjectVersion(project);
        }

        if (!string.IsNullOrWhiteSpace(release.ResolvedVersion))
            return release.ResolvedVersion!.Trim();

        var projectVersions = release.Projects
            .Select(NormalizeProjectVersion)
            .Where(static version => !string.IsNullOrWhiteSpace(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return projectVersions.Length == 1 ? projectVersions[0] : null;
    }

    private static string? NormalizeProjectVersion(DotNetRepositoryProjectResult? project)
    {
        if (project is null)
            return null;

        if (!string.IsNullOrWhiteSpace(project.NewVersion))
            return project.NewVersion!.Trim();
        if (!string.IsNullOrWhiteSpace(project.OldVersion))
            return project.OldVersion!.Trim();

        return null;
    }

    private static string BuildMissingReleaseVersionMessage(
        ReleaseVersionSource? source,
        string? primaryProject,
        bool explicitOnly)
    {
        var sourceText = source?.ToString() ?? "explicit package/project build";
        var primaryText = string.IsNullOrWhiteSpace(primaryProject)
            ? string.Empty
            : $" for PrimaryProject '{primaryProject}'";
        var explicitText = explicitOnly ? " marked with UseAsReleaseVersionSource" : string.Empty;

        return $"Release version source '{sourceText}'{primaryText}{explicitText} did not produce a resolved version.";
    }

    private readonly struct SynchronizedReleaseLane
    {
        public SynchronizedReleaseLane(
            bool enabled,
            bool buildBeforeModule,
            bool useAsReleaseVersionSource)
        {
            Enabled = enabled;
            BuildBeforeModule = buildBeforeModule;
            UseAsReleaseVersionSource = useAsReleaseVersionSource;
        }

        public bool Enabled { get; }
        public bool BuildBeforeModule { get; }
        public bool UseAsReleaseVersionSource { get; }
    }

}
