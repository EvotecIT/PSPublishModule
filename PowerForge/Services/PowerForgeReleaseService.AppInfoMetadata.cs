using System.Text.Json;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static (AppStoreConnectAppInfoMetadataSpec Spec, string ConfigPath)[] LoadAppleAppInfoSpecs(
        PowerForgeAppleReleasePlan plan)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(plan.AppInfoConfigPath))
            paths.Add(plan.AppInfoConfigPath!);
        paths.AddRange(plan.AppInfoConfigPaths.Where(static path => !string.IsNullOrWhiteSpace(path)));

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var json = File.ReadAllText(path);
                var spec = JsonSerializer.Deserialize<AppStoreConnectAppInfoMetadataSpec>(json, CreateJsonOptions())
                    ?? throw new InvalidOperationException($"Unable to deserialize App Information metadata config: {path}");
                if (string.IsNullOrWhiteSpace(spec.AppId))
                    throw new InvalidOperationException($"App Information metadata config must declare AppId: {path}");
                return (spec, path);
            })
            .ToArray();
    }

    private static AppStoreConnectAppInfoMetadataSpec[] ResolveMatchingAppInfoSpecs(
        (AppStoreConnectAppInfoMetadataSpec Spec, string ConfigPath)[] specs,
        PowerForgeAppleAppReleaseTargetPlan app)
    {
        var matches = specs
            .Where(candidate =>
                string.Equals(candidate.Spec.AppId.Trim(), app.AppStoreConnectAppId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
            throw new InvalidOperationException($"No App Information metadata config matches Apple app '{app.Name}' ({app.AppStoreConnectAppId}).");

        var duplicateLocale = matches
            .GroupBy(candidate => candidate.Spec.Locale?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateLocale is not null)
            throw new InvalidOperationException($"Multiple App Information metadata configs match Apple app '{app.Name}' and locale '{duplicateLocale.Key}'.");

        return matches.Select(static candidate => candidate.Spec).ToArray();
    }

    private static Dictionary<string, AppStoreConnectAppInfoMetadataSpec[]> IndexAppInfoSpecsByAppId(
        (AppStoreConnectAppInfoMetadataSpec Spec, string ConfigPath)[] specs,
        PowerForgeAppleAppReleaseTargetPlan[] apps)
    {
        var indexed = new Dictionary<string, AppStoreConnectAppInfoMetadataSpec[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            var appId = app.AppStoreConnectAppId!;
            if (!indexed.ContainsKey(appId))
                indexed.Add(appId, ResolveMatchingAppInfoSpecs(specs, app));
        }

        return indexed;
    }
}
