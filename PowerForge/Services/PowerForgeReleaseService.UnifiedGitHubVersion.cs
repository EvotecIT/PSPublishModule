namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    internal static void UpdateResolvedModuleVersion(PowerForgeModuleReleasePlanSummary? plan)
    {
        if (plan is null || string.IsNullOrWhiteSpace(plan.ManifestPath) || !File.Exists(plan.ManifestPath))
            return;

        var moduleVersion = ModuleManifestValueReader.ReadTopLevelString(plan.ManifestPath!, "ModuleVersion");
        if (NormalizeReleaseVersion(plan.ModuleVersion) is null && !string.IsNullOrWhiteSpace(moduleVersion))
            plan.ModuleVersion = moduleVersion;

        if (string.IsNullOrWhiteSpace(plan.PreReleaseTag))
        {
            plan.PreReleaseTag = ModuleManifestValueReader
                .ReadPsDataStringOrArray(plan.ManifestPath!, "Prerelease")
                .FirstOrDefault();
        }
    }

    internal static string? ResolveUnifiedReleaseVersion(
        PowerForgeReleaseGitHubOptions options,
        PowerForgeReleaseResult result,
        string? sharedReleaseVersion)
    {
        var packageVersion = NormalizeReleaseVersion(sharedReleaseVersion);
        var moduleVersion = ResolveModuleReleaseVersion(result.ModulePlan);
        var assetVersion = ResolveUniqueAssetVersion(result.ReleaseAssetEntries);

        return options.VersionSource switch
        {
            PowerForgeReleaseVersionSource.Module => moduleVersion,
            PowerForgeReleaseVersionSource.Packages => packageVersion,
            PowerForgeReleaseVersionSource.Assets => assetVersion,
            _ => packageVersion ?? moduleVersion ?? assetVersion
        };
    }

    private static string? ResolveModuleReleaseVersion(PowerForgeModuleReleasePlanSummary? plan)
    {
        var moduleVersion = NormalizeReleaseVersion(plan?.ModuleVersion);
        if (string.IsNullOrWhiteSpace(moduleVersion))
            return null;

        var preReleaseTag = plan?.PreReleaseTag?.Trim().TrimStart('-');
        if (string.IsNullOrWhiteSpace(preReleaseTag) || moduleVersion!.Contains('-'))
            return moduleVersion;

        return moduleVersion + "-" + preReleaseTag;
    }

    private static string? ResolveUniqueAssetVersion(IEnumerable<PowerForgeReleaseAssetEntry> entries)
    {
        var versions = entries
            .Select(entry => NormalizeReleaseVersion(entry.Version))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return versions.Length == 1 ? versions[0] : null;
    }

    private static string? NormalizeReleaseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var version = value!.Trim();
        return version.IndexOf("X", StringComparison.OrdinalIgnoreCase) >= 0 || version.Contains('*')
            ? null
            : version;
    }
}
