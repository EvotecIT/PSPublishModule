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
                ExpectedVersion = ResolveExpectedVersion(project.ProjectName, expectedGlobal, expectedMap, out _)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.ExpectedVersion) &&
                           !PackageVersionUtility.TryNormalizeExact(item.ExpectedVersion, out _))
            .GroupBy(item => item.ExpectedVersion!.Trim(), StringComparer.OrdinalIgnoreCase);

        foreach (var group in patternGroups)
        {
            Version? highestCurrent = null;
            string? highestPackageId = null;

            foreach (var item in group)
            {
                var packageId = string.IsNullOrWhiteSpace(item.Project.PackageId)
                    ? item.Project.ProjectName
                    : item.Project.PackageId;
                var current = _resolver.ResolveLatest(
                    packageId,
                    spec.VersionSources,
                    spec.VersionSourceCredential,
                    spec.VersionSourceCredentials,
                    spec.IncludePrerelease);

                if (current is not null && (highestCurrent is null || current.CompareTo(highestCurrent) > 0))
                {
                    highestCurrent = current;
                    highestPackageId = packageId;
                }
            }

            var alignedVersion = VersionPatternStepper.Step(group.Key, highestCurrent);
            var members = group.ToArray();
            foreach (var item in members)
                aligned[item.Project.ProjectName] = alignedVersion;

            if (highestCurrent is null)
            {
                _logger.Info(
                    $"Aligned {members.Length} project(s) using pattern '{group.Key}' to {alignedVersion}; " +
                    "no current package version was found, so the X-pattern baseline was used.");
            }
            else
            {
                _logger.Info(
                    $"Aligned {members.Length} project(s) using pattern '{group.Key}' to {alignedVersion}; " +
                    $"highest current package version is {highestCurrent} ({highestPackageId}).");
            }
        }

        return aligned;
    }

    private static string? ResolveExpectedVersion(
        string projectName,
        string? expectedGlobal,
        IReadOnlyDictionary<string, string> expectedMap,
        out string source)
    {
        if (expectedMap.TryGetValue(projectName, out var overrideVersion) && !string.IsNullOrWhiteSpace(overrideVersion))
        {
            source = "per-project";
            return overrideVersion;
        }

        if (string.IsNullOrWhiteSpace(expectedGlobal))
        {
            source = "csproj";
            return null;
        }

        source = "global";
        return expectedGlobal;
    }
}
