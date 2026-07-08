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
    public async Task SearchPackagesAsync_merges_catalog_matches_across_user_and_machine_scopes()
    {
        using var temp = new TemporaryDirectory();
        var userCatalogPath = await CreateCatalogAsync(
            temp.Path,
            "Pester",
            "5.7.0",
            ManagedModuleCatalogCacheMode.Fallback);
        var machineCatalogPath = await CreateCatalogAsync(
            Path.Combine(temp.Path, "machine"),
            "PSReadLine",
            "2.3.6",
            ManagedModuleCatalogCacheMode.Fallback);
        using var client = new HttpClient(new FailingGalleryHandler());
        var repositoryClient = new ManagedModuleRepositoryClient(
            new NullLogger(),
            client,
            options: new ManagedModuleRepositoryClientOptions
            {
                ManagedModuleCatalogPath = userCatalogPath,
                MachineManagedModuleCatalogPath = machineCatalogPath,
                RetryDelay = TimeSpan.Zero,
                MaxRetries = 0
            });
        var repository = CreatePowerShellGalleryRepository();

        var results = await repositoryClient.SearchPackagesAsync(repository, "P*", includePrerelease: false, take: 10);

        Assert.Equal(new[] { "Pester", "PSReadLine" }, results.Select(result => result.Name).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase));
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

    [Fact]
    public async Task GetVersionsAsync_falls_through_to_machine_catalog_when_user_catalog_has_no_package()
    {
        using var temp = new TemporaryDirectory();
        var userCatalogPath = Path.Combine(temp.Path, "user-catalog.json");
        var machineCatalogPath = await CreatePesterCatalogAsync(
            Path.Combine(temp.Path, "machine"),
            ManagedModuleCatalogCacheMode.Fallback);
        CreateEmptyCatalog(userCatalogPath, ManagedModuleCatalogCacheMode.Fallback);
        using var client = new HttpClient(new FailingGalleryHandler());
        var repositoryClient = new ManagedModuleRepositoryClient(
            new NullLogger(),
            client,
            options: new ManagedModuleRepositoryClientOptions
            {
                ManagedModuleCatalogPath = userCatalogPath,
                MachineManagedModuleCatalogPath = machineCatalogPath,
                RetryDelay = TimeSpan.Zero,
                MaxRetries = 0
            });
        var repository = CreatePowerShellGalleryRepository();

        var versions = await repositoryClient.GetVersionsAsync(repository, "Pester", includePrerelease: false);

        Assert.Equal(new[] { "5.7.0" }, versions.Select(version => version.Version));
    }

    [Fact]
    public async Task GetVersionsAsync_ignores_catalog_with_matching_name_but_different_source()
    {
        using var temp = new TemporaryDirectory();
        var catalogPath = await CreateCatalogAsync(
            temp.Path,
            "Pester",
            "5.7.0",
            ManagedModuleCatalogCacheMode.Fallback,
            source: "https://packages.contoso.example/api/v2",
            repositoryKind: ManagedModuleRepositoryKind.NuGetV2);
        using var client = new HttpClient(new FailingGalleryHandler());
        var repositoryClient = new ManagedModuleRepositoryClient(
            new NullLogger(),
            client,
            options: CreateCatalogOptions(temp.Path, catalogPath));
        var repository = CreatePowerShellGalleryRepository();

        await Assert.ThrowsAsync<ManagedModuleRepositoryException>(() => repositoryClient.GetVersionsAsync(repository, "Pester", includePrerelease: false));
    }

    [Fact]
    public async Task GetVersionsAsync_does_not_fallback_when_live_query_succeeds_with_no_versions()
    {
        using var temp = new TemporaryDirectory();
        var catalogPath = await CreatePesterCatalogAsync(temp.Path, ManagedModuleCatalogCacheMode.Fallback);
        using var client = new HttpClient(new EmptyGalleryHandler());
        var repositoryClient = new ManagedModuleRepositoryClient(
            new NullLogger(),
            client,
            options: CreateCatalogOptions(temp.Path, catalogPath));
        var repository = CreatePowerShellGalleryRepository();

        var versions = await repositoryClient.GetVersionsAsync(repository, "Pester", includePrerelease: false);

        Assert.Empty(versions);
    }

    [Fact]
    public async Task GetVersionsAsync_readthrough_catalog_warms_after_successful_live_query()
    {
        using var temp = new TemporaryDirectory();
        var catalogPath = Path.Combine(temp.Path, "catalog.json");
        CreateEmptyCatalog(catalogPath, ManagedModuleCatalogCacheMode.ReadThrough);
        var handler = new CatalogRefreshHandler();
        using var client = new HttpClient(handler);
        var repositoryClient = new ManagedModuleRepositoryClient(
            new NullLogger(),
            client,
            options: CreateCatalogOptions(temp.Path, catalogPath));
        var repository = CreatePowerShellGalleryRepository();

        var versions = await repositoryClient.GetVersionsAsync(repository, "Pester", includePrerelease: false);

        Assert.Equal(new[] { "5.7.0" }, versions.Select(version => version.Version));
        Assert.True(handler.RequestCount >= 2);
        var warmed = new ManagedModuleCatalogStore(catalogPath).GetCatalog("PSGallery");
        Assert.NotNull(warmed);
        Assert.Contains(warmed.Packages, package => package.Id == "Pester");
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

    private static async Task<string> CreateCatalogAsync(
        string tempRoot,
        string packageName,
        string stableVersion,
        ManagedModuleCatalogCacheMode mode,
        string? source = null,
        ManagedModuleRepositoryKind repositoryKind = ManagedModuleRepositoryKind.NuGetV3)
    {
        var catalogPath = Path.Combine(tempRoot, "catalog.json");
        using var client = new HttpClient(new CatalogRefreshHandler(packageName, stableVersion, includePrerelease: false));
        var store = new ManagedModuleCatalogStore(catalogPath, client);
        store.SetCatalog(new ManagedModuleCatalogSetRequest
        {
            Name = "PSGallery",
            Source = source ?? ManagedModuleCatalogDefaults.PowerShellGalleryV3,
            RepositoryKind = repositoryKind,
            Mode = mode,
            MaxStaleness = TimeSpan.FromDays(30),
            IncludePrerelease = true
        });
        await store.UpdateCatalogAsync(new ManagedModuleCatalogUpdateRequest
        {
            Name = "PSGallery",
            PackageNames = new[] { packageName }
        });
        return catalogPath;
    }

    private static void CreateEmptyCatalog(string catalogPath, ManagedModuleCatalogCacheMode mode)
    {
        var store = new ManagedModuleCatalogStore(catalogPath);
        store.SetCatalog(new ManagedModuleCatalogSetRequest
        {
            Name = "PSGallery",
            Source = ManagedModuleCatalogDefaults.PowerShellGalleryV3,
            Mode = mode,
            MaxStaleness = TimeSpan.FromDays(30),
            IncludePrerelease = true
        });
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

    private sealed class EmptyGalleryHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
<?xml version="1.0" encoding="utf-8"?>
<feed xmlns="http://www.w3.org/2005/Atom"
      xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
      xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata" />
""", Encoding.UTF8, "application/xml")
            });
    }

    private sealed class CatalogRefreshHandler : HttpMessageHandler
    {
        private readonly string _packageName;
        private readonly string _stableVersion;
        private readonly bool _includePrerelease;

        public CatalogRefreshHandler()
            : this("Pester", "5.7.0", includePrerelease: true)
        {
        }

        public CatalogRefreshHandler(string packageName, string stableVersion, bool includePrerelease)
        {
            _packageName = packageName;
            _stableVersion = stableVersion;
            _includePrerelease = includePrerelease;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
            if (uri.AbsoluteUri.Contains($"id='{_packageName}'", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(CreateFeed(_packageName, _stableVersion, _includePrerelease), Encoding.UTF8, "application/xml")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static string CreateFeed(string packageName, string stableVersion, bool includePrerelease)
        {
            var prereleaseEntry = includePrerelease
                ? $"""
  <entry>
    <content type="application/zip" src="https://www.powershellgallery.com/api/v2/package/{packageName}/5.8.0-preview1">
      <m:properties>
        <d:Id>{packageName}</d:Id>
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
"""
                : string.Empty;

            return $"""
<?xml version="1.0" encoding="utf-8"?>
<feed xmlns="http://www.w3.org/2005/Atom"
      xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
      xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
  <entry>
    <content type="application/zip" src="https://www.powershellgallery.com/api/v2/package/{packageName}/{stableVersion}">
      <m:properties>
        <d:Id>{packageName}</d:Id>
        <d:Version>{stableVersion}</d:Version>
        <d:NormalizedVersion>{stableVersion}</d:NormalizedVersion>
        <d:Tags>powershell test</d:Tags>
        <d:IsPrerelease m:type="Edm.Boolean">false</d:IsPrerelease>
        <d:IsLatestVersion m:type="Edm.Boolean">true</d:IsLatestVersion>
        <d:IsAbsoluteLatestVersion m:type="Edm.Boolean">false</d:IsAbsoluteLatestVersion>
        <d:Published m:type="Edm.DateTime">2025-01-01T00:00:00</d:Published>
      </m:properties>
    </content>
  </entry>
{prereleaseEntry}
</feed>
""";
        }
    }
}
