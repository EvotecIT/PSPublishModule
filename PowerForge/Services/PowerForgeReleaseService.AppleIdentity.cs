namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static (string? MarketingVersion, string? BuildNumber) ResolveAppleArchiveValidationValues(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app,
        XcodeProjectVersionUpdateResult? versionUpdate)
    {
        if (plan.ManageAppVersionAndBuildNumber)
            return (null, null);

        // Local workspace rehearsals can leave versions to Xcode build settings.
        // Delivery still requires a resolved release identity during preflight.
        if (plan.Rehearse && app.IsWorkspace)
            return ((versionUpdate?.After.MarketingVersion ?? app.MarketingVersion)?.Trim(),
                (versionUpdate?.After.BuildNumber ?? app.BuildNumber)?.Trim());

        return ResolveAppleDistributionValues(app, versionUpdate);
    }

    private bool PrepareAppleProjectAndReleaseIdentity(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app)
    {
        if (plan.Action == PowerForgeAppleReleaseAction.Cleanup || IsExplicitAppleVersionObservation(plan))
            return false;

        var generated = _generateAppleProject(app);
        if (app.VersionUpdateRequested &&
            app.BuildNumberPolicy == AppleBuildNumberPolicy.IncrementExisting &&
            string.IsNullOrWhiteSpace(app.BuildNumber))
        {
            app.BuildNumber = ResolveAppleBuildNumber(
                new AppleAppConfiguration
                {
                    BuildNumberPolicy = app.BuildNumberPolicy
                },
                app.ProjectPath,
                new XcodeProjectVersionEditor());
        }

        if (RequiresAppleReleaseIdentity(plan))
        {
            var values = ResolveAppleDistributionValues(app, versionUpdate: null);
            app.MarketingVersion = values.MarketingVersion;
            app.BuildNumber = values.BuildNumber;
        }
        return generated;
    }

    private void ResolveAppleValidationTargetIdentities(PowerForgeAppleReleasePlan plan)
    {
        if (!RequiresAppleReleaseIdentity(plan))
            return;

        foreach (var app in plan.Apps.Where(app => ShouldExecuteAppleTarget(plan.Action, app)))
            _ = PrepareAppleProjectAndReleaseIdentity(plan, app);
    }
}
