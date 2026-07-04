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
    public void Create_AzureArtifactsFeedAndRepository_PreservesRepositoryAsLocalAlias()
    {
        var endpoint = PrivateGalleryRepositoryEndpoints.Create(
            PrivateGalleryProvider.AzureArtifacts,
            azureDevOpsOrganization: "contoso",
            azureDevOpsProject: "Platform",
            azureArtifactsFeed: "Modules",
            repository: "CompanyModules");

        Assert.Equal("CompanyModules", endpoint.RepositoryName);
        Assert.Equal("Modules", endpoint.Repository);
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
    public void Create_GitHubPackagesOwner_ReturnsGitHubNuGetServiceIndex()
    {
        var endpoint = PrivateGalleryRepositoryEndpoints.Create(
            PrivateGalleryProvider.GitHubPackages,
            repositoryName: "Licensing",
            gitHubOwner: "EvotecIT");

        Assert.Equal(PrivateGalleryProvider.GitHubPackages, endpoint.Provider);
        Assert.Equal("Licensing", endpoint.RepositoryName);
        Assert.Equal("EvotecIT", endpoint.Repository);
        Assert.Equal("EvotecIT", endpoint.GitHubOwner);
        Assert.Equal("https://nuget.pkg.github.com/EvotecIT/index.json", endpoint.PSResourceGetUri);
        Assert.Equal(endpoint.PSResourceGetUri, endpoint.PowerShellGetSourceUri);
        Assert.Equal(endpoint.PSResourceGetUri, endpoint.PowerShellGetPublishUri);
    }

    [Fact]
    public void Create_GitHubAliasUsesRepositoryAsOwnerWhenOwnerIsOmitted()
    {
        var endpoint = PrivateGalleryRepositoryEndpoints.Create(
            PrivateGalleryProvider.GitHub,
            repository: "EvotecIT");

        Assert.Equal(PrivateGalleryProvider.GitHubPackages, endpoint.Provider);
        Assert.Equal("EvotecIT", endpoint.RepositoryName);
        Assert.Equal("https://nuget.pkg.github.com/EvotecIT/index.json", endpoint.PSResourceGetUri);
    }

    [Fact]
    public void Create_GitHubPackagesRejectsMultiSegmentOwner()
    {
        var ex = Assert.Throws<ArgumentException>(() => PrivateGalleryRepositoryEndpoints.Create(
            PrivateGalleryProvider.GitHubPackages,
            repositoryName: "Licensing",
            gitHubOwner: "EvotecIT/Licensing"));

        Assert.Contains("single GitHub user or organization", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_GenericNuGet_RequiresExplicitRepositoryUri()
    {
        var ex = Assert.Throws<ArgumentException>(() => PrivateGalleryRepositoryEndpoints.Create(
            PrivateGalleryProvider.NuGet,
            repositoryName: "Company"));

        Assert.Contains("RepositoryUri", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_GenericNuGet_AllowsExplicitPowerShellGalleryDefault()
    {
        var endpoint = PrivateGalleryRepositoryEndpoints.Create(
            PrivateGalleryProvider.NuGet,
            repositoryName: "PSGallery",
            repositoryUri: "https://www.powershellgallery.com/api/v3/index.json");

        Assert.Equal(PrivateGalleryProvider.NuGet, endpoint.Provider);
        Assert.Equal("PSGallery", endpoint.RepositoryName);
        Assert.Equal("PSGallery", endpoint.Repository);
        Assert.Equal("https://www.powershellgallery.com/api/v3/index.json", endpoint.PSResourceGetUri);
    }

    [Fact]
    public void Create_RejectsUnknownProviderValues()
    {
        var ex = Assert.Throws<ArgumentException>(() => PrivateGalleryRepositoryEndpoints.Create(
            (PrivateGalleryProvider)999,
            repositoryName: "Company",
            repositoryUri: "https://example.test/v3/index.json"));

        Assert.Contains("not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(PrivateGalleryProvider.JFrog)]
    [InlineData(PrivateGalleryProvider.GitHubPackages)]
    [InlineData(PrivateGalleryProvider.NuGet)]
    public void Create_RejectsResolvedPowerShellGalleryName(PrivateGalleryProvider provider)
    {
        var ex = Assert.Throws<ArgumentException>(() => PrivateGalleryRepositoryEndpoints.Create(
            provider,
            repositoryName: "PSGallery",
            repository: "EvotecIT",
            repositoryUri: "https://example.test/v3/index.json"));

        Assert.Contains("PSGallery", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
