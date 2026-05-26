using System;
using Xunit;

namespace PowerForge.Tests;

public sealed class PrivateGalleryRepositoryEndpointsTests
{
    [Fact]
    public void Create_AzureAlias_ReturnsAzureArtifactsEndpoint()
    {
        var endpoint = PrivateGalleryRepositoryEndpoints.Create(
            PrivateGalleryProvider.Azure,
            azureDevOpsOrganization: "contoso",
            azureDevOpsProject: "Platform",
            repository: "Modules",
            repositoryName: "Company");

        Assert.Equal(PrivateGalleryProvider.AzureArtifacts, endpoint.Provider);
        Assert.Equal("Company", endpoint.RepositoryName);
        Assert.Equal("Modules", endpoint.Repository);
        Assert.Equal("https://pkgs.dev.azure.com/contoso/Platform/_packaging/Modules/nuget/v3/index.json", endpoint.PSResourceGetUri);
    }

    [Fact]
    public void Create_JFrogBaseAndRepository_ReturnsV2AndV3Uris()
    {
        var endpoint = PrivateGalleryRepositoryEndpoints.Create(
            PrivateGalleryProvider.JFrog,
            repository: "powershell-virtual",
            jfrogBaseUri: "https://company.jfrog.io/artifactory/");

        Assert.Equal(PrivateGalleryProvider.JFrog, endpoint.Provider);
        Assert.Equal("powershell-virtual", endpoint.RepositoryName);
        Assert.Equal("powershell-virtual", endpoint.Repository);
        Assert.Equal("https://company.jfrog.io/artifactory/api/nuget/powershell-virtual", endpoint.PowerShellGetSourceUri);
        Assert.Equal("https://company.jfrog.io/artifactory/api/nuget/powershell-virtual", endpoint.PowerShellGetPublishUri);
        Assert.Equal("https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json", endpoint.PSResourceGetUri);
        Assert.Equal("https://company.jfrog.io/artifactory", endpoint.JFrogBaseUri);
        Assert.Equal("powershell-virtual", endpoint.JFrogRepository);
    }

    [Fact]
    public void Create_JFrogExplicitUri_UsesUriForAllClientsWhenNoV2UriIsProvided()
    {
        var endpoint = PrivateGalleryRepositoryEndpoints.Create(
            PrivateGalleryProvider.JFrog,
            repositoryName: "Company",
            repositoryUri: "https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json");

        Assert.Equal("Company", endpoint.RepositoryName);
        Assert.Equal("https://company.jfrog.io/artifactory/api/nuget/v3/powershell-virtual/index.json", endpoint.PSResourceGetUri);
        Assert.Equal(endpoint.PSResourceGetUri, endpoint.PowerShellGetSourceUri);
        Assert.Equal(endpoint.PSResourceGetUri, endpoint.PowerShellGetPublishUri);
    }

    [Fact]
    public void Create_GenericNuGet_RequiresExplicitRepositoryUri()
    {
        var ex = Assert.Throws<ArgumentException>(() => PrivateGalleryRepositoryEndpoints.Create(
            PrivateGalleryProvider.NuGet,
            repositoryName: "Company"));

        Assert.Contains("RepositoryUri", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(PrivateGalleryProvider.JFrog)]
    [InlineData(PrivateGalleryProvider.NuGet)]
    public void Create_RejectsResolvedPowerShellGalleryName(PrivateGalleryProvider provider)
    {
        var ex = Assert.Throws<ArgumentException>(() => PrivateGalleryRepositoryEndpoints.Create(
            provider,
            repositoryName: "PSGallery",
            repositoryUri: "https://example.test/v3/index.json"));

        Assert.Contains("PSGallery", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
