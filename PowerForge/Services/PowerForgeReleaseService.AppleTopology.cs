namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private void DiscoverAppStoreConnectAppId(
        PowerForgeAppleAppReleaseTargetPlan app,
        AppStoreConnectApiCredential credential,
        PowerForgeAppleReleaseAction action)
    {
        if (string.IsNullOrWhiteSpace(app.BundleId))
        {
            throw new InvalidOperationException(
                $"Apple action '{action}' cannot discover AppStoreConnectAppId for '{app.Name}' because BundleId is missing.");
        }

        var matches = _findAppleApps(credential, app.BundleId!)
            .Where(candidate => string.Equals(candidate.BundleId, app.BundleId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(static candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();
        if (matches.Length == 0)
        {
            if (action == PowerForgeAppleReleaseAction.Doctor)
                return;
            throw new InvalidOperationException(
                $"App Store Connect has no app record for bundle id '{app.BundleId}' used by '{app.Name}'. " +
                "Create the app record in the App Store Connect website, then rerun this action; the API cannot create apps.");
        }
        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"App Store Connect returned multiple app records for bundle id '{app.BundleId}' used by '{app.Name}': " +
                string.Join(", ", matches.Select(static candidate => candidate.Id)) +
                ". Configure AppStoreConnectAppId explicitly.");
        }

        app.AppStoreConnectAppId = matches[0].Id;
        app.AppStoreConnectAppIdDiscovered = true;
    }

    private static void ValidateAppleProductTopology(PowerForgeAppleAppReleaseTargetPlan[] apps)
    {
        var names = apps.Select(static app => app.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var app in apps)
        {
            var embedded = app.DistributionRoute == AppleDistributionRoute.EmbeddedCompanion;
            if (embedded && string.IsNullOrWhiteSpace(app.ParentTarget))
                throw new InvalidOperationException($"Embedded Apple target '{app.Name}' requires ParentTarget.");
            if (!string.IsNullOrWhiteSpace(app.ParentTarget) && !names.Contains(app.ParentTarget!))
                throw new InvalidOperationException($"Apple target '{app.Name}' references unknown ParentTarget '{app.ParentTarget}'.");
            if (string.Equals(app.Name, app.ParentTarget, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Apple target '{app.Name}' cannot be its own ParentTarget.");
            if (app.DistributionRoute == AppleDistributionRoute.DirectNotarized && app.Platform != ApplePlatform.macOS)
                throw new InvalidOperationException($"Direct-notarized Apple target '{app.Name}' must use Platform macOS.");
            if (app.DistributionRoute == AppleDistributionRoute.TestFlightOnly &&
                app.TestFlightPolicy == AppleTestFlightPolicy.Disabled)
            {
                throw new InvalidOperationException(
                    $"TestFlight-only Apple target '{app.Name}' cannot disable TestFlight.");
            }
        }
    }
}
