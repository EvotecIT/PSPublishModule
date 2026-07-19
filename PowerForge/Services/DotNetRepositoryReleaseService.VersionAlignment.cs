using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

public sealed partial class DotNetRepositoryReleaseService
{
    private Dictionary<string, string> ResolveAlignedPackageVersions(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        string? expectedGlobal,
        IReadOnlyDictionary<string, string> expectedMap,
        DotNetRepositoryReleaseSpec spec)
    {
        var aligned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!spec.AlignPackageVersions || !spec.UpdateVersions)
            return aligned;

        var patternGroups = projects
            .Select(project => new
            {
                Project = project,
                ExpectedVersion = ResolveExpectedVersion(
                    project.ProjectName,
                    expectedGlobal,
                    expectedMap,
                    spec.ExpectedVersionMapUseWildcards,
                    out _)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.ExpectedVersion) &&
                           !PackageVersionUtility.TryNormalizeExact(item.ExpectedVersion, out _))
            .Select(item => CreateAlignmentCandidate(item.Project, item.ExpectedVersion!, spec))
            .GroupBy(item => item.GroupKey, StringComparer.OrdinalIgnoreCase);

        foreach (var group in patternGroups)
        {
            var settings = group.First();
            Version? highestCurrent = null;
            string? highestPackageId = null;

            foreach (var item in group)
            {
                var current = _resolver.ResolveLatest(
                    item.PackageId,
                    settings.VersionSources,
                    spec.VersionSourceCredential,
                    spec.VersionSourceCredentials,
                    settings.IncludePrerelease);

                if (current is not null && (highestCurrent is null || current.CompareTo(highestCurrent) > 0))
                {
                    highestCurrent = current;
                    highestPackageId = item.PackageId;
                }
            }

            var alignedVersion = VersionPatternStepper.Step(settings.ExpectedVersion, highestCurrent);
            var members = group.ToArray();
            var floorMember = members.FirstOrDefault(item => IsReleaseVersionFloorProject(item.Project, spec));
            if (floorMember is not null)
            {
                alignedVersion = ApplyReleaseVersionFloor(
                    floorMember.Project,
                    floorMember.ExpectedVersion,
                    alignedVersion,
                    spec);
            }
            foreach (var item in members)
                aligned[item.Project.ProjectName] = alignedVersion;

            if (highestCurrent is null)
            {
                _logger.Info(
                    $"Aligned {members.Length} project(s) using pattern '{settings.ExpectedVersion}' to {alignedVersion}; " +
                    "no current package version was found, so the X-pattern baseline was used.");
            }
            else
            {
                _logger.Info(
                    $"Aligned {members.Length} project(s) using pattern '{settings.ExpectedVersion}' to {alignedVersion}; " +
                    $"highest current package version is {highestCurrent} ({highestPackageId}).");
            }
        }

        return aligned;
    }

    private void PrepareReleaseVersionFloor(
        IReadOnlyList<DotNetRepositoryProjectResult> projects,
        string? expectedGlobal,
        IReadOnlyDictionary<string, string> expectedMap,
        DotNetRepositoryReleaseSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.ReleaseVersionFloor) &&
            string.IsNullOrWhiteSpace(spec.ReleaseVersionFloorProject))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(spec.ReleaseVersionFloor) ||
            !PackageVersionUtility.TryNormalizeExact(spec.ReleaseVersionFloor, out var normalizedFloor))
        {
            throw new InvalidOperationException(
                $"Release version floor '{spec.ReleaseVersionFloor}' is not a valid exact package version.");
        }
        if (string.IsNullOrWhiteSpace(spec.ReleaseVersionFloorProject))
        {
            throw new InvalidOperationException("Release version floor requires a primary project.");
        }
        if (!spec.UpdateVersions)
        {
            throw new InvalidOperationException("Release version floor requires UpdateVersions to be enabled.");
        }

        var requestedProject = spec.ReleaseVersionFloorProject!.Trim();
        var matches = projects
            .Where(project => string.Equals(project.ProjectName, requestedProject, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
        {
            matches = projects
                .Where(project => string.Equals(project.PackageId, requestedProject, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (matches.Length == 0)
        {
            throw new InvalidOperationException(
                $"Release version floor primary project '{requestedProject}' did not match a packable project name or package id.");
        }
        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"Release version floor primary project '{requestedProject}' matched multiple packable projects. Use an unambiguous project name.");
        }

        spec.ReleaseVersionFloor = normalizedFloor;
        spec.ResolvedReleaseVersionFloorProject = matches[0].ProjectName;
        var expectedVersion = ResolveExpectedVersion(
            matches[0].ProjectName,
            expectedGlobal,
            expectedMap,
            spec.ExpectedVersionMapUseWildcards,
            out _);
        ValidateReleaseVersionFloorExpectedVersion(matches[0], expectedVersion, spec);
    }

    private string ApplyReleaseVersionFloor(
        DotNetRepositoryProjectResult project,
        string? expectedVersion,
        string resolvedVersion,
        DotNetRepositoryReleaseSpec spec)
    {
        if (!IsReleaseVersionFloorProject(project, spec))
        {
            return resolvedVersion;
        }

        ValidateReleaseVersionFloorExpectedVersion(project, expectedVersion, spec);
        var floor = spec.ReleaseVersionFloor!;
        if (PackageVersionUtility.TryNormalizeExact(expectedVersion, out _))
        {
            return resolvedVersion;
        }

        if (!PackageVersionUtility.TryNormalizeExact(resolvedVersion, out var normalizedResolvedVersion))
        {
            throw new InvalidOperationException(
                $"Primary project '{project.ProjectName}' resolved version '{resolvedVersion}' is not a valid exact package version.");
        }
        var resolvedNumericVersion = Version.Parse(PackageVersionUtility.GetNumericVersion(normalizedResolvedVersion));
        var floorNumericVersion = Version.Parse(PackageVersionUtility.GetNumericVersion(floor));
        var numericComparison = resolvedNumericVersion.CompareTo(floorNumericVersion);
        var resolvedPrerelease = PackageVersionUtility.GetPrereleaseVersion(normalizedResolvedVersion);
        var floorPrerelease = PackageVersionUtility.GetPrereleaseVersion(floor);
        var resolvedWinsEqualNumericVersion =
            (!string.IsNullOrWhiteSpace(resolvedPrerelease) || string.IsNullOrWhiteSpace(floorPrerelease)) &&
            PackageVersionUtility.Compare(normalizedResolvedVersion, floor) >= 0;
        if (numericComparison > 0 || (numericComparison == 0 && resolvedWinsEqualNumericVersion))
        {
            return normalizedResolvedVersion;
        }

        _logger.Info(
            $"Primary project '{project.ProjectName}' version adjusted from {normalizedResolvedVersion} to coordinated module floor {floor}.");
        return floor;
    }

    private static void ValidateReleaseVersionFloorExpectedVersion(
        DotNetRepositoryProjectResult project,
        string? expectedVersion,
        DotNetRepositoryReleaseSpec spec)
    {
        var floor = spec.ReleaseVersionFloor!;
        if (string.IsNullOrWhiteSpace(expectedVersion))
        {
            throw new InvalidOperationException(
                $"Primary project '{project.ProjectName}' requires an expected exact version or X-pattern before it can use coordinated version floor '{floor}'.");
        }

        if (PackageVersionUtility.TryNormalizeExact(expectedVersion, out var exactVersion))
        {
            if (PackageVersionUtility.Compare(exactVersion, floor) < 0)
            {
                throw new InvalidOperationException(
                    $"Primary project '{project.ProjectName}' exact version '{exactVersion}' is below coordinated version floor '{floor}'. " +
                    "Raise the exact version or use a compatible X-pattern.");
            }

            return;
        }

        if (!VersionPatternStepper.CanRepresent(expectedVersion!, floor))
        {
            throw new InvalidOperationException(
                $"Primary project '{project.ProjectName}' expected version pattern '{expectedVersion}' cannot represent coordinated version floor '{floor}'. " +
                "Use the same version line for the module and primary package.");
        }
    }

    private static bool IsReleaseVersionFloorProject(
        DotNetRepositoryProjectResult project,
        DotNetRepositoryReleaseSpec spec)
        => !string.IsNullOrWhiteSpace(spec.ResolvedReleaseVersionFloorProject) &&
           string.Equals(
               project.ProjectName,
               spec.ResolvedReleaseVersionFloorProject,
               StringComparison.OrdinalIgnoreCase);

    private static PackageVersionAlignmentCandidate CreateAlignmentCandidate(
        DotNetRepositoryProjectResult project,
        string expectedVersion,
        DotNetRepositoryReleaseSpec spec)
    {
        var configuredGroups = spec.VersionAlignmentGroups;
        if (configuredGroups is not null)
        {
            for (var index = configuredGroups.Count - 1; index >= 0; index--)
            {
                var configured = configuredGroups[index];
                if (!string.Equals(configured.ExpectedVersion, expectedVersion, StringComparison.OrdinalIgnoreCase) ||
                    !configured.Projects.Contains(project.ProjectName, StringComparer.OrdinalIgnoreCase))
                    continue;

                var packageId = string.Equals(configured.AnchorProject, project.ProjectName, StringComparison.OrdinalIgnoreCase) &&
                                !string.IsNullOrWhiteSpace(configured.AnchorPackageId)
                    ? configured.AnchorPackageId!
                    : ResolveAlignmentPackageId(project);

                return new PackageVersionAlignmentCandidate(
                    project,
                    packageId,
                    expectedVersion,
                    "track:" + configured.Name,
                    configured.VersionSources,
                    configured.IncludePrerelease);
            }
        }

        return new PackageVersionAlignmentCandidate(
            project,
            ResolveAlignmentPackageId(project),
            expectedVersion,
            "pattern:" + expectedVersion,
            spec.VersionSources,
            spec.IncludePrerelease);
    }

    private static string ResolveAlignmentPackageId(DotNetRepositoryProjectResult project)
        => string.IsNullOrWhiteSpace(project.PackageId)
            ? project.ProjectName
            : project.PackageId;

    private static string? ResolveExpectedVersion(
        string projectName,
        string? expectedGlobal,
        IReadOnlyDictionary<string, string> expectedMap,
        bool allowWildcards,
        out string source)
    {
        if (expectedMap.TryGetValue(projectName, out var overrideVersion) && !string.IsNullOrWhiteSpace(overrideVersion))
        {
            source = "per-project";
            return overrideVersion;
        }

        if (allowWildcards)
        {
            foreach (var entry in expectedMap)
            {
                if (string.IsNullOrWhiteSpace(entry.Value) || !MatchesPattern(projectName, entry.Key, allowWildcards))
                    continue;

                source = $"per-project wildcard '{entry.Key}'";
                return entry.Value;
            }
        }

        if (string.IsNullOrWhiteSpace(expectedGlobal))
        {
            source = "csproj";
            return null;
        }

        source = "global";
        return expectedGlobal;
    }

    private sealed class PackageVersionAlignmentCandidate
    {
        public PackageVersionAlignmentCandidate(
            DotNetRepositoryProjectResult project,
            string packageId,
            string expectedVersion,
            string groupKey,
            IReadOnlyList<string>? versionSources,
            bool includePrerelease)
        {
            Project = project;
            PackageId = packageId;
            ExpectedVersion = expectedVersion.Trim();
            GroupKey = groupKey;
            VersionSources = versionSources;
            IncludePrerelease = includePrerelease;
        }

        public DotNetRepositoryProjectResult Project { get; }
        public string PackageId { get; }
        public string ExpectedVersion { get; }
        public string GroupKey { get; }
        public IReadOnlyList<string>? VersionSources { get; }
        public bool IncludePrerelease { get; }
    }
}
