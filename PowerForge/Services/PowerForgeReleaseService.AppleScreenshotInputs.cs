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
            var match = ResolveMatchingScreenshotSpec(
                configured,
                app,
                version,
                required: plan.SyncScreenshots || configured.Length > 0);
            if (match is not null)
                selected.Add(match.Value);
        }

        var comparer = FrameworkCompatibility.GetPathStringComparisonForPath(plan.ProjectRoot) == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        return selected
            .GroupBy(static value => value.ConfigPath, comparer)
            .Select(static group => group.First())
            .ToArray();
    }

}
