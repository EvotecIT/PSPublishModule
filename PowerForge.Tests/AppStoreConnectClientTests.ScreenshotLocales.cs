using System.Net;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Theory]
    [InlineData("duplicate")]
    [InlineData("missing-files")]
    [InlineData("version-id")]
    [InlineData("platform")]
    public async Task ReleasePreparation_RejectsInvalidScreenshotLocaleBeforeAnyRemoteWork(string defect)
    {
        var root = Directory.CreateTempSubdirectory("PowerForge.ScreenshotLocales.");
        try
        {
            var first = CreateLocaleMapping(root.FullName, "en-US");
            var second = CreateLocaleMapping(root.FullName, "pl");
            switch (defect)
            {
                case "duplicate": second.Spec.Locale = " EN-us "; break;
                case "missing-files": second.Spec.ScreenshotSets[0].Path = "missing"; break;
                case "version-id": second.Spec.VersionId = "other-version"; break;
                case "platform": second.Spec.Platform = ApplePlatform.macOS; break;
            }
            var handler = new SequenceHandler();
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var error = await Record.ExceptionAsync(() => new AppStoreConnectReleasePreparationService(client).PrepareAsync(new()
            {
                AppId = "app-1", VersionString = "1.0.0", SelectBuild = false,
                ScreenshotMappings = [first, second]
            }));
            Assert.NotNull(error);
            Assert.Empty(handler.Methods);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task ReleasePreparation_SyncsBothLocalesUsingTheirOwnRelativeFolders()
    {
        var root = Directory.CreateTempSubdirectory("PowerForge.ScreenshotLocales.");
        try
        {
            var mappings = new[] { CreateLocaleMapping(root.FullName, "en-US"), CreateLocaleMapping(root.FullName, "pl") };
            var responses = mappings.SelectMany(mapping => ExistingLocaleScreenshotResponses(mapping.Spec.Locale)).ToArray();
            var handler = new SequenceHandler(responses);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var result = await new AppStoreConnectReleasePreparationService(client).PrepareAsync(new()
            {
                AppId = "app-1", VersionString = "1.0.0", CreateVersion = false, SelectBuild = false,
                ScreenshotMappings = mappings
            });
            Assert.Equal(new[] { "en-US", "pl" }, result.ScreenshotResults.Select(value => value.Localization.Locale));
            Assert.Same(result.ScreenshotResults[0], result.Screenshots);
            Assert.All(result.ScreenshotResults, value => Assert.Empty(Assert.Single(value.ScreenshotSets).Uploaded));
            Assert.Equal(6, handler.Methods.Count);
            Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task ReleasePreparation_RejectsLaterLocaleInventoryDriftBeforeMetadataMutation()
    {
        var root = Directory.CreateTempSubdirectory("PowerForge.ScreenshotLocales.");
        try
        {
            var mappings = new[] { CreateLocaleMapping(root.FullName, "en-US"), CreateLocaleMapping(root.FullName, "pl") };
            var handler = new SequenceHandler(mappings.SelectMany(mapping => ExistingLocaleScreenshotResponses(mapping.Spec.Locale)).ToArray());
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var expected = mappings.Select(mapping => new AppStoreConnectReleaseScreenshotSetReadiness
            {
                Locale = mapping.Spec.Locale, ScreenshotDisplayType = "APP_IPHONE_65", ScreenshotSetId = $"set-{mapping.Spec.Locale}",
                Screenshots = [new() { Id = mapping.Spec.Locale == "pl" ? "prior-polish-shot" : "shot-en-US", FileName = $"{mapping.Spec.Locale}.png", FileSize = 3,
                    SourceFileChecksum = "5289df737df57326fcdd22597afb1fac", AssetDeliveryState = "COMPLETE" }]
            });
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new AppStoreConnectReleasePreparationService(client).PrepareAsync(new()
            {
                AppId = "app-1", VersionString = "1.0.0", CreateVersion = false, SelectBuild = false,
                ScreenshotMappings = mappings, ReplaceScreenshots = true,
                ExpectedScreenshotInventorySha256 = AppStoreConnectScreenshotInventory.ComputeSha256(expected),
                MetadataSpec = new() { AppId = "app-1", VersionString = "1.0.0", VersionId = "version-1", Locale = "en-US", Metadata = new() { Description = "Updated" } }
            }));
            Assert.Contains("before any remote release mutation", error.Message);
            Assert.Equal(6, handler.Methods.Count);
            Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public async Task ReleaseReadiness_ChecksEveryConfiguredScreenshotLocale()
    {
        var responses = new List<SequenceResponse>();
        foreach (var locale in new[] { "en-US", "pl" })
        {
            responses.Add(new(HttpStatusCode.OK, """{"data":[{"id":"version-1","type":"appStoreVersions","attributes":{"versionString":"1.0.0","platform":"IOS"}}]}"""));
            responses.AddRange(ExistingLocaleScreenshotResponses(locale));
        }
        var handler = new SequenceHandler(responses.ToArray());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectReleaseReadinessService(client).CheckAsync(new()
        {
            AppId = "app-1", VersionString = "1.0.0", RequireDescription = false, RequireKeywords = false, RequireSupportUrl = false,
            ScreenshotSpecs = new[] { "en-US", "pl" }.Select(locale => new AppStoreConnectScreenshotSyncSpec
            {
                Locale = locale, ScreenshotSets = [new() { ScreenshotDisplayType = "APP_IPHONE_65" }]
            }).ToArray()
        });
        Assert.True(result.IsReady);
        Assert.Equal(new[] { "en-US", "pl" }, result.Localizations.Select(localization => localization.Locale));
        Assert.Equal(new[] { "en-US", "pl" }, result.ScreenshotSets.Select(set => set.Locale));
        Assert.Contains(result.Checks, check => check.Name == "pl.screenshots.APP_IPHONE_65.complete");
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("pl")]
    public async Task ReleasePreparation_LegacyScreenshotPreservesReadinessLocaleAndCheckNames(string readinessLocale)
    {
        var root = Directory.CreateTempSubdirectory("PowerForge.ScreenshotLocales.");
        try
        {
            var mapping = CreateLocaleMapping(root.FullName, "en-US");
            var responses = new List<SequenceResponse>(ExistingLocaleScreenshotResponses("en-US"))
            {
                new(HttpStatusCode.OK, """{"data":[{"id":"version-1","type":"appStoreVersions","attributes":{"versionString":"1.0.0","platform":"IOS"}}]}"""),
                new(HttpStatusCode.OK, """{"data":[]}""")
            };
            responses.AddRange(ExistingLocaleScreenshotResponses(readinessLocale));
            var handler = new SequenceHandler(responses.ToArray());
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var result = await new AppStoreConnectReleasePreparationService(client).PrepareAsync(new()
            {
                AppId = "app-1", VersionString = "1.0.0", BuildNumber = "5", CreateVersion = false, SelectBuild = false,
                ScreenshotSpec = mapping.Spec, BaseDirectory = mapping.BaseDirectory, CheckReadiness = true,
                ReadinessRequest = new()
                {
                    Locale = readinessLocale, RequireSelectedBuild = false,
                    RequireDescription = false, RequireKeywords = false, RequireSupportUrl = false
                }
            });
            Assert.NotNull(result.Readiness);
            Assert.Equal(readinessLocale, result.Readiness.Localization?.Locale);
            Assert.Contains(result.Readiness.Checks, check => check.Name == "version");
            Assert.Contains(result.Readiness.Checks, check => check.Name == "screenshots.APP_IPHONE_65.complete");
            Assert.DoesNotContain(result.Readiness.Checks, check => check.Name.StartsWith("en-US.") || check.Name.StartsWith("pl."));
            Assert.Equal("en-US", result.Screenshots?.Localization.Locale);
        }
        finally { root.Delete(true); }
    }

    private static AppStoreConnectReleaseScreenshotMapping CreateLocaleMapping(string root, string locale)
    {
        var baseDirectory = Directory.CreateDirectory(Path.Combine(root, locale)).FullName;
        Directory.CreateDirectory(Path.Combine(baseDirectory, "shots"));
        File.WriteAllBytes(Path.Combine(baseDirectory, "shots", $"{locale}.png"), [1, 2, 3]);
        return new()
        {
            BaseDirectory = baseDirectory,
            Spec = new()
            {
                AppId = "app-1", VersionString = "1.0.0", VersionId = "version-1", Locale = locale,
                ScreenshotSets = [new() { ScreenshotDisplayType = "APP_IPHONE_65", Path = "shots" }]
            }
        };
    }

    private static SequenceResponse[] ExistingLocaleScreenshotResponses(string locale) =>
    [
        new(HttpStatusCode.OK, $$$$"""{"data":[{"id":"loc-{{{{locale}}}}","type":"appStoreVersionLocalizations","attributes":{"locale":"{{{{locale}}}}"}}]}"""),
        new(HttpStatusCode.OK, $$$$"""{"data":[{"id":"set-{{{{locale}}}}","type":"appScreenshotSets","attributes":{"screenshotDisplayType":"APP_IPHONE_65"}}]}"""),
        new(HttpStatusCode.OK, $$$$"""{"data":[{"id":"shot-{{{{locale}}}}","type":"appScreenshots","attributes":{"fileName":"{{{{locale}}}}.png","fileSize":3,"sourceFileChecksum":"5289df737df57326fcdd22597afb1fac","assetDeliveryState":{"state":"COMPLETE"}}}]}""")
    ];
}
