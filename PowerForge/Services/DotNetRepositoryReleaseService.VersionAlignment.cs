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
                var packageId = string.IsNullOrWhiteSpace(item.Project.PackageId)
                    ? item.Project.ProjectName
                    : item.Project.PackageId;
                var current = _resolver.ResolveLatest(
                    packageId,
                    settings.VersionSources,
                    spec.VersionSourceCredential,
                    spec.VersionSourceCredentials,
                    settings.IncludePrerelease);

                if (current is not null && (highestCurrent is null || current.CompareTo(highestCurrent) > 0))
                {
                    highestCurrent = current;
                    highestPackageId = packageId;
                }
            }

            var alignedVersion = VersionPatternStepper.Step(settings.ExpectedVersion, highestCurrent);
            var members = group.ToArray();
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

                return new PackageVersionAlignmentCandidate(
                    project,
                    expectedVersion,
                    "track:" + configured.Name,
                    configured.VersionSources,
                    configured.IncludePrerelease);
            }
        }

        return new PackageVersionAlignmentCandidate(
            project,
            expectedVersion,
            "pattern:" + expectedVersion,
            spec.VersionSources,
            spec.IncludePrerelease);
    }

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
            string expectedVersion,
            string groupKey,
            IReadOnlyList<string>? versionSources,
            bool includePrerelease)
        {
            Project = project;
            ExpectedVersion = expectedVersion.Trim();
            GroupKey = groupKey;
            VersionSources = versionSources;
            IncludePrerelease = includePrerelease;
        }

        public DotNetRepositoryProjectResult Project { get; }
        public string ExpectedVersion { get; }
        public string GroupKey { get; }
        public IReadOnlyList<string>? VersionSources { get; }
        public bool IncludePrerelease { get; }
    }
}
