using System.Text.Json;

namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private (AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)[] ResolveSelectedAppleScreenshotSpecs(
        PowerForgeAppleReleasePlan plan,
        (AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)[]? configured = null)
    {
        if (plan.Action == PowerForgeAppleReleaseAction.Ship &&
            plan.ShipPhase == PowerForgeAppleShipPhase.VersionCheckpoint)
            return Array.Empty<(AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)>();

        if (!plan.SyncScreenshots && !plan.CheckReleaseReadiness &&
            (!plan.SubmitForReview || plan.SkipReviewReadinessCheck))
            return Array.Empty<(AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)>();

        configured ??= LoadAppleScreenshotSpecs(plan);
        if (configured.Length == 0)
            return Array.Empty<(AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)>();

        var selected = new List<(AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)>();
        foreach (var app in plan.Apps.Where(app =>
                     app.DistributionRoute == AppleDistributionRoute.AppStore &&
                     ShouldRunAppleShipAppStoreStep(plan, app)))
        {
            var version = ResolveAppleDistributionValues(app, versionUpdate: null).MarketingVersion;
            var match = ResolveMatchingScreenshotSpecs(
                configured,
                app,
                version,
                required: plan.SyncScreenshots || configured.Length > 0);
            selected.AddRange(match);
        }

        var comparer = FrameworkCompatibility.GetPathStringComparisonForPath(plan.ProjectRoot) == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return selected
            .GroupBy(static value => value.ConfigPath, comparer)
            .Select(static group => group.First())
            .ToArray();
    }

    private static (AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)[] LoadAppleScreenshotSpecs(PowerForgeAppleReleasePlan plan)
    {
        var paths = new List<string>();
        if (!string.IsNullOrWhiteSpace(plan.ScreenshotConfigPath))
            paths.Add(plan.ScreenshotConfigPath!);
        paths.AddRange(plan.ScreenshotConfigPaths.Where(static path => !string.IsNullOrWhiteSpace(path)));

        return paths
            .Distinct(FrameworkCompatibility.GetPathStringComparisonForPath(plan.ProjectRoot) == StringComparison.OrdinalIgnoreCase
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
            .Select(path =>
            {
                var json = ReadApprovedMutationInputText(plan, path);
                var spec = JsonSerializer.Deserialize<AppStoreConnectScreenshotSyncSpec>(json, CreateJsonOptions())
                    ?? throw new InvalidOperationException($"Unable to deserialize screenshot sync config: {path}");
                return (spec, path);
            })
            .ToArray();
    }

    private static void ValidateAppleScreenshotPreflight(
        (AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath) configured,
        string? expectedSourceCommit)
    {
        var baseDirectory = Path.GetDirectoryName(configured.ConfigPath) ?? Directory.GetCurrentDirectory();
        var validation = new AppStoreConnectScreenshotSyncConfigValidator()
            .Validate(configured.Spec, baseDirectory, expectedSourceCommit: expectedSourceCommit);
        if (validation.IsValid)
            return;

        var messages = validation.Messages
            .Concat(validation.ScreenshotSets.SelectMany(static set => set.Messages))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        throw new InvalidOperationException(
            $"Screenshot preflight failed for '{configured.ConfigPath}': {string.Join(" ", messages)}");
    }

    private static (AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)[] ResolveMatchingScreenshotSpecs(
        (AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)[] specs,
        PowerForgeAppleAppReleaseTargetPlan app,
        string marketingVersion,
        bool required = false)
    {
        var matches = specs
            .Where(candidate =>
                ScreenshotSpecMatches(candidate.Spec, app, marketingVersion))
            .ToArray();
        AppStoreConnectReleasePreparationService.ValidateScreenshotLocales(matches.Select(static match => match.Spec));
        if (matches.Length == 0 && required)
        {
            throw new InvalidOperationException(
                $"No screenshot sync config matches Apple app '{app.Name}' " +
                $"(AppStoreConnectAppId '{app.AppStoreConnectAppId}', platform '{app.Platform}', version '{marketingVersion}').");
        }

        return matches;
    }

    private static bool ScreenshotSpecMatches(
        AppStoreConnectScreenshotSyncSpec spec,
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

    private static AppStoreConnectScreenshotSyncSpec BindScreenshotSpec(
        AppStoreConnectScreenshotSyncSpec source,
        PowerForgeAppleAppReleaseTargetPlan app,
        string marketingVersion)
        => new()
        {
            AppId = app.AppStoreConnectAppId!,
            VersionString = marketingVersion,
            VersionId = null,
            UseReleaseVersion = false,
            Platform = app.Platform,
            Locale = source.Locale.Trim(),
            ScreenshotSets = source.ScreenshotSets,
            Quality = source.Quality
        };

}
