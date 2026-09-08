using System.Text.Json;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static (AppStoreConnectVersionMetadataSpec Spec, string ConfigPath)[] LoadAppleMetadataSpecs(PowerForgeAppleReleasePlan plan)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(plan.MetadataConfigPath))
            paths.Add(plan.MetadataConfigPath!);
        paths.AddRange(plan.MetadataConfigPaths.Where(static path => !string.IsNullOrWhiteSpace(path)));

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var json = ReadApprovedMutationInputText(plan, path);
                var spec = JsonSerializer.Deserialize<AppStoreConnectVersionMetadataSpec>(json, CreateJsonOptions())
                    ?? throw new InvalidOperationException($"Unable to deserialize App Store version metadata config: {path}");
                return (spec, path);
            })
            .ToArray();
    }

    private static void ValidateAppleMetadataPreflight(
        (AppStoreConnectVersionMetadataSpec Spec, string ConfigPath) configured)
    {
        if (string.IsNullOrWhiteSpace(configured.Spec.Locale))
        {
            throw new InvalidOperationException(
                $"App Store version metadata config must declare Locale: {configured.ConfigPath}");
        }
        if (configured.Spec.Metadata is null)
        {
            throw new InvalidOperationException(
                $"App Store version metadata config must declare a Metadata object: {configured.ConfigPath}");
        }
    }

    private static (AppStoreConnectVersionMetadataSpec Spec, string ConfigPath)[] ResolveMatchingMetadataSpecs(
        (AppStoreConnectVersionMetadataSpec Spec, string ConfigPath)[] specs,
        PowerForgeAppleAppReleaseTargetPlan app,
        string marketingVersion,
        bool required = false)
    {
        var matches = specs
            .Where(candidate => MetadataSpecMatches(candidate.Spec, app, marketingVersion))
            .ToArray();
        var duplicateLocale = matches.GroupBy(candidate => candidate.Spec.Locale?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateLocale is not null)
            throw new InvalidOperationException($"Multiple App Store metadata configs match Apple app '{app.Name}' version '{marketingVersion}' platform '{app.Platform}' locale '{duplicateLocale.Key}'.");
        if (matches.Length == 0 && required)
        {
            throw new InvalidOperationException(
                $"No App Store metadata config matches Apple app '{app.Name}' " +
                $"(AppStoreConnectAppId '{app.AppStoreConnectAppId}', platform '{app.Platform}', version '{marketingVersion}').");
        }

        return matches;
    }

    private static bool MetadataSpecMatches(
        AppStoreConnectVersionMetadataSpec spec,
        PowerForgeAppleAppReleaseTargetPlan app,
        string marketingVersion)
    {
        var appIdMatches = string.IsNullOrWhiteSpace(spec.AppId) ||
                           string.Equals(spec.AppId.Trim(), app.AppStoreConnectAppId, StringComparison.OrdinalIgnoreCase);
        var specVersionString = string.IsNullOrWhiteSpace(spec.VersionString) ? null : spec.VersionString!.Trim();
        var versionMatches = (spec.UseReleaseVersion && specVersionString is null) ||
                             string.Equals(specVersionString, marketingVersion, StringComparison.OrdinalIgnoreCase);
        return appIdMatches && versionMatches && spec.Platform == app.Platform;
    }
}
