namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static void BindAppleShipRoutes(
        PowerForgeReleaseRequest request,
        PowerForgeAppleReleasePlan plan)
    {
        if (request.AppleAction != PowerForgeAppleReleaseAction.Ship)
        {
            if (request.AppleShipTestFlightTargets.Length > 0 ||
                request.AppleShipAppStoreTargets.Length > 0 ||
                !request.AppleShipReuseRemoteScreenshots)
            {
                throw new InvalidOperationException(
                    "Apple Ship target and screenshot options require Apple action 'Ship'.");
            }
            return;
        }

        if (request.Targets.Length > 0)
        {
            throw new InvalidOperationException(
                "Apple action 'Ship' uses --apple-testflight-target and --apple-app-store-target; do not combine it with --target.");
        }

        var testFlight = ResolveAppleShipTargets(
            plan.Apps,
            request.AppleShipTestFlightTargets,
            "internal TestFlight");
        var appStore = ResolveAppleShipTargets(
            plan.Apps,
            request.AppleShipAppStoreTargets,
            "App Store Review");
        if (testFlight.Length == 0 && appStore.Length == 0)
        {
            throw new InvalidOperationException(
                "Apple action 'Ship' requires at least one --apple-testflight-target or --apple-app-store-target.");
        }

        foreach (var app in testFlight)
        {
            if (!UsesTestFlight(app) || app.TestFlightPolicy != AppleTestFlightPolicy.Internal)
            {
                throw new InvalidOperationException(
                    $"Apple Ship internal TestFlight target '{app.Name}' must use AppStore or TestFlightOnly distribution with TestFlightPolicy=Internal.");
            }
            app.ShipToTestFlight = true;
        }

        foreach (var app in appStore)
        {
            if (app.DistributionRoute != AppleDistributionRoute.AppStore)
            {
                throw new InvalidOperationException(
                    $"Apple Ship App Store Review target '{app.Name}' must use DistributionRoute=AppStore.");
            }
            app.ShipToAppStoreReview = true;
        }

        if (!request.AppleShipReuseRemoteScreenshots && appStore.Length == 0)
        {
            throw new InvalidOperationException(
                "Apple Ship screenshot synchronization requires at least one App Store Review target.");
        }

        if (appStore.Length == 0)
        {
            plan.PrepareDistribution = false;
            plan.SelectBuildForDistribution = false;
            plan.SyncMetadata = false;
            plan.SyncAppInfo = false;
            plan.SyncScreenshots = false;
            plan.CheckGovernance = false;
            plan.CheckReleaseReadiness = false;
            plan.SubmitForReview = false;
        }
        if (testFlight.Length == 0)
            plan.DistributeTestFlight = false;

        plan.ShipTestFlightTargets = testFlight
            .Select(static app => app.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        plan.ShipAppStoreTargets = appStore
            .Select(static app => app.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        plan.ShipReuseRemoteScreenshots = request.AppleShipReuseRemoteScreenshots;
    }

    private static PowerForgeAppleAppReleaseTargetPlan[] ResolveAppleShipTargets(
        PowerForgeAppleAppReleaseTargetPlan[] apps,
        IEnumerable<string>? selectors,
        string route)
    {
        var resolved = new List<PowerForgeAppleAppReleaseTargetPlan>();
        foreach (var selector in NormalizeStrings(selectors?.ToArray()))
        {
            var matches = apps
                .Where(IsIndependentReleaseTarget)
                .Where(app =>
                    app.Name.Equals(selector, StringComparison.OrdinalIgnoreCase) ||
                    app.Scheme.Equals(selector, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (matches.Length == 0)
                throw new InvalidOperationException($"Unknown Apple Ship {route} target '{selector}'.");
            if (matches.Length > 1)
            {
                throw new InvalidOperationException(
                    $"Apple Ship {route} target '{selector}' is ambiguous. Use a unique configured target name.");
            }
            if (!resolved.Contains(matches[0]))
                resolved.Add(matches[0]);
        }
        return resolved.ToArray();
    }

    private static bool IsAppleShipTarget(PowerForgeAppleAppReleaseTargetPlan app)
        => app.ShipToTestFlight || app.ShipToAppStoreReview;

    private static bool ShouldRunAppleShipAppStoreStep(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app)
        => plan.Action != PowerForgeAppleReleaseAction.Ship || app.ShipToAppStoreReview;

    private static bool ShouldRunAppleShipTestFlightDistribution(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app)
        => plan.Action != PowerForgeAppleReleaseAction.Ship || app.ShipToTestFlight;

    private PowerForgeAppleVersionReceipt? ResolveApprovedAppleShipVersion(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleVersionReceipt current,
        string requested)
    {
        if (plan.Action != PowerForgeAppleReleaseAction.Ship ||
            string.IsNullOrWhiteSpace(plan.ApprovedPlanSha256))
        {
            return null;
        }

        var approved = _appleReceiptStore.ReadAll(plan)
            .FirstOrDefault(receipt =>
                receipt.Action == PowerForgeAppleReleaseAction.Ship &&
                receipt.ShipPhase == PowerForgeAppleShipPhase.Release &&
                string.Equals(receipt.PlanSha256, plan.ApprovedPlanSha256, StringComparison.OrdinalIgnoreCase) &&
                AppleSourceCommitEvidenceMatches(receipt.SourceCommit, plan.SourceCommit) &&
                receipt.Versioning is not null &&
                string.Equals(receipt.Versioning.MarketingVersion, current.MarketingVersion, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(receipt.Versioning.BuildNumber, current.BuildNumber, StringComparison.OrdinalIgnoreCase));
        if (approved?.Versioning is null)
            return null;

        var versioning = approved.Versioning;
        return new PowerForgeAppleVersionReceipt
        {
            SourcePath = versioning.SourcePath,
            RequestedMarketingVersion = requested,
            MarketingVersionPattern = versioning.MarketingVersionPattern,
            MarketingVersion = versioning.MarketingVersion,
            BuildNumber = versioning.BuildNumber,
            PreviousMarketingVersion = versioning.PreviousMarketingVersion,
            PreviousBuildNumber = versioning.PreviousBuildNumber,
            HighestRemoteBuildNumber = versioning.HighestRemoteBuildNumber,
            HighestRemoteMarketingVersion = versioning.HighestRemoteMarketingVersion,
            ReusedUnreleasedMarketingVersion = versioning.ReusedUnreleasedMarketingVersion,
            Changed = false
        };
    }
}
