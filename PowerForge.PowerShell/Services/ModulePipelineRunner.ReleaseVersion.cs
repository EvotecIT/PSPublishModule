using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
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

    private static string ResolveManualReleaseVersion(ReleaseConfiguration release)
    {
        if (string.IsNullOrWhiteSpace(release.Version))
            throw new InvalidOperationException("Release VersionSource Manual requires Version.");

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

}
