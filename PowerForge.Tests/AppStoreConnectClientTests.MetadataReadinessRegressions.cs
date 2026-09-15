using System.Net;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Fact]
    public async Task ReleaseReadiness_NineteenLocalesShareVersionAndBuildRequests()
    {
        var locales = new[] { "en-US", "pl", "de-DE", "es-ES", "fr-FR", "it", "nl-NL", "sv", "da", "no", "cs", "pt-BR", "fi", "pt-PT", "uk", "ja", "ko", "zh-Hans", "zh-Hant" };
        var responses = new List<SequenceResponse>
        {
            MetadataReadinessVersion(), MetadataReadinessBuild(),
            new(HttpStatusCode.OK, """{"data":{"id":"build-1","type":"builds"}}""")
        };
        foreach (var locale in locales)
            responses.AddRange(MetadataLocaleReadinessResponses(locale, locale, screenshots: locale == "en-US").Skip(1));
        var handler = new SequenceHandler(responses.ToArray());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectReleaseReadinessService(client).CheckAsync(new()
        {
            AppId = "app-1", VersionString = "1.0.0", BuildNumber = "5", MetadataLocales = locales,
            RequireKeywords = false, RequireSupportUrl = false,
            ScreenshotSpec = new() { Locale = "en-US", ScreenshotSets = [new() { ScreenshotDisplayType = "APP_IPHONE_65" }] }
        });
        Assert.True(result.IsReady);
        Assert.Equal(locales, result.Localizations.Select(value => value.Locale));
        Assert.Equal("build-1", result.SelectedBuildId);
        Assert.Equal("en-US", Assert.Single(result.ScreenshotSets).Locale);
        Assert.Equal(24, handler.RequestUris.Count);
        Assert.Single(handler.RequestUris, uri => uri.AbsolutePath.EndsWith("/appStoreVersions", StringComparison.Ordinal));
        Assert.Single(handler.RequestUris, uri => uri.AbsolutePath.EndsWith("/builds", StringComparison.Ordinal));
        Assert.Single(handler.RequestUris, uri => uri.AbsolutePath.EndsWith("/relationships/build", StringComparison.Ordinal));
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReleasePreparation_AppliedMetadataPreservesLegacyScreenshotReadinessLocale(bool syncScreenshots, bool missingLocale)
    {
        var root = Directory.CreateTempSubdirectory("PowerForge.MetadataReadiness.");
        try
        {
            var mapping = CreateLocaleMapping(root.FullName, "en-US");
            var responses = new List<SequenceResponse>
            {
                MetadataReadinessLocalization("en-US", "English"), MetadataReadinessLocalization("en-US", "English", single: true)
            };
            if (syncScreenshots)
                responses.AddRange(ExistingLocaleScreenshotResponses("en-US"));
            responses.Add(MetadataReadinessVersion());
            responses.Add(MetadataReadinessBuild());
            responses.Add(missingLocale ? new(HttpStatusCode.OK, """{"data":[]}""") : MetadataReadinessLocalization("pl", "Polski"));
            if (!missingLocale)
                responses.AddRange(ExistingLocaleScreenshotResponses("pl").Skip(1));
            responses.Add(MetadataReadinessLocalization("en-US", "English"));
            var handler = new SequenceHandler(responses.ToArray());
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
            using var client = new AppStoreConnectClient(CreateCredential(), http);
            var metadata = VersionListing("en-US", "English");
            metadata.VersionId = "version-1";
            var result = await new AppStoreConnectReleasePreparationService(client).PrepareAsync(new()
            {
                AppId = "app-1", VersionString = "1.0.0", BuildNumber = "5", CreateVersion = false, SelectBuild = false,
                MetadataSpec = metadata, ScreenshotSpec = syncScreenshots ? mapping.Spec : null,
                BaseDirectory = mapping.BaseDirectory, CheckReadiness = true,
                ReadinessRequest = new()
                {
                    Locale = "pl", ScreenshotSpec = syncScreenshots ? null : mapping.Spec,
                    RequireSelectedBuild = false, RequireKeywords = false, RequireSupportUrl = false
                }
            });
            Assert.Equal(!missingLocale, result.Readiness!.IsReady);
            Assert.Contains(result.Readiness.Checks, check => check.Name == "pl.localization" && check.Passed == !missingLocale);
            Assert.Equal("pl", Assert.Single(result.Readiness.ScreenshotSets).Locale);
            Assert.Equal("en-US", Assert.Single(result.MetadataResults).After.Locale);
            Assert.Equal("en-US", mapping.Spec.Locale);
        }
        finally { root.Delete(true); }
    }

    private static SequenceResponse MetadataReadinessBuild()
        => new(HttpStatusCode.OK, """{"data":[{"id":"build-1","type":"builds","attributes":{"version":"5","processingState":"VALID"},"relationships":{"preReleaseVersion":{"data":{"id":"train-1","type":"preReleaseVersions"}}}}],"included":[{"id":"train-1","type":"preReleaseVersions","attributes":{"version":"1.0.0","platform":"IOS"}}]}""");
}
