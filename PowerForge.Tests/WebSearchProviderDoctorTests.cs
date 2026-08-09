using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebSearchProviderDoctorTests
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
            Capabilities = [WebSearchProviderCapabilities.SearchAnalytics]
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
        first.Sites[0].Providers[0].Settings["password"] = "first-candidate";
        var second = CreateGoogleConfiguration();
        second.Sites[0].Providers[0].Settings["password"] = "second-candidate";

        Assert.Equal(
            WebSearchProviderConfigurationFingerprint.Compute(first),
            WebSearchProviderConfigurationFingerprint.Compute(second));
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
    [InlineData("sc-domain:")]
    [InlineData("sc-domain:https://officeimo.com")]
    [InlineData("sc-domain:bad value")]
    [InlineData("SC-DOMAIN:officeimo.com")]
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
