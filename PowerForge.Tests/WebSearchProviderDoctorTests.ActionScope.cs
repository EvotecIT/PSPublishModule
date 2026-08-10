using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebSearchProviderDoctorTests
{
    [Fact]
    public void Doctor_RejectsCloudflareZoneIdsWithNoncanonicalWhitespace()
    {
        var configuration = CreateCloudflareConfiguration(" abcdef0123456789abcdef0123456789 ");

        var result = WebSearchProviderDoctor.Inspect(configuration, _ => "credential-value");

        Assert.False(result.Success);
        Assert.Contains(result.Checks, check => check.Code == "provider.cloudflare-zone-invalid");
        Assert.Null(result.ConfigurationHash);
    }

    [Fact]
    public void ProviderActionDoctor_IgnoresUnrelatedRegistrationsAndKeepsScopedIdentity()
    {
        var configuration = CreateGoogleConfiguration();
        var site = configuration.Sites[0];
        var provider = site.Providers[0];
        var first = WebSearchProviderDoctor.InspectProviderAction(
            configuration,
            site,
            provider,
            WebSearchCollectorCatalog.AvailableCapabilities,
            _ => "credential");
        configuration.Sites =
        [
            .. configuration.Sites,
            new WebSearchSiteProviderConfiguration
            {
                Id = "broken",
                BaseUrl = "not-a-url",
                Providers =
                [
                    new WebSearchProviderRegistration
                    {
                        Id = "broken",
                        Kind = "unknown",
                        Capabilities = ["unknown"]
                    }
                ]
            }
        ];

        var fleet = WebSearchProviderDoctor.InspectWithCapabilities(
            configuration,
            WebSearchCollectorCatalog.AvailableCapabilities,
            _ => "credential");
        var second = WebSearchProviderDoctor.InspectProviderAction(
            configuration,
            site,
            provider,
            WebSearchCollectorCatalog.AvailableCapabilities,
            _ => "credential");

        Assert.False(fleet.Success);
        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(first.ConfigurationHash, second.ConfigurationHash);
    }
}
