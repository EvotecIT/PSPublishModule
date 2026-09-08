namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(nameof(AppStoreConnectVersionLocalizationInfo.Description))]
    [InlineData(nameof(AppStoreConnectVersionLocalizationInfo.Keywords))]
    [InlineData(nameof(AppStoreConnectVersionLocalizationInfo.SupportUrl))]
    [InlineData(nameof(AppStoreConnectVersionLocalizationInfo.MarketingUrl))]
    [InlineData(nameof(AppStoreConnectVersionLocalizationInfo.PromotionalText))]
    [InlineData(nameof(AppStoreConnectVersionLocalizationInfo.WhatsNew))]
    public void Execute_AppleProtectedMutationRejectsSecondaryLocaleChangedAfterPlanApproval(string field)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "CasaRay.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var localizedMetadata = new AppStoreConnectVersionLocalizationInfo { Locale = "pl" };
            var property = typeof(AppStoreConnectVersionLocalizationInfo).GetProperty(field)!;
            property.SetValue(localizedMetadata, "Approved value");
            var submitCalls = 0;
            var service = CreateAppleAutomationService(
                request => CreateReleaseState(request, "VALID"),
                submitAppleReview: request =>
                {
                    submitCalls++;
                    return new AppStoreConnectReviewSubmissionResult
                    {
                        AppId = request.AppId,
                        VersionString = request.VersionString,
                        BuildNumber = request.BuildNumber,
                        Platform = request.Platform
                    };
                },
                checkAppleReleaseReadiness: (_, request) => new AppStoreConnectReleaseReadinessResult
                {
                    AppId = request.AppId,
                    VersionString = request.VersionString,
                    BuildNumber = request.BuildNumber,
                    Platform = request.Platform,
                    IsReady = true,
                    Localization = new() { Locale = "en-US", Description = "Unchanged English description" },
                    Localizations = [new() { Locale = "en-US", Description = "Unchanged English description" }, localizedMetadata],
                    Checks =
                    [
                        new AppStoreConnectReleaseReadinessCheck
                        {
                            Name = "metadata",
                            Passed = true,
                            Message = "Metadata is ready."
                        }
                    ]
                });
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var plan = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    AppleAction = PowerForgeAppleReleaseAction.SubmitAppReview
                });
            property.SetValue(localizedMetadata, "Changed value");

            var execution = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    AppleAction = PowerForgeAppleReleaseAction.SubmitAppReview,
                    AppleActionConfirmed = true,
                    AppleExpectedPlanSha256 = plan.AppleReceipt!.PlanSha256
                });

            Assert.False(execution.Success);
            Assert.Equal(0, submitCalls);
            Assert.Contains("changed after plan approval", execution.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

}
