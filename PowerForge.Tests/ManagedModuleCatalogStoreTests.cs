using System.Net;
using System.Text;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleCatalogStoreTests
{
    [Fact]
    public async Task UpdateCatalog_uses_powershellgallery_v2_metadata_and_records_cdn_package_urls()
    {
        using var temp = new TemporaryDirectory();
        var requests = new List<string>();
        using var client = new HttpClient(new CatalogHandler(requests));
        var catalogPath = Path.Combine(temp.Path, "catalog.json");
        var store = new ManagedModuleCatalogStore(catalogPath, client);
        store.SetCatalog(new ManagedModuleCatalogSetRequest
        {
            Name = "PSGallery",
            Source = ManagedModuleCatalogDefaults.PowerShellGalleryV3,
            Mode = ManagedModuleCatalogCacheMode.Fallback
        });

        var result = await store.UpdateCatalogAsync(new ManagedModuleCatalogUpdateRequest
        {
            Name = "PSGallery",
            PackageNames = new[] { "Pester" }
        });

        Assert.Equal(1, result.RefreshedPackageCount);
        Assert.Equal(1, result.PackageCount);
        Assert.Equal(2, result.VersionCount);
        Assert.Contains("https://www.powershellgallery.com/api/v2/FindPackagesById()?id='Pester'&semVerLevel=2.0.0", requests);
        var catalog = Assert.Single(store.GetCatalogs());
        var package = Assert.Single(catalog.Packages);
        Assert.Equal("Pester", package.Id);
        Assert.Equal("5.7.0", package.LatestStableVersion);
        Assert.Equal("5.8.0-preview1", package.LatestPrereleaseVersion);
        var stable = Assert.Single(package.Versions, version => version.Version == "5.7.0");
        Assert.Equal("https://cdn.powershellgallery.com/packages/pester.5.7.0.nupkg", stable.CdnPackageSource);
        Assert.Equal(123456, stable.PackageSize);
        Assert.Equal("SHA512", stable.PackageHashAlgorithm);
    }

    [Fact]
    public void SetCatalog_round_trips_cache_settings()
    {
        using var temp = new TemporaryDirectory();
        var catalogPath = Path.Combine(temp.Path, "catalog.json");
        var store = new ManagedModuleCatalogStore(catalogPath);

        var saved = store.SetCatalog(new ManagedModuleCatalogSetRequest
        {
            Name = "PSGallery",
            Mode = ManagedModuleCatalogCacheMode.PreferCache,
            MaxStaleness = TimeSpan.FromDays(7),
            IncludePrerelease = false
        });

        Assert.Equal(ManagedModuleCatalogCacheMode.PreferCache, saved.Mode);
        var loaded = Assert.Single(store.GetCatalogs());
        Assert.Equal(TimeSpan.FromDays(7), loaded.MaxStaleness);
        Assert.False(loaded.IncludePrerelease);
        Assert.True(File.Exists(catalogPath));
    }

    [Fact]
    public async Task SetCatalog_clears_packages_when_source_identity_changes()
    {
        using var temp = new TemporaryDirectory();
        var catalogPath = Path.Combine(temp.Path, "catalog.json");
        using var client = new HttpClient(new CatalogHandler(new List<string>()));
        var store = new ManagedModuleCatalogStore(catalogPath, client);
        store.SetCatalog(new ManagedModuleCatalogSetRequest
        {
            Name = "Internal",
            Source = ManagedModuleCatalogDefaults.PowerShellGalleryV3,
            Mode = ManagedModuleCatalogCacheMode.Fallback
        });
        await store.UpdateCatalogAsync(new ManagedModuleCatalogUpdateRequest
        {
            Name = "Internal",
            PackageNames = new[] { "Pester" }
        });
        Assert.NotEmpty(store.GetCatalog("Internal")!.Packages);

        var updated = store.SetCatalog(new ManagedModuleCatalogSetRequest
        {
            Name = "Internal",
            Source = "https://packages.contoso.example/api/v2",
            RepositoryKind = ManagedModuleRepositoryKind.NuGetV2,
            Mode = ManagedModuleCatalogCacheMode.Fallback
        });

        Assert.Empty(updated.Packages);
        Assert.Null(updated.LastRefreshAtUtc);
    }

    [Fact]
    public async Task UpdateCatalog_propagates_caller_cancellation()
    {
        using var temp = new TemporaryDirectory();
        var catalogPath = Path.Combine(temp.Path, "catalog.json");
        using var client = new HttpClient(new CatalogHandler(new List<string>()));
        var store = new ManagedModuleCatalogStore(catalogPath, client);
        store.SetCatalog(new ManagedModuleCatalogSetRequest
        {
            Name = "PSGallery",
            Source = ManagedModuleCatalogDefaults.PowerShellGalleryV3,
            Mode = ManagedModuleCatalogCacheMode.Fallback
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => store.UpdateCatalogAsync(
            new ManagedModuleCatalogUpdateRequest
            {
                Name = "PSGallery",
                PackageNames = new[] { "Pester" }
            },
            cts.Token));
    }

    [Fact]
    public async Task UpdateCatalog_sends_repository_credentials()
    {
        using var temp = new TemporaryDirectory();
        var catalogPath = Path.Combine(temp.Path, "catalog.json");
        var handler = new AuthenticatedCatalogHandler();
        using var client = new HttpClient(handler);
        var store = new ManagedModuleCatalogStore(catalogPath, client);
        store.SetCatalog(new ManagedModuleCatalogSetRequest
        {
            Name = "Internal",
            Source = "https://packages.contoso.example/api/v2",
            RepositoryKind = ManagedModuleRepositoryKind.NuGetV2,
            Mode = ManagedModuleCatalogCacheMode.Fallback
        });

        var result = await store.UpdateCatalogAsync(new ManagedModuleCatalogUpdateRequest
        {
            Name = "Internal",
            PackageNames = new[] { "Company.Tools" },
            Credential = new RepositoryCredential
            {
                UserName = "build",
                Secret = "secret"
            }
        });

        Assert.Equal(1, result.RefreshedPackageCount);
        Assert.Equal("Basic", handler.AuthorizationScheme);
        Assert.False(string.IsNullOrWhiteSpace(handler.AuthorizationParameter));
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        private readonly List<string> _requests;

        public CatalogHandler(List<string> requests)
        {
            _requests = requests;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
            _requests.Add(uri.AbsoluteUri);
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
        <d:Authors>Pester Team</d:Authors>
        <d:Owners>pester</d:Owners>
        <d:Description>Testing framework.</d:Description>
        <d:ProjectUrl>https://pester.dev</d:ProjectUrl>
        <d:GalleryDetailsUrl>https://www.powershellgallery.com/packages/Pester/5.7.0</d:GalleryDetailsUrl>
        <d:Tags>powershell test</d:Tags>
        <d:IsPrerelease m:type="Edm.Boolean">false</d:IsPrerelease>
        <d:IsLatestVersion m:type="Edm.Boolean">true</d:IsLatestVersion>
        <d:IsAbsoluteLatestVersion m:type="Edm.Boolean">false</d:IsAbsoluteLatestVersion>
        <d:Published m:type="Edm.DateTime">2025-01-01T00:00:00</d:Published>
        <d:DownloadCount m:type="Edm.Int32">1000</d:DownloadCount>
        <d:VersionDownloadCount m:type="Edm.Int32">100</d:VersionDownloadCount>
        <d:PackageSize m:type="Edm.Int64">123456</d:PackageSize>
        <d:PackageHash>hash</d:PackageHash>
        <d:PackageHashAlgorithm>SHA512</d:PackageHashAlgorithm>
      </m:properties>
    </content>
  </entry>
  <entry>
    <content type="application/zip" src="https://www.powershellgallery.com/api/v2/package/Pester/5.8.0-preview1">
      <m:properties>
        <d:Id>Pester</d:Id>
        <d:Version>5.8.0-preview1</d:Version>
        <d:NormalizedVersion>5.8.0-preview1</d:NormalizedVersion>
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

    private sealed class AuthenticatedCatalogHandler : HttpMessageHandler
    {
        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationParameter { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CreateCompanyToolsFeed(), Encoding.UTF8, "application/xml")
            });
        }

        private static string CreateCompanyToolsFeed()
            => """
<?xml version="1.0" encoding="utf-8"?>
<feed xmlns="http://www.w3.org/2005/Atom"
      xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices"
      xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata">
  <entry>
    <content type="application/zip" src="https://packages.contoso.example/api/v2/package/Company.Tools/1.0.0">
      <m:properties>
        <d:Id>Company.Tools</d:Id>
        <d:Version>1.0.0</d:Version>
        <d:NormalizedVersion>1.0.0</d:NormalizedVersion>
        <d:IsPrerelease m:type="Edm.Boolean">false</d:IsPrerelease>
        <d:Published m:type="Edm.DateTime">2025-01-01T00:00:00</d:Published>
      </m:properties>
    </content>
  </entry>
</feed>
""";
    }
}
