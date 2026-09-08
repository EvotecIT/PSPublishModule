using System.Text.Json;

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
                    Localizations = request.MetadataLocales.Select(locale => locale == "pl"
                        ? localizedMetadata
                        : new AppStoreConnectVersionLocalizationInfo { Locale = locale, Description = "Unchanged English description" }).ToArray(),
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
            ConfigureMetadataReadinessFiles(spec, root, ["en-US", "pl"]);
            var plan = service.Execute(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "powerforge.release.json"),
                    PlanOnly = true,
                    AppleAction = PowerForgeAppleReleaseAction.SubmitAppReview
                });
            Assert.True(plan.Success, plan.ErrorMessage);
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


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Execute_AppleSubmissionCarriesEveryMetadataLocaleWithOnlyEnglishScreenshots(bool changeMetadataDuringApproval)
    {
        var root = CreateSandbox();
        try
        {
            CreateXcodeProject(root, "Sample.xcodeproj", "1.2.0", "9");
            var keyPath = Path.Combine(root, "AuthKey_TEST.p8");
            File.WriteAllText(keyPath, "private-key");
            var locales = new[] { "en-US", "pl", "de-DE", "es-ES", "fr-FR", "it", "nl-NL", "sv", "da", "no", "cs", "pt-BR", "fi", "pt-PT", "uk", "ja", "ko", "zh-Hans", "zh-Hant" };
            var spec = CreateAppleAutomationSpec(root, keyPath);
            var app = spec.AppleApps!.Apps[0];
            app.ProjectPath = "Sample.xcodeproj";
            app.Scheme = "Sample";
            app.Name = "Sample";
            app.BundleId = "com.example.sample";
            app.AppStoreConnectAppId = "1234567890";
            ConfigureMetadataReadinessFiles(spec, root, locales);
            var readinessRequests = new List<AppStoreConnectReleaseReadinessRequest>();
            AppStoreConnectReviewSubmissionRequest? submission = null;
            var service = CreateAppleAutomationService(request => CreateReleaseState(request, "VALID"),
                checkAppleReleaseReadiness: (_, request) =>
                {
                    readinessRequests.Add(request);
                    if (changeMetadataDuringApproval && readinessRequests.Count == 2)
                    {
                        var path = Path.Combine(root, "metadata-pl.json");
                        var metadata = JsonSerializer.Deserialize<AppStoreConnectVersionMetadataSpec>(File.ReadAllText(path))!;
                        metadata.Locale = "ro";
                        File.WriteAllText(path, JsonSerializer.Serialize(metadata));
                    }
                    return CreateReadyReleaseReadiness(request);
                },
                submitAppleReview: request =>
                {
                    submission = request;
                    return new() { AppId = request.AppId, VersionString = request.VersionString, BuildNumber = request.BuildNumber, Platform = request.Platform };
                });
            var request = new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "powerforge.release.json"),
                AppleAction = PowerForgeAppleReleaseAction.SubmitAppReview, PlanOnly = true
            };
            var plan = service.Execute(spec, request);
            Assert.True(plan.Success, plan.ErrorMessage);
            request.PlanOnly = false;
            request.AppleActionConfirmed = true;
            request.AppleExpectedPlanSha256 = plan.AppleReceipt!.PlanSha256;
            var result = service.Execute(spec, request);
            if (changeMetadataDuringApproval)
            {
                Assert.False(result.Success);
                Assert.Contains("changed after plan approval", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
                Assert.Null(submission);
                return;
            }
            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotEmpty(readinessRequests);
            Assert.NotNull(submission?.ReadinessRequest);
            foreach (var readiness in readinessRequests.Append(submission!.ReadinessRequest!))
            {
                Assert.Equal(locales, readiness.MetadataLocales);
                Assert.Equal("en-US", readiness.ScreenshotSpec?.Locale);
                Assert.Empty(readiness.ScreenshotSpecs);
            }
        }
        finally { TryDelete(root); }
    }

    private static void ConfigureMetadataReadinessFiles(PowerForgeReleaseSpec spec, string root, string[] locales)
    {
        var app = spec.AppleApps!.Apps[0];
        var metadataPaths = new List<string>();
        foreach (var locale in locales)
        {
            var filename = $"metadata-{locale}.json";
            File.WriteAllText(Path.Combine(root, filename), JsonSerializer.Serialize(new AppStoreConnectVersionMetadataSpec
            {
                AppId = app.AppStoreConnectAppId!, Platform = app.Platform, UseReleaseVersion = true,
                Locale = locale, Metadata = new() { Description = $"Description for {locale}" }
            }));
            metadataPaths.Add(filename);
        }
        spec.AppleApps.MetadataConfigPaths = metadataPaths.ToArray();
        const string screenshotPath = "screenshots-en.json";
        File.WriteAllText(Path.Combine(root, screenshotPath), JsonSerializer.Serialize(new AppStoreConnectScreenshotSyncSpec
        {
            AppId = app.AppStoreConnectAppId!, Platform = app.Platform, UseReleaseVersion = true, Locale = "en-US",
            ScreenshotSets = [new() { ScreenshotDisplayType = "APP_IPHONE_65", Path = "missing-local-files" }]
        }));
        spec.AppleApps.ScreenshotConfigPaths = [screenshotPath];
    }
}
