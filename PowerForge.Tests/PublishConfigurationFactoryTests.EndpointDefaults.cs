using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PublishConfigurationFactoryTests
{
    [Theory]
    [InlineData("https://company.jfrog.io/artifactory/api/nuget/feed")]
    [InlineData("https://company.jfrog.io/artifactory/api/nuget/v3/feed/index.json")]
    public void Jfrog_explicit_repository_uri_retains_auto_protocol_selection(string repositoryUri)
    {
        var repository = new PublishConfigurationFactory().Create(new PublishConfigurationRequest
        {
            ParameterSetName = "JFrog", JFrogBaseUri = "https://company.jfrog.io/artifactory", JFrogRepository = "feed",
            RepositoryUri = repositoryUri
        }).Configuration.Repository!;
        Assert.Equal(RepositoryApiVersion.Auto, repository.ApiVersion);
        Assert.Equal(repositoryUri, ModulePublishDependencyPolicy.ReadUri(repository));
        Assert.Equal(repositoryUri, ModulePublishDependencyPolicy.PSResourceGetRegistrationUri(repository));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Jfrog_single_override_leaves_the_other_operation_at_repository_uri(bool sourceOverride)
    {
        const string target = "https://packages.example.test/override/index.json";
        var repository = new PublishConfigurationFactory().Create(new PublishConfigurationRequest
        {
            ParameterSetName = "JFrog", JFrogBaseUri = "https://company.jfrog.io/artifactory", JFrogRepository = "feed",
            RepositorySourceUri = sourceOverride ? target : null,
            RepositoryPublishUri = sourceOverride ? null : target
        }).Configuration.Repository!;
        Assert.False(repository.UseProviderEndpointDefaults);
        Assert.Equal(sourceOverride ? target : repository.Uri, ModulePublishDependencyPolicy.ReadUri(repository));
        Assert.Equal(sourceOverride ? repository.Uri : target, ModulePublishDependencyPolicy.PSResourceGetRegistrationUri(repository));
        Assert.True(ModulePublishDependencyPolicy.HasSeparateEndpoints(repository));
    }

    [Theory]
    [InlineData(PrivateGalleryProvider.JFrog, false)]
    [InlineData(PrivateGalleryProvider.JFrog, true)]
    [InlineData(PrivateGalleryProvider.GitHubPackages, false)]
    [InlineData(PrivateGalleryProvider.GitHubPackages, true)]
    [InlineData(PrivateGalleryProvider.NuGet, false)]
    [InlineData(PrivateGalleryProvider.NuGet, true)]
    public void Normalized_profiles_preserve_independent_single_endpoint_overrides(PrivateGalleryProvider provider, bool sourceOverride)
    {
        const string target = "https://packages.example.test/override/index.json";
        const string repositoryUri = "https://packages.example.test/default/index.json";
        var profile = ModuleRepositoryProfileStore.Normalize(new ModuleRepositoryProfile
        {
            Name = "Company", Provider = provider, RepositoryName = "Company", RepositoryUri = repositoryUri,
            RepositorySourceUri = sourceOverride ? target : string.Empty, RepositoryPublishUri = sourceOverride ? string.Empty : target,
            GitHubOwner = "Company", JFrogBaseUri = "https://company.jfrog.io/artifactory", JFrogRepository = "feed"
        });
        var repository = new PublishConfigurationFactory().Create(new PublishConfigurationRequest
        {
            ParameterSetName = "ApiKey", RepositoryProfile = profile, RepositoryName = profile.RepositoryName,
            RepositoryUri = profile.RepositoryUri, RepositorySourceUri = profile.RepositorySourceUri,
            RepositoryPublishUri = profile.RepositoryPublishUri
        }).Configuration.Repository!;
        Assert.False(repository.UseProviderEndpointDefaults);
        Assert.Equal(sourceOverride ? target : repositoryUri, ModulePublishDependencyPolicy.ReadUri(repository));
        Assert.Equal(sourceOverride ? repositoryUri : target, ModulePublishDependencyPolicy.PSResourceGetRegistrationUri(repository));
        Assert.True(ModulePublishDependencyPolicy.HasSeparateEndpoints(repository));
    }

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
