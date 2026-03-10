using Xunit;

namespace PowerForge.Tests;

public sealed class AzureArtifactsRepositoryEndpointsTests
{
    [Fact]
    public void Create_ProjectScopedFeed_ReturnsExpectedUris()
    {
        var endpoint = AzureArtifactsRepositoryEndpoints.Create(
            organization: "contoso",
            project: "Platform",
            feed: "Modules");

        Assert.Equal("Modules", endpoint.RepositoryName);
        Assert.Equal("https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v2", endpoint.PowerShellGetSourceUri);
        Assert.Equal("https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v2", endpoint.PowerShellGetPublishUri);
        Assert.Equal("https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v3/index.json", endpoint.PSResourceGetUri);
    }

    [Fact]
    public void Create_OrganizationScopedFeed_ReturnsExpectedUris()
    {
        var endpoint = AzureArtifactsRepositoryEndpoints.Create(
            organization: "contoso",
            project: null,
            feed: "Modules",
            repositoryName: "Internal");

        Assert.Equal("Internal", endpoint.RepositoryName);
        Assert.Equal("https://pkgs.dev.azure.com/contoso/_packaging/Modules/nuget/v2", endpoint.PowerShellGetSourceUri);
        Assert.Equal("https://pkgs.dev.azure.com/contoso/_packaging/Modules/nuget/v2", endpoint.PowerShellGetPublishUri);
        Assert.Equal("https://pkgs.dev.azure.com/contoso/_packaging/Modules/nuget/v3/index.json", endpoint.PSResourceGetUri);
    }

    [Fact]
    public void CreatePublishRepositoryConfiguration_DefaultsPsResourceGetToV3()
    {
        var repository = AzureArtifactsRepositoryEndpoints.CreatePublishRepositoryConfiguration(
            organization: "contoso",
            project: "Platform",
            feed: "Modules",
            apiVersion: RepositoryApiVersion.Auto);

        Assert.Equal("Modules", repository.Name);
        Assert.Equal(RepositoryApiVersion.V3, repository.ApiVersion);
        Assert.Equal("https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v2", repository.SourceUri);
        Assert.Equal("https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v2", repository.PublishUri);
        Assert.Equal("https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v3/index.json", repository.Uri);
    }
}
