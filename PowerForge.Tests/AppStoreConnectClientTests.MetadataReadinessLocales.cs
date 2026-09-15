using System.Net;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class AppStoreConnectClientTests
{
    [Theory]
    [InlineData("ready")]
    [InlineData("missing-description")]
    [InlineData("missing-locale")]
    public async Task ReleaseReadiness_ChecksMetadataLocalesWithoutRequiringLocalizedScreenshots(string state)
    {
        var responses = MetadataLocaleReadinessResponses("en-US", "English", screenshots: true)
            .Concat(MetadataLocaleReadinessResponses("pl", state == "missing-description" ? null : "Polski",
                missing: state == "missing-locale").Skip(1)).ToArray();
        var handler = new SequenceHandler(responses);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectReleaseReadinessService(client).CheckAsync(new()
        {
            AppId = "app-1", VersionString = "1.0.0", MetadataLocales = ["en-US", " pl ", "PL"],
            RequireKeywords = false, RequireSupportUrl = false,
            ScreenshotSpec = new() { Locale = "en-US", ScreenshotSets = [new() { ScreenshotDisplayType = "APP_IPHONE_65" }] }
        });

        Assert.Equal(state == "ready", result.IsReady);
        Assert.Equal("en-US", Assert.Single(result.ScreenshotSets).Locale);
        Assert.DoesNotContain(result.Checks, check => check.Name.StartsWith("pl.screenshots", StringComparison.Ordinal));
        Assert.Contains(result.Checks, check => check.Name == "pl.localization" && check.Passed == (state != "missing-locale"));
        if (state != "missing-locale")
        {
            Assert.Equal(new[] { "en-US", "pl" }, result.Localizations.Select(value => value.Locale));
            Assert.Contains(result.Checks, check => check.Name == "pl.metadata.description" && check.Passed == (state == "ready"));
        }
        Assert.Equal(5, handler.Methods.Count);
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task ReleaseReadiness_MetadataOnlySelectionDoesNotImplicitlyRequireEnglish()
    {
        var handler = new SequenceHandler(MetadataLocaleReadinessResponses("fi", "Suomi"));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectReleaseReadinessService(client).CheckAsync(new()
        {
            AppId = "app-1", VersionString = "1.0.0", MetadataLocales = ["fi"],
            RequireScreenshots = false, RequireKeywords = false, RequireSupportUrl = false
        });
        Assert.True(result.IsReady);
        Assert.Equal("fi", Assert.Single(result.Localizations).Locale);
        Assert.Empty(result.ScreenshotSets);
        Assert.Equal(2, handler.Methods.Count);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fi")]
    public async Task ReleasePreparation_ChecksEveryAppliedMetadataLocaleWithoutScreenshotSync(string primaryLocale)
    {
        var responses = new List<SequenceResponse>
        {
            MetadataReadinessVersion(),
            MetadataReadinessLocalization("en-US", "English"), MetadataReadinessLocalization("en-US", "English", single: true),
            MetadataReadinessLocalization("pl", "Polski"), MetadataReadinessLocalization("pl", "Polski", single: true)
        };
        var expectedLocales = new[] { primaryLocale, "en-US", "pl" }.Distinct().ToArray();
        responses.Add(MetadataReadinessVersion());
        responses.Add(new(HttpStatusCode.OK, """{"data":[{"id":"build-1","type":"builds","attributes":{"version":"5","processingState":"VALID"},"relationships":{"preReleaseVersion":{"data":{"id":"train-1","type":"preReleaseVersions"}}}}],"included":[{"id":"train-1","type":"preReleaseVersions","attributes":{"version":"1.0.0","platform":"IOS"}}]}"""));
        foreach (var locale in expectedLocales)
            responses.Add(MetadataReadinessLocalization(locale, locale == "en-US" ? "English" : "Polski"));
        var handler = new SequenceHandler(responses.ToArray());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var result = await new AppStoreConnectReleasePreparationService(client).PrepareAsync(new()
        {
            AppId = "app-1", VersionString = "1.0.0", BuildNumber = "5", CreateVersion = false, SelectBuild = false,
            MetadataSpecs = [VersionListing("en-US", "English"), VersionListing("pl", "Polski")], CheckReadiness = true,
            ReadinessRequest = new() { Locale = primaryLocale, RequireSelectedBuild = false, RequireValidBuild = false, RequireScreenshots = false, RequireKeywords = false, RequireSupportUrl = false }
        });
        Assert.True(result.Readiness?.IsReady);
        Assert.Equal(expectedLocales, result.Readiness!.Localizations.Select(value => value.Locale));
        Assert.Equal(2, result.MetadataResults.Length);
        Assert.Equal(2, handler.Methods.Count(method => method == HttpMethod.Patch));
        Assert.All(handler.Methods, method => Assert.True(method == HttpMethod.Get || method == HttpMethod.Patch));
    }

    [Fact]
    public async Task ReviewSubmission_RejectsMissingSecondaryMetadataBeforeAnyMutation()
    {
        var build = new SequenceResponse(HttpStatusCode.OK, """{"data":[{"id":"build-1","type":"builds","attributes":{"version":"5","processingState":"VALID"},"relationships":{"preReleaseVersion":{"data":{"id":"train-1","type":"preReleaseVersions"}}}}],"included":[{"id":"train-1","type":"preReleaseVersions","attributes":{"version":"1.0.0","platform":"IOS"}}]}""");
        var responses = new List<SequenceResponse> { MetadataReadinessVersion(), build };
        responses.Add(MetadataReadinessVersion());
        responses.Add(build);
        foreach (var locale in new[] { "en-US", "pl" })
        {
            responses.AddRange(MetadataLocaleReadinessResponses(locale, locale == "en-US" ? "English" : null,
                screenshots: locale == "en-US").Skip(1));
        }
        var handler = new SequenceHandler(responses.ToArray());
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new AppStoreConnectReviewSubmissionService(client).SubmitAsync(new()
        {
            AppId = "app-1", VersionString = "1.0.0", BuildNumber = "5", RequireSelectedBuild = false,
            ReadinessRequest = new()
            {
                MetadataLocales = ["en-US", "pl"], RequireSelectedBuild = false, RequireKeywords = false, RequireSupportUrl = false,
                ScreenshotSpec = new() { Locale = "en-US", ScreenshotSets = [new() { ScreenshotDisplayType = "APP_IPHONE_65" }] }
            }
        }));
        Assert.Contains("description is missing", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.All(handler.Methods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task ReleasePreparation_RejectsInvalidReadinessLocaleBeforeMutation()
    {
        var handler = new SequenceHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.appstoreconnect.apple.com/v1/") };
        using var client = new AppStoreConnectClient(CreateCredential(), http);
        await Assert.ThrowsAsync<ArgumentException>(() => new AppStoreConnectReleasePreparationService(client).PrepareAsync(new()
        {
            AppId = "app-1", VersionString = "1.0.0", BuildNumber = "5", CheckReadiness = true,
            ReadinessRequest = new() { MetadataLocales = [" "] }
        }));
        Assert.Empty(handler.Methods);
    }

    private static SequenceResponse[] MetadataLocaleReadinessResponses(string locale, string? description, bool screenshots = false, bool missing = false)
    {
        var responses = new List<SequenceResponse>
        {
            MetadataReadinessVersion(),
            missing ? new(HttpStatusCode.OK, """{"data":[]}""") : MetadataReadinessLocalization(locale, description)
        };
        if (screenshots)
            responses.AddRange(ExistingLocaleScreenshotResponses(locale).Skip(1));
        return responses.ToArray();
    }

    private static SequenceResponse MetadataReadinessVersion()
        => new(HttpStatusCode.OK, """{"data":[{"id":"version-1","type":"appStoreVersions","attributes":{"versionString":"1.0.0","platform":"IOS"}}]}""");

    private static SequenceResponse MetadataReadinessLocalization(string locale, string? description, bool single = false)
    {
        var localization = new { id = $"loc-{locale}", type = "appStoreVersionLocalizations", attributes = new { locale, description } };
        return new(HttpStatusCode.OK, JsonSerializer.Serialize(new { data = single ? (object)localization : new[] { localization } }));
    }
}
