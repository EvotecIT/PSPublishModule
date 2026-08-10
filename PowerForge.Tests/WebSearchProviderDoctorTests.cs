using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebSearchProviderDoctorTests
{
    [Fact]
    public void ExampleConfiguration_PassesSchemaLoaderAndCapabilityDoctor()
    {
        var examplePath = RepositoryPath("Examples", "PowerForge.Web", "Search", "providers.json");
        var configuration = WebSearchProviderConfigurationLoader.LoadWithPath(examplePath, WebCliJson.Options).Configuration;
        var schema = LoadProviderSchema();
        var document = JsonNode.Parse(File.ReadAllText(examplePath))!;

        var result = WebSearchProviderDoctor.Inspect(
            configuration,
            _ => null,
            new HashSet<string>(["lighthouse"], StringComparer.OrdinalIgnoreCase));

        Assert.True(schema.Evaluate(document, new EvaluationOptions()).IsValid);
        Assert.True(result.Success);
        Assert.Equal(1, result.SiteCount);
        Assert.Equal(5, result.ProviderCount);
        Assert.Equal(5, result.ConfigurationReadyCount);
        Assert.Equal(1, result.CollectorAvailableCount);
        Assert.NotNull(result.ConfigurationHash);
        Assert.StartsWith("sha256:", result.ConfigurationHash, StringComparison.Ordinal);
        Assert.All(result.Checks, check => Assert.NotEqual(WebSearchProviderCheckSeverity.Error, check.Severity));
        var lighthouse = Assert.Single(result.Providers, provider => provider.ProviderId == "lighthouse");
        Assert.True(lighthouse.CollectionReady);
        Assert.Contains(result.Checks, check => check.Code == "provider.credential-unavailable");
    }

    [Fact]
    public void Doctor_RejectsUnavailableCredentialForEnabledProviderWithoutExposingItsValue()
    {
        var configuration = CreateGoogleConfiguration();

        var result = WebSearchProviderDoctor.Inspect(configuration, _ => null);

        Assert.False(result.Success);
        Assert.False(Assert.Single(result.Providers).ConfigurationReady);
        var check = Assert.Single(result.Checks, finding => finding.Code == "provider.credential-unavailable");
        Assert.Equal(WebSearchProviderCheckSeverity.Error, check.Severity);
        Assert.Contains("POWERFORGE_TEST_GSC", check.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("credential-value", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Fact]
    public void Doctor_SeparatesValidConfigurationFromCollectorAvailability()
    {
        var configuration = CreateGoogleConfiguration();

        var planned = WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value");
        var available = WebSearchProviderDoctor.Inspect(
            configuration,
            _ => "credential-value",
            new HashSet<string>(["google-search-console"], StringComparer.OrdinalIgnoreCase));

        Assert.True(planned.Success);
        Assert.True(Assert.Single(planned.Providers).ConfigurationReady);
        Assert.False(Assert.Single(planned.Providers).CollectorAvailable);
        Assert.False(Assert.Single(planned.Providers).CollectionReady);
        Assert.Contains(planned.Checks, check => check.Code == "provider.collector-unavailable");
        Assert.True(Assert.Single(available.Providers).CollectionReady);
        Assert.DoesNotContain(available.Checks, check => check.Code == "provider.collector-unavailable");
    }

    [Fact]
    public void Doctor_NeverMarksProvidersReadyWhenAConfigurationWideErrorExists()
    {
        var configuration = CreateGoogleConfiguration();
        configuration.SchemaVersion = 2;

        var result = WebSearchProviderDoctor.Inspect(
            configuration,
            _ => "credential-value",
            new HashSet<string>(["google-search-console"], StringComparer.OrdinalIgnoreCase));

        Assert.False(result.Success);
        Assert.False(Assert.Single(result.Providers).ConfigurationReady);
        Assert.False(Assert.Single(result.Providers).CollectionReady);
    }

    [Fact]
    public void Doctor_MarksEveryOccurrenceOfADuplicateProviderIdentityNotReady()
    {
        var configuration = CreateGoogleConfiguration();
        configuration.Sites[0].Providers =
        [
            configuration.Sites[0].Providers[0],
            CreateGoogleConfiguration().Sites[0].Providers[0]
        ];

        var result = WebSearchProviderDoctor.Inspect(
            configuration,
            _ => "credential-value",
            new HashSet<string>(["google-search-console"], StringComparer.OrdinalIgnoreCase));

        Assert.False(result.Success);
        Assert.All(result.Providers, provider =>
        {
            Assert.False(provider.ConfigurationReady);
            Assert.False(provider.CollectionReady);
        });
    }

    [Fact]
    public void ConfigurationFingerprint_IsStableAcrossFleetOrderingAndChangesWithIntent()
    {
        var first = CreateGoogleConfiguration();
        var bing = new WebSearchProviderRegistration
        {
            Id = "bing",
            Kind = "bing-webmaster-export",
            Capabilities = [WebSearchProviderCapabilities.SearchAnalytics],
            Settings = new Dictionary<string, string?> { ["siteUrl"] = "https://officeimo.com/" }
        };
        first.Sites[0].Providers = [first.Sites[0].Providers[0], bing];
        var reordered = CreateGoogleConfiguration();
        reordered.Sites[0].Providers = [bing, reordered.Sites[0].Providers[0]];
        var changed = CreateGoogleConfiguration();
        changed.Sites[0].Providers[0].Settings["property"] = "sc-domain:tactra.dev";

        Assert.Equal(
            WebSearchProviderConfigurationFingerprint.Compute(first),
            WebSearchProviderConfigurationFingerprint.Compute(reordered));
        Assert.NotEqual(
            WebSearchProviderConfigurationFingerprint.Compute(first),
            WebSearchProviderConfigurationFingerprint.Compute(changed));
    }

    [Fact]
    public void ConfigurationFingerprint_RedactsForbiddenSecretSettingValues()
    {
        var first = CreateGoogleConfiguration();
        first.Sites[0].Providers[0].Settings["api_key"] = "first-candidate";
        var second = CreateGoogleConfiguration();
        second.Sites[0].Providers[0].Settings["api_key"] = "second-candidate";

        Assert.Equal(
            WebSearchProviderConfigurationFingerprint.Compute(first),
            WebSearchProviderConfigurationFingerprint.Compute(second));
        Assert.Contains(
            WebSearchProviderDoctor.Inspect(first, _ => "credential-value").Checks,
            check => check.Code == "provider.setting-secret-forbidden");
    }

    [Fact]
    public void ConfigurationFingerprint_RedactsUnsupportedSettingValuesEvenWhenPunctuationLooksAllowed()
    {
        var first = CreateGoogleConfiguration();
        first.Sites[0].Providers[0].Settings["pro_per_ty"] = "first-candidate";
        first.Sites[0].Providers[0].Settings["siteUrl"] = "https://first.example/";
        var second = CreateGoogleConfiguration();
        second.Sites[0].Providers[0].Settings["pro_per_ty"] = "second-candidate";
        second.Sites[0].Providers[0].Settings["siteUrl"] = "https://second.example/";

        Assert.Equal(
            WebSearchProviderConfigurationFingerprint.Compute(first),
            WebSearchProviderConfigurationFingerprint.Compute(second));
        Assert.Equal(2, WebSearchProviderDoctor.Inspect(first, _ => "credential-value").Checks.Count(
            check => check.Code == "provider.setting-unsupported"));
    }

    [Fact]
    public void ConfigurationFingerprint_RedactsNoncanonicalSettingValues()
    {
        var first = CreateGoogleConfiguration();
        first.Sites[0].Providers[0].Settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PROPERTY"] = "first-candidate"
        };
        var second = CreateGoogleConfiguration();
        second.Sites[0].Providers[0].Settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PROPERTY"] = "second-candidate"
        };

        Assert.Equal(
            WebSearchProviderConfigurationFingerprint.Compute(first),
            WebSearchProviderConfigurationFingerprint.Compute(second));
    }

    [Fact]
    public void ConfigurationFingerprint_NormalizesEquivalentProviderSettingValues()
    {
        var googleLower = CreateGoogleConfiguration();
        var googleUpper = CreateGoogleConfiguration();
        googleUpper.Sites[0].Providers[0].Settings["property"] = "sc-domain:OfficeIMO.COM";
        var bingLower = CreateBingConfiguration("https://officeimo.com/");
        var bingUpper = CreateBingConfiguration("HTTPS://OfficeIMO.COM:443/");
        var cloudflareLower = CreateCloudflareConfiguration("abcdef0123456789abcdef0123456789");
        var cloudflareUpper = CreateCloudflareConfiguration("ABCDEF0123456789ABCDEF0123456789");

        Assert.All(
            new[] { googleLower, googleUpper, bingLower, bingUpper, cloudflareLower, cloudflareUpper },
            configuration => Assert.True(WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value").Success));

        Assert.Equal(
            WebSearchProviderConfigurationFingerprint.Compute(googleLower),
            WebSearchProviderConfigurationFingerprint.Compute(googleUpper));
        Assert.Equal(
            WebSearchProviderConfigurationFingerprint.Compute(bingLower),
            WebSearchProviderConfigurationFingerprint.Compute(bingUpper));
        Assert.Equal(
            WebSearchProviderConfigurationFingerprint.Compute(cloudflareLower),
            WebSearchProviderConfigurationFingerprint.Compute(cloudflareUpper));
    }

    [Fact]
    public void ConfigurationFingerprint_NormalizesIdnHostsAcrossSitesAndProviderUrls()
    {
        const string unicodeHost = "bücher.example";
        const string asciiHost = "xn--bcher-kva.example";
        var unicodeSite = CreateGoogleConfiguration();
        unicodeSite.Sites[0].BaseUrl = $"https://{unicodeHost}/";
        unicodeSite.Sites[0].Providers[0].Settings["property"] = $"sc-domain:{asciiHost}";
        var asciiSite = CreateGoogleConfiguration();
        asciiSite.Sites[0].BaseUrl = $"https://{asciiHost}/";
        asciiSite.Sites[0].Providers[0].Settings["property"] = $"sc-domain:{asciiHost}";

        var unicodeBing = CreateBingConfiguration($"https://{unicodeHost}/");
        unicodeBing.Sites[0].BaseUrl = $"https://{asciiHost}/";
        var asciiBing = CreateBingConfiguration($"https://{asciiHost}/");
        asciiBing.Sites[0].BaseUrl = $"https://{asciiHost}/";

        Assert.All(
            new[] { unicodeSite, asciiSite, unicodeBing, asciiBing },
            configuration => Assert.True(WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value").Success));
        Assert.Equal(
            WebSearchProviderConfigurationFingerprint.Compute(unicodeSite),
            WebSearchProviderConfigurationFingerprint.Compute(asciiSite));
        Assert.Equal(
            WebSearchProviderConfigurationFingerprint.Compute(unicodeBing),
            WebSearchProviderConfigurationFingerprint.Compute(asciiBing));
    }

    [Fact]
    public void ConfigurationFingerprint_NormalizesTrailingDnsRootDotsAcrossSitesAndProviderUrls()
    {
        var dottedGoogle = CreateGoogleConfiguration();
        dottedGoogle.Sites[0].BaseUrl = "https://officeimo.com./";
        dottedGoogle.Sites[0].Providers[0].Settings["property"] = "sc-domain:officeimo.com.";
        var dotlessGoogle = CreateGoogleConfiguration();

        var dottedBing = CreateBingConfiguration("https://officeimo.com./");
        var dotlessBing = CreateBingConfiguration("https://officeimo.com/");

        Assert.All(
            new[] { dottedGoogle, dotlessGoogle, dottedBing, dotlessBing },
            configuration => Assert.True(WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value").Success));
        Assert.Equal(
            WebSearchProviderConfigurationFingerprint.Compute(dottedGoogle),
            WebSearchProviderConfigurationFingerprint.Compute(dotlessGoogle));
        Assert.Equal(
            WebSearchProviderConfigurationFingerprint.Compute(dottedBing),
            WebSearchProviderConfigurationFingerprint.Compute(dotlessBing));
    }

    [Fact]
    public void Doctor_RejectsUnsupportedCapabilitiesDuplicateIdentitiesAndSecretSettings()
    {
        var configuration = CreateGoogleConfiguration();
        var provider = configuration.Sites[0].Providers[0];
        provider.Capabilities = [WebSearchProviderCapabilities.SearchAnalytics, WebSearchProviderCapabilities.TrafficAnalytics];
        provider.Settings["apiToken"] = "must-not-be-here";
        configuration.Sites[0].Providers = [provider, provider];

        var result = WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value");

        Assert.False(result.Success);
        Assert.Contains(result.Checks, check => check.Code == "provider.id-duplicate");
        Assert.Contains(result.Checks, check => check.Code == "provider.capability-unsupported");
        Assert.Contains(result.Checks, check => check.Code == "provider.setting-secret-forbidden");
        Assert.DoesNotContain("must-not-be-here", JsonSerializer.Serialize(result), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("PROPERTY")]
    [InlineData(" property ")]
    public void Doctor_RejectsNoncanonicalProviderSettingKeys(string key)
    {
        var configuration = CreateGoogleConfiguration();
        configuration.Sites[0].Providers[0].Settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [key] = "sc-domain:officeimo.com"
        };

        var result = WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value");

        Assert.False(result.Success);
        Assert.Contains(result.Checks, check => check.Code == "provider.setting-key-noncanonical");
        Assert.False(Assert.Single(result.Providers).ConfigurationReady);
    }

    [Fact]
    public void Doctor_RejectsExplicitNullSettingsForProviderWithoutRequiredSettings()
    {
        var configuration = CreateGoogleConfiguration();
        configuration.Sites[0].Providers =
        [
            new WebSearchProviderRegistration
            {
                Id = "lighthouse",
                Kind = "lighthouse",
                Capabilities = [WebSearchProviderCapabilities.PerformanceLighthouse],
                Settings = null!
            }
        ];

        var result = WebSearchProviderDoctor.Inspect(
            configuration,
            _ => null,
            new HashSet<string>(["lighthouse"], StringComparer.OrdinalIgnoreCase));
        var document = JsonNode.Parse(JsonSerializer.Serialize(configuration, WebCliJson.Options))!;
        document["sites"]!.AsArray()[0]!["providers"]!.AsArray()[0]!["settings"] = null;

        Assert.False(LoadProviderSchema().Evaluate(document, new EvaluationOptions()).IsValid);
        Assert.False(result.Success);
        Assert.Contains(result.Checks, check => check.Code == "provider.settings-missing");
        Assert.False(Assert.Single(result.Providers).ConfigurationReady);
    }

    [Fact]
    public void Doctor_DoesNotExposeMutableCatalogCapabilityArrays()
    {
        var configuration = CreateGoogleConfiguration();
        var first = WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value");
        Assert.Single(first.Providers).SupportedCapabilities[0] = WebSearchProviderCapabilities.TrafficAnalytics;

        var second = WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value");

        Assert.True(second.Success);
        Assert.Contains(WebSearchProviderCapabilities.SearchAnalytics, Assert.Single(second.Providers).SupportedCapabilities);
        Assert.DoesNotContain(WebSearchProviderCapabilities.TrafficAnalytics, Assert.Single(second.Providers).SupportedCapabilities);
    }

    [Fact]
    public void Loader_RejectsUnknownConfigurationMembers()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(root, "providers.json");
            var document = JsonNode.Parse(JsonSerializer.Serialize(CreateGoogleConfiguration()))!.AsObject();
            document["credentialValue"] = "must-not-load";
            File.WriteAllText(configPath, document.ToJsonString());

            var exception = Assert.Throws<JsonException>(() =>
                WebSearchProviderConfigurationLoader.LoadWithPath(configPath, WebCliJson.Options));

            Assert.Contains("credentialValue", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Loader_RejectsSchemaPropertyCasingThatThePublishedSchemaRejects()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(root, "providers.json");
            var document = JsonNode.Parse(JsonSerializer.Serialize(CreateGoogleConfiguration()))!.AsObject();
            document["SchemaVersion"] = document["schemaVersion"]!.DeepClone();
            document.Remove("schemaVersion");
            File.WriteAllText(configPath, document.ToJsonString());

            Assert.Throws<JsonException>(() =>
                WebSearchProviderConfigurationLoader.LoadWithPath(configPath, WebCliJson.Options));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Loader_RejectsDuplicateJsonMembersBeforeDeserialization()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var configPath = Path.Combine(root, "providers.json");
            var payloads = new[]
            {
                """
                {"schemaVersion":1,"sites":[{"id":"officeimo","baseUrl":"https://officeimo.com/","providers":[{"id":"lighthouse","kind":"lighthouse","enabled":false,"enabled":true,"capabilities":["performance.lighthouse"],"settings":{}}]}]}
                """,
                """
                {"schemaVersion":1,"sites":[{"id":"officeimo","baseUrl":"https://officeimo.com/","providers":[{"id":"lighthouse","kind":"lighthouse","capabilities":["performance.lighthouse"],"settings":{"marker":"first","marker":"second"}}]}]}
                """
            };

            foreach (var payload in payloads)
            {
                File.WriteAllText(configPath, payload);
                var exception = Assert.Throws<JsonException>(() =>
                    WebSearchProviderConfigurationLoader.LoadWithPath(configPath, WebCliJson.Options));
                Assert.Equal("Provider configuration contains a duplicate JSON object member.", exception.Message);
            }
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Doctor_RejectsNoncanonicalIdentityAndEnvironmentValues()
    {
        var configuration = CreateGoogleConfiguration();
        configuration.Sites[0].Id = " officeimo ";
        configuration.Sites[0].Providers[0].Kind = " GOOGLE-SEARCH-CONSOLE ";
        configuration.Sites[0].Providers[0].Credential!.EnvironmentVariable = " POWERFORGE_TEST_GSC ";

        var result = WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value");

        Assert.False(result.Success);
        Assert.Contains(result.Checks, check => check.Code == "site.id-noncanonical");
        Assert.Contains(result.Checks, check => check.Code == "provider.kind-invalid");
        Assert.Contains(result.Checks, check => check.Code == "provider.credential-environment-invalid");
    }

    [Theory]
    [InlineData("https://officeimo.com/?preview_token=secret", "preview_token")]
    [InlineData("https://officeimo.com/#preview-secret", "preview-secret")]
    public void Doctor_RejectsSiteBaseUrlsWithQueryOrFragment(string baseUrl, string sensitiveValue)
    {
        var configuration = CreateGoogleConfiguration();
        configuration.Sites[0].BaseUrl = baseUrl;

        var result = WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value");
        var serialized = JsonSerializer.Serialize(result, WebCliJson.Options);

        Assert.False(result.Success);
        Assert.Null(result.ConfigurationHash);
        Assert.Contains(result.Checks, check => check.Code == "site.base-url-invalid");
        Assert.DoesNotContain(sensitiveValue, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Doctor_AllowsRepeatedPathSeparatorsForNonCloudflareProviders()
    {
        var configuration = CreateGoogleConfiguration();
        configuration.Sites[0].BaseUrl = "https://officeimo.com/docs//v2/";

        var result = WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value");

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Checks, check => check.Code == "site.base-url-invalid");
    }

    [Theory]
    [InlineData("https://officeimo.com/docs//v2/", "provider.cloudflare-site-path-invalid")]
    [InlineData("https://officeimo.com/docs%20archive/", "provider.cloudflare-site-path-filter-unsupported")]
    [InlineData("https://officeimo.com/docs_archive/", "provider.cloudflare-site-path-filter-unsupported")]
    public void Doctor_RejectsCloudflareSpecificPathFilterBoundaries(string baseUrl, string expectedCode)
    {
        var configuration = CreateCloudflareConfiguration("abcdef0123456789abcdef0123456789");
        configuration.Sites[0].BaseUrl = baseUrl;

        var result = WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value");

        Assert.False(result.Success);
        Assert.Contains(result.Checks, check => check.Code == expectedCode);
    }

    [Theory]
    [InlineData("sc-domain:")]
    [InlineData("sc-domain:https://officeimo.com")]
    [InlineData("sc-domain:bad value")]
    [InlineData("SC-DOMAIN:officeimo.com")]
    [InlineData("https://user:password@officeimo.com/")]
    public void Doctor_RejectsMalformedSearchConsoleDomainProperties(string property)
    {
        var configuration = CreateGoogleConfiguration();
        configuration.Sites[0].Providers[0].Settings["property"] = property;

        var result = WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value");

        Assert.False(result.Success);
        Assert.Contains(result.Checks, check => check.Code == "provider.gsc-property-invalid");
        Assert.False(Assert.Single(result.Providers).ConfigurationReady);
    }

    [Fact]
    public void Doctor_OmitsConfigurationHashWhenASettingValueFailsSemanticValidation()
    {
        const string credentialCandidate = "low-entropy-secret";
        var configuration = CreateGoogleConfiguration();
        configuration.Sites[0].Providers[0].Settings["property"] =
            $"https://user:{credentialCandidate}@officeimo.com/";

        var result = WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value");
        var serialized = JsonSerializer.Serialize(result, WebCliJson.Options);

        Assert.False(result.Success);
        Assert.Null(result.ConfigurationHash);
        Assert.DoesNotContain(credentialCandidate, serialized, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sc-domain:example.net")]
    [InlineData("https://example.net/")]
    public void Doctor_RejectsSearchConsolePropertiesThatDoNotCoverTheOwningSite(string property)
    {
        var configuration = CreateGoogleConfiguration();
        configuration.Sites[0].Providers[0].Settings["property"] = property;

        var result = WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value");

        Assert.False(result.Success);
        Assert.Contains(result.Checks, check => check.Code == "provider.gsc-property-site-mismatch");
        Assert.False(Assert.Single(result.Providers).ConfigurationReady);
    }

    [Fact]
    public void Doctor_RejectsBingSiteUrlThatDoesNotCoverTheOwningSite()
    {
        var configuration = CreateBingConfiguration("https://example.net/");

        var result = WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value");

        Assert.False(result.Success);
        Assert.Contains(result.Checks, check => check.Code == "provider.bing-site-url-mismatch");
        Assert.False(Assert.Single(result.Providers).ConfigurationReady);
    }

    [Fact]
    public void Doctor_KeepsBlankIdentityErrorsScopedToTheirRegistrations()
    {
        var configuration = CreateGoogleConfiguration();
        var blankProvider = CreateGoogleConfiguration().Sites[0].Providers[0];
        blankProvider.Id = string.Empty;
        configuration.Sites[0].Providers = [configuration.Sites[0].Providers[0], blankProvider];

        var blankSite = CreateGoogleConfiguration().Sites[0];
        blankSite.Id = string.Empty;
        blankSite.BaseUrl = "https://tactra.dev/";
        blankSite.Providers[0].Settings["property"] = "sc-domain:tactra.dev";
        configuration.Sites = [configuration.Sites[0], blankSite];

        var result = WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value");

        Assert.False(result.Success);
        Assert.True(Assert.Single(result.Providers, state =>
            state.SiteId == "officeimo" && state.ProviderId == "google-search-console").ConfigurationReady);
        Assert.False(Assert.Single(result.Providers, state =>
            state.SiteId == "officeimo" && state.ProviderId == string.Empty).ConfigurationReady);
        Assert.False(Assert.Single(result.Providers, state =>
            state.SiteId == string.Empty).ConfigurationReady);
    }

    [Fact]
    public void Cli_ProviderDoctor_UsesTheSharedConfigurationContract()
    {
        var examplePath = RepositoryPath("Examples", "PowerForge.Web", "Search", "providers.json");

        var exitCode = WebCliCommandHandlers.HandleSubCommand(
            "provider",
            ["doctor", "--config", examplePath],
            outputJson: false,
            logger: new WebConsoleLogger(),
            outputSchemaVersion: 1);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void Cli_ProviderDoctor_RejectsOptionWithoutValue()
    {
        var exitCode = WebCliCommandHandlers.HandleSubCommand(
            "provider",
            ["doctor", "--config", "--output", "json"],
            outputJson: true,
            logger: new WebConsoleLogger(),
            outputSchemaVersion: 1);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void Cli_ProviderDoctor_RejectsUnknownArguments()
    {
        var examplePath = RepositoryPath("Examples", "PowerForge.Web", "Search", "providers.json");

        var exitCode = WebCliCommandHandlers.HandleSubCommand(
            "provider",
            ["doctor", "--config", examplePath, "--credential", "inline-secret"],
            outputJson: true,
            logger: new WebConsoleLogger(),
            outputSchemaVersion: 1);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void Cli_ProviderDoctor_RejectsUnsupportedOutputFormats()
    {
        var examplePath = RepositoryPath("Examples", "PowerForge.Web", "Search", "providers.json");

        var exitCode = WebCliCommandHandlers.HandleSubCommand(
            "provider",
            ["doctor", "--config", examplePath, "--output", "yaml"],
            outputJson: false,
            logger: new WebConsoleLogger(),
            outputSchemaVersion: 1);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public void Cli_ProviderDoctor_RejectsRepeatedValueOptions()
    {
        var examplePath = RepositoryPath("Examples", "PowerForge.Web", "Search", "providers.json");

        var exitCode = WebCliCommandHandlers.HandleSubCommand(
            "provider",
            ["doctor", "--config", examplePath, "--output", "json", "--output", "yaml"],
            outputJson: true,
            logger: new WebConsoleLogger(),
            outputSchemaVersion: 1);

        Assert.Equal(2, exitCode);
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--output-json")]
    public void Cli_ProviderDoctor_AcceptsGlobalJsonAliases(string alias)
    {
        var examplePath = RepositoryPath("Examples", "PowerForge.Web", "Search", "providers.json");

        var exitCode = WebCliCommandHandlers.HandleSubCommand(
            "provider",
            ["doctor", "--config", examplePath, alias],
            outputJson: true,
            logger: new WebConsoleLogger(),
            outputSchemaVersion: 1);

        Assert.Equal(0, exitCode);
    }

    private static WebSearchProviderConfiguration CreateGoogleConfiguration() => new()
    {
        SchemaVersion = WebSearchProviderConfiguration.CurrentSchemaVersion,
        Sites =
        [
            new WebSearchSiteProviderConfiguration
            {
                Id = "officeimo",
                BaseUrl = "https://officeimo.com/",
                Providers =
                [
                    new WebSearchProviderRegistration
                    {
                        Id = "google-search-console",
                        Kind = "google-search-console",
                        Capabilities = [WebSearchProviderCapabilities.SearchAnalytics],
                        Credential = new WebSearchCredentialReference
                        {
                            Kind = "google-service-account-file",
                            EnvironmentVariable = "POWERFORGE_TEST_GSC"
                        },
                        Settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["property"] = "sc-domain:officeimo.com"
                        }
                    }
                ]
            }
        ]
    };

    private static WebSearchProviderConfiguration CreateBingConfiguration(string siteUrl)
    {
        var configuration = CreateGoogleConfiguration();
        configuration.Sites[0].Providers =
        [
            new WebSearchProviderRegistration
            {
                Id = "bing-webmaster",
                Kind = "bing-webmaster",
                Capabilities = [WebSearchProviderCapabilities.SearchAnalytics],
                Credential = new WebSearchCredentialReference
                {
                    Kind = "bing-api-key",
                    EnvironmentVariable = "POWERFORGE_TEST_BING"
                },
                Settings = new Dictionary<string, string?>
                {
                    ["siteUrl"] = siteUrl
                }
            }
        ];
        return configuration;
    }

    private static WebSearchProviderConfiguration CreateCloudflareConfiguration(string zoneId)
    {
        var configuration = CreateGoogleConfiguration();
        configuration.Sites[0].Providers =
        [
            new WebSearchProviderRegistration
            {
                Id = "cloudflare",
                Kind = "cloudflare-analytics",
                Capabilities = [WebSearchProviderCapabilities.TrafficAnalytics],
                Credential = new WebSearchCredentialReference
                {
                    Kind = "cloudflare-api-token",
                    EnvironmentVariable = "POWERFORGE_TEST_CLOUDFLARE"
                },
                Settings = new Dictionary<string, string?>
                {
                    ["zoneId"] = zoneId
                }
            }
        ];
        return configuration;
    }

    private static JsonSchema LoadProviderSchema() => JsonSchema.FromText(File.ReadAllText(
        RepositoryPath("Schemas", "powerforge.web.search-providers.schema.json")));

    private static string RepositoryPath(params string[] segments)
    {
        var parts = new[] { AppContext.BaseDirectory, "..", "..", "..", ".." }.Concat(segments).ToArray();
        return Path.GetFullPath(Path.Combine(parts));
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "powerforge-search-provider-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }
}
