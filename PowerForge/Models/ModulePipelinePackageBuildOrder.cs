namespace PowerForge;

/// <summary>
/// Resolves the effective package-lane position shared by execution and progress planning.
/// </summary>
internal static class ModulePipelinePackageBuildOrder
{
    internal static bool ShouldRunBeforeModule(ConfigurationReleaseSegment? release, bool buildBeforeModule)
        => ResolveOverride(release) ?? buildBeforeModule;

    private static bool? ResolveOverride(ConfigurationReleaseSegment? release)
    {
        var order = release?.Configuration?.BuildOrder;
        if (order is null || order.Length == 0)
            return null;

        int? packageIndex = null;
        int? moduleIndex = null;
        for (var index = 0; index < order.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(order[index]))
                continue;

            if (!TryParseLane(order[index], out var lane))
                continue;

            if (lane == ReleaseBuildLane.PackageBuild && packageIndex is null)
                packageIndex = index;
            if (lane == ReleaseBuildLane.Module && moduleIndex is null)
                moduleIndex = index;
        }

        if (packageIndex is null || moduleIndex is null)
            return null;

        return packageIndex.Value < moduleIndex.Value;
    }

    private static bool TryParseLane(string value, out ReleaseBuildLane lane)
    {
        var normalized = value.Trim().Replace("-", string.Empty).Replace("_", string.Empty);
        if (string.Equals(normalized, "PackageBuild", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "PackageBuilds", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "ProjectBuild", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "ProjectBuilds", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Packages", StringComparison.OrdinalIgnoreCase))
        {
            lane = ReleaseBuildLane.PackageBuild;
            return true;
        }

        if (string.Equals(normalized, "Module", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "ModuleBuild", StringComparison.OrdinalIgnoreCase))
        {
            lane = ReleaseBuildLane.Module;
            return true;
        }

        lane = default;
        return false;
    }

    private enum ReleaseBuildLane
    {
        PackageBuild,
        Module
    }
}
