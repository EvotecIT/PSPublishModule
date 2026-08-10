using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebCloudflareAnalyticsCollectorTests
{
    [Fact]
    public void ProviderActionInspection_IgnoresUnrelatedProviderCredentialsForCloudflareCollection()
    {
        var configuration = CreateConfiguration();
        configuration.Sites[0].Providers =
        [
            .. configuration.Sites[0].Providers,
            new WebSearchProviderRegistration
            {
                Id = "google",
                Kind = GoogleSearchConsoleCollector.ProviderKind,
                Enabled = true,
                Capabilities = [WebSearchProviderCapabilities.SearchAnalytics],
                Credential = new WebSearchCredentialReference
                {
                    Kind = "google-service-account-json",
                    EnvironmentVariable = "POWERFORGE_TEST_UNRELATED_GSC_UNAVAILABLE"
                },
                Settings = new Dictionary<string, string?> { ["property"] = "sc-domain:officeimo.com" }
            }
        ];
        var site = configuration.Sites[0];
        var cloudflare = site.Providers.Single(value => value.Id == "cloudflare");

        var result = WebCliCommandHandlers.InspectProviderAction(
            configuration,
            site,
            cloudflare,
            WebSearchProviderCapabilities.TrafficAnalytics,
            useSelectedCredential: true,
            environmentReader: name => name == "POWERFORGE_TEST_CLOUDFLARE_TOKEN_UNAVAILABLE" ? "token" : null);

        Assert.True(result.Success);
        Assert.True(result.Providers.Single(value => value.ProviderId == "cloudflare").CollectionReady);
    }
}
