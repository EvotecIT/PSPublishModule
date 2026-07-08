using System.Net;
using System.Text;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleRepositoryClientCatalogTests
{
    [Fact]
    public async Task GetVersionsAsync_uses_managed_catalog_fallback_when_live_gallery_metadata_fails()
    {
        using var temp = new TemporaryDirectory();
        var catalogPath = await CreatePesterCatalogAsync(temp.Path, ManagedModuleCatalogCacheMode.Fallback);
        using var client = new HttpClient(new FailingGalleryHandler());
        var repositoryClient = new ManagedModuleRepositoryClient(
            new NullLogger(),
            client,
            options: CreateCatalogOptions(temp.Path, catalogPath));
        var repository = CreatePowerShellGalleryRepository();

        var versions = await repositoryClient.GetVersionsAsync(repository, "Pester", includePrerelease: false);

        Assert.Equal(new[] { "5.7.0" }, versions.Select(version => version.Version));
        Assert.Equal("https://cdn.powershellgallery.com/packages/pester.5.7.0.nupkg", Assert.Single(versions).PackageSource);
    }

    [Fact]
    public async Task GetLatestVersionAsync_uses_managed_catalog_fallback_when_live_gallery_metadata_fails()
    {
        using var temp = new TemporaryDirectory();
        var catalogPath = await CreatePesterCatalogAsync(temp.Path, ManagedModuleCatalogCacheMode.Fallback);
        using var client = new HttpClient(new FailingGalleryHandler());
        var repositoryClient = new ManagedModuleRepositoryClient(
            new NullLogger(),
            client,
            options: CreateCatalogOptions(temp.Path, catalogPath));
        var repository = CreatePowerShellGalleryRepository();

        var latest = await repositoryClient.GetLatestVersionAsync(repository, "Pester", includePrerelease: true);

        Assert.NotNull(latest);
        Assert.Equal("5.8.0-preview1", latest.Version);
        Assert.Equal("https://cdn.powershellgallery.com/packages/pester.5.8.0-preview1.nupkg", latest.PackageSource);
    }

    [Fact]
    public async Task SearchPackagesAsync_uses_managed_catalog_fallback_when_live_gallery_search_fails()
    {
        using var temp = new TemporaryDirectory();
        var catalogPath = await CreatePesterCatalogAsync(temp.Path, ManagedModuleCatalogCacheMode.Fallback);
        using var client = new HttpClient(new FailingGalleryHandler());
        var repositoryClient = new ManagedModuleRepositoryClient(
            new NullLogger(),
            client,
            options: CreateCatalogOptions(temp.Path, catalogPath));
        var repository = CreatePowerShellGalleryRepository();

        var results = await repositoryClient.SearchPackagesAsync(repository, "Pest*", includePrerelease: false, take: 10);

        var result = Assert.Single(results);
        Assert.Equal("Pester", result.Name);
        Assert.Equal("5.7.0", result.Version);
    }

    [Fact]
    public async Task GetVersionsAsync_uses_managed_catalog_offline_without_live_gallery_request()
    {
        using var temp = new TemporaryDirectory();
        var catalogPath = await CreatePesterCatalogAsync(temp.Path, ManagedModuleCatalogCacheMode.Offline);
        using var client = new HttpClient(new ThrowingHandler());
        var repositoryClient = new ManagedModuleRepositoryClient(
            new NullLogger(),
            client,
            options: CreateCatalogOptions(temp.Path, catalogPath));
        var repository = CreatePowerShellGalleryRepository();

        var versions = await repositoryClient.GetVersionsAsync(repository, "Pester", includePrerelease: true);

        Assert.Equal(new[] { "5.7.0", "5.8.0-preview1" }, versions.Select(version => version.Version));
    }

    private static async Task<string> CreatePesterCatalogAsync(string tempRoot, ManagedModuleCatalogCacheMode mode)
    {
        var catalogPath = Path.Combine(tempRoot, "catalog.json");
        using var client = new HttpClient(new CatalogRefreshHandler());
        var store = new ManagedModuleCatalogStore(catalogPath, client);
        store.SetCatalog(new ManagedModuleCatalogSetRequest
        {
            Name = "PSGallery",
            Source = ManagedModuleCatalogDefaults.PowerShellGalleryV3,
            Mode = mode,
            MaxStaleness = TimeSpan.FromDays(30),
            IncludePrerelease = true
        });
        await store.UpdateCatalogAsync(new ManagedModuleCatalogUpdateRequest
        {
            Name = "PSGallery",
            PackageNames = new[] { "Pester" }
        });
        return catalogPath;
    }

    private static ManagedModuleRepositoryClientOptions CreateCatalogOptions(string tempRoot, string catalogPath)
        => new()
        {
            ManagedModuleCatalogPath = catalogPath,
            MachineManagedModuleCatalogPath = Path.Combine(tempRoot, "machine-catalog.json"),
            RetryDelay = TimeSpan.Zero,
            MaxRetries = 0
        };

    private static ManagedModuleRepository CreatePowerShellGalleryRepository()
        => new("PSGallery", ManagedModuleCatalogDefaults.PowerShellGalleryV3);

    private sealed class FailingGalleryHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Live metadata should not be queried in offline catalog mode.");
    }

    private sealed class CatalogRefreshHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
            if (uri.AbsoluteUri == "https://www.powershellgallery.com/api/v2/FindPackagesById()?id='Pester'&semVerLevel=2.0.0")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CreatePesterFeed(), Encoding.UTF8, "application/xml")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static string CreatePesterFeed()
            => """
<?xml version="1.0" encoding="utf-8"?>
<feed xmlns="http://www.w3.org/2005/Atom"
      xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
      xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
  <entry>
    <content type="application/zip" src="https://www.powershellgallery.com/api/v2/package/Pester/5.7.0">
      <m:properties>
        <d:Id>Pester</d:Id>
        <d:Version>5.7.0</d:Version>
        <d:NormalizedVersion>5.7.0</d:NormalizedVersion>
        <d:Tags>powershell test</d:Tags>
        <d:IsPrerelease m:type="Edm.Boolean">false</d:IsPrerelease>
        <d:IsLatestVersion m:type="Edm.Boolean">true</d:IsLatestVersion>
        <d:IsAbsoluteLatestVersion m:type="Edm.Boolean">false</d:IsAbsoluteLatestVersion>
        <d:Published m:type="Edm.DateTime">2025-01-01T00:00:00</d:Published>
      </m:properties>
    </content>
  </entry>
  <entry>
    <content type="application/zip" src="https://www.powershellgallery.com/api/v2/package/Pester/5.8.0-preview1">
      <m:properties>
        <d:Id>Pester</d:Id>
        <d:Version>5.8.0-preview1</d:Version>
        <d:NormalizedVersion>5.8.0-preview1</d:NormalizedVersion>
        <d:Tags>powershell test</d:Tags>
        <d:IsPrerelease m:type="Edm.Boolean">true</d:IsPrerelease>
        <d:IsLatestVersion m:type="Edm.Boolean">false</d:IsLatestVersion>
        <d:IsAbsoluteLatestVersion m:type="Edm.Boolean">true</d:IsAbsoluteLatestVersion>
        <d:Published m:type="Edm.DateTime">2025-02-01T00:00:00</d:Published>
      </m:properties>
    </content>
  </entry>
</feed>
""";
    }
}
