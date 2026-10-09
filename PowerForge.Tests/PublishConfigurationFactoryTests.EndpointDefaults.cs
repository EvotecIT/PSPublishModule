using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PublishConfigurationFactoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Jfrog_defaults_and_explicit_equal_overrides_survive_json_roundtrip(bool explicitOverrides)
    {
        const string target = "https://packages.example.test/target/index.json";
        var segment = new PublishConfigurationFactory().Create(new PublishConfigurationRequest
        {
            ParameterSetName = "JFrog",
            JFrogBaseUri = "https://company.jfrog.io/artifactory",
            JFrogRepository = "feed",
            RepositorySourceUri = explicitOverrides ? target : null,
            RepositoryPublishUri = explicitOverrides ? target : null
        });
        var json = JsonSerializer.Serialize(new { Segments = new[] { segment } });
        var repository = Assert.Single(new ModulePublishConfigurationReader().ReadFromJson(json)).Repository!;
        Assert.Equal(!explicitOverrides, repository.UseProviderEndpointDefaults);
        Assert.Equal(explicitOverrides ? target : repository.Uri, ModulePublishDependencyPolicy.ReadUri(repository));
        Assert.Equal(explicitOverrides ? target : repository.Uri, ModulePublishDependencyPolicy.PSResourceGetRegistrationUri(repository));
        Assert.Equal(explicitOverrides ? RepositoryApiVersion.Auto : RepositoryApiVersion.V3, repository.ApiVersion);
        Assert.False(ModulePublishDependencyPolicy.HasSeparateEndpoints(repository));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Jfrog_profile_defaults_are_distinct_from_custom_profile_endpoints(bool customEndpoints)
    {
        var defaults = PrivateGalleryRepositoryEndpoints.Create(PrivateGalleryProvider.JFrog,
            repositoryName: "Company", jfrogBaseUri: "https://company.jfrog.io/artifactory", jfrogRepository: "feed");
        var profile = new ModuleRepositoryProfile
        {
            Provider = PrivateGalleryProvider.JFrog, RepositoryName = "Company",
            JFrogBaseUri = defaults.JFrogBaseUri!, JFrogRepository = "feed",
            RepositoryUri = defaults.PSResourceGetUri,
            RepositorySourceUri = customEndpoints ? "https://packages.example.test/target/index.json" : defaults.PowerShellGetSourceUri,
            RepositoryPublishUri = customEndpoints ? "https://packages.example.test/target/index.json" : defaults.PowerShellGetPublishUri
        };
        var repository = new PublishConfigurationFactory().Create(new PublishConfigurationRequest
        {
            ParameterSetName = "ApiKey", RepositoryName = profile.RepositoryName,
            RepositoryUri = profile.RepositoryUri, RepositorySourceUri = profile.RepositorySourceUri,
            RepositoryPublishUri = profile.RepositoryPublishUri, RepositoryProfile = profile
        }).Configuration.Repository!;
        Assert.Equal(!customEndpoints, repository.UseProviderEndpointDefaults);
        Assert.Equal(customEndpoints ? profile.RepositorySourceUri : profile.RepositoryUri, ModulePublishDependencyPolicy.ReadUri(repository));
        Assert.Equal(customEndpoints ? profile.RepositoryPublishUri : profile.RepositoryUri, ModulePublishDependencyPolicy.PSResourceGetRegistrationUri(repository));
    }
}
