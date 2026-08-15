namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static string ComputeApplePlanSha256(PowerForgeAppleReleaseReceipt receipt)
    {
        var targets = receipt.Action == PowerForgeAppleReleaseAction.Ship
            ? receipt.Targets.Select(CreateAppleShipIntentHashTarget).ToArray()
            : receipt.Targets.Cast<object>().ToArray();
        var versioning = receipt.Action == PowerForgeAppleReleaseAction.Ship && receipt.Versioning is not null
            ? CreateAppleShipIntentHashVersion(receipt.Versioning)
            : receipt.Versioning;
        var canonical = new
        {
            receipt.SchemaVersion,
            receipt.Action,
            receipt.SourceCommit,
            receipt.PlanOnly,
            receipt.AdoptExistingBuild,
            receipt.ShipPhase,
            receipt.MutationInputsSha256,
            MutationInputFiles = receipt.MutationInputFiles
                .OrderBy(static value => value.Key, StringComparer.Ordinal)
                .ToArray(),
            receipt.Success,
            receipt.ErrorMessage,
            Versioning = versioning,
            Targets = targets,
            receipt.Cleanup,
            receipt.Diagnostics,
            receipt.NextActions
        };
        return ComputeStableSha256(canonical);
    }

    private static object CreateAppleShipIntentHashVersion(PowerForgeAppleVersionReceipt versioning)
        => new
        {
            versioning.SourcePath,
            versioning.RequestedMarketingVersion,
            versioning.MarketingVersionPattern,
            versioning.MarketingVersion,
            versioning.BuildNumber,
            versioning.PreviousMarketingVersion,
            versioning.PreviousBuildNumber,
            versioning.ReusedUnreleasedMarketingVersion,
            versioning.Changed
        };

    private static object CreateAppleShipIntentHashTarget(PowerForgeAppleReleaseTargetReceipt target)
        => new
        {
            target.Name,
            target.BundleId,
            target.Platform,
            target.Configuration,
            target.ProjectPath,
            target.IsWorkspace,
            target.Scheme,
            target.ArchiveVariant,
            target.Destination,
            target.DistributionRoute,
            target.ProductRole,
            target.ParentTarget,
            Capabilities = target.Capabilities.OrderBy(static value => value, StringComparer.Ordinal).ToArray(),
            target.TestFlightPolicy,
            target.ShipToTestFlight,
            target.ShipToAppStoreReview,
            target.AppId,
            target.Version,
            target.Build
        };
}
