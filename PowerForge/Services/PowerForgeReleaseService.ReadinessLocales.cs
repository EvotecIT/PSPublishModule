namespace PowerForge;

internal sealed partial class PowerForgeReleaseService
{
    private static bool RequiresAppleMetadataSpecs(PowerForgeAppleReleasePlan plan)
        => plan.SyncMetadata || plan.CheckReleaseReadiness || (plan.SubmitForReview && !plan.SkipReviewReadinessCheck);

    private static AppStoreConnectReleaseReadinessRequest CreateAppleReadinessRequest(
        (AppStoreConnectVersionMetadataSpec Spec, string ConfigPath)[] metadata,
        (AppStoreConnectScreenshotSyncSpec Spec, string ConfigPath)[] screenshots)
    {
        return new AppStoreConnectReleaseReadinessRequest
        {
            Locale = screenshots.FirstOrDefault().Spec?.Locale ?? metadata.FirstOrDefault().Spec?.Locale ?? "en-US",
            MetadataLocales = AppStoreConnectReleaseReadinessService.NormalizeMetadataLocales(metadata.Select(static value => value.Spec.Locale)),
            ScreenshotSpec = screenshots.Length == 1 ? screenshots[0].Spec : null,
            ScreenshotSpecs = screenshots.Length > 1
                ? screenshots.Select(static value => value.Spec).ToArray()
                : Array.Empty<AppStoreConnectScreenshotSyncSpec>()
        };
    }
}
