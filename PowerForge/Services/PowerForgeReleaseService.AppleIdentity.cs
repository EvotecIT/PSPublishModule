namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private bool PrepareAppleProjectAndReleaseIdentity(
        PowerForgeAppleReleasePlan plan,
        PowerForgeAppleAppReleaseTargetPlan app)
    {
        if (plan.Action == PowerForgeAppleReleaseAction.Cleanup)
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
