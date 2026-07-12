using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleRepositoryClientTests
{
    [Theory]
    [InlineData("packages/feed")]
    [InlineData(@"packages\feed")]
    public void ManagedModuleRepository_infers_relative_folder_feeds_as_local(string source)
    {
        var repository = new ManagedModuleRepository("Local", source);

        Assert.Equal(ManagedModuleRepositoryKind.LocalFolder, repository.Kind);
    }

    [Theory]
    [InlineData("https://example.test/api/v2")]
    [InlineData("https://example.test/api/v2/items/psscript")]
    [InlineData("https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v2")]
    [InlineData("https://example.test/packages/v2")]
    public void ManagedModuleRepository_infers_common_v2_feed_paths(string source)
    {
        var repository = new ManagedModuleRepository("Feed", source);

        Assert.Equal(ManagedModuleRepositoryKind.NuGetV2, repository.Kind);
    }

    [Fact]
    public async Task DownloadPackageAsync_uses_v2_package_endpoint_for_script_feed_sources()
    {
        using var destination = new TemporaryDirectory();
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("ScriptFeed", "https://example.test/api/v2/items/psscript");

        var result = await repositoryClient.DownloadPackageAsync(repository, "Company.Tools", "1.1.0", destination.Path);

        Assert.Equal("https://example.test/api/v2/package/Company.Tools/1.1.0", result.Source);
        Assert.Contains(requests, request => request.Url == "https://example.test/api/v2/package/Company.Tools/1.1.0");
        Assert.DoesNotContain(requests, request => request.Url.Contains("/items/psscript/package/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetVersionsAsync_uses_nuget_v3_package_base_address()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var versions = await repositoryClient.GetVersionsAsync(repository, "Company.Tools", includePrerelease: false);

        Assert.Equal(new[] { "1.0.0", "1.1.0" }, versions.Select(version => version.Version));
        Assert.All(versions, version =>
        {
            Assert.Equal("Gallery", version.RepositoryName);
            Assert.StartsWith("https://example.test/packages/company.tools/", version.PackageSource, StringComparison.OrdinalIgnoreCase);
            Assert.False(version.IsPrerelease);
        });
        Assert.Contains(requests, request => request.Url == "https://example.test/v3/index.json");
        Assert.Contains(requests, request => request.Url == "https://example.test/packages/company.tools/index.json");
        Assert.All(requests, request => Assert.Contains("PowerForge-ManagedModule/1.0", request.UserAgent, StringComparison.Ordinal));
        Assert.Equal(2, repositoryClient.RequestCount);
    }

    [Fact]
    public async Task GetVersionsAsync_keeps_case_distinct_service_index_cache_entries()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);

        var upperVersions = await repositoryClient.GetVersionsAsync(
            new ManagedModuleRepository("Upper", "https://EXAMPLE.test/Feed/v3/index.json"),
            "Company.Tools",
            includePrerelease: false);
        var lowerVersions = await repositoryClient.GetVersionsAsync(
            new ManagedModuleRepository("Lower", "https://example.test/feed/v3/index.json"),
            "Company.Tools",
            includePrerelease: false);

        Assert.Equal(new[] { "1.0.0" }, upperVersions.Select(version => version.Version));
        Assert.Equal(new[] { "2.0.0" }, lowerVersions.Select(version => version.Version));
        Assert.Contains(requests, request => request.Url == "https://example.test/Feed/v3/index.json");
        Assert.Contains(requests, request => request.Url == "https://example.test/feed/v3/index.json");
        Assert.Contains(requests, request => request.Url == "https://example.test/Feed/packages/company.tools/index.json");
        Assert.Contains(requests, request => request.Url == "https://example.test/feed/packages/company.tools/index.json");
    }

    [Fact]
    public async Task GetVersionsAsync_treats_trailing_slash_service_index_as_v3_index()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json/");

        var versions = await repositoryClient.GetVersionsAsync(repository, "Company.Tools", includePrerelease: false);

        Assert.Equal(new[] { "1.0.0", "1.1.0" }, versions.Select(version => version.Version));
        Assert.Contains(requests, request => request.Url == "https://example.test/v3/index.json");
        Assert.Contains(requests, request => request.Url == "https://example.test/packages/company.tools/index.json");
        Assert.DoesNotContain(requests, request => request.Url == "https://example.test/v3/index.json/company.tools/index.json");
    }

    [Fact]
    public async Task GetVersionsAsync_returns_empty_when_nuget_v3_package_is_absent()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var versions = await repositoryClient.GetVersionsAsync(repository, "Missing.Tools", includePrerelease: false);

        Assert.Empty(versions);
        Assert.Contains(requests, request => request.Url == "https://example.test/packages/missing.tools/index.json");
    }

    [Fact]
    public async Task GetVersionsAsync_uses_powershellgallery_v2_read_api_for_canonical_default()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository(
            "PSGallery",
            ManagedModuleCatalogDefaults.PowerShellGalleryV2);

        var versions = await repositoryClient.GetVersionsAsync(repository, "Pester", includePrerelease: false);

        Assert.Equal(new[] { "5.6.1", "5.7.0" }, versions.Select(version => version.Version));
        Assert.All(versions, version =>
        {
            Assert.Equal("PSGallery", version.RepositoryName);
            Assert.Equal("https://www.powershellgallery.com/api/v2", version.RepositorySource);
            Assert.StartsWith("https://www.powershellgallery.com/api/v2/package/Pester/", version.PackageSource, StringComparison.OrdinalIgnoreCase);
        });
        Assert.DoesNotContain(requests, request => request.Url == "https://www.powershellgallery.com/api/v3/index.json");
        Assert.Contains(requests, request => request.Url == "https://www.powershellgallery.com/api/v2/FindPackagesById()?id='Pester'&semVerLevel=2.0.0");
    }

    [Fact]
    public async Task GetVersionsAsync_does_not_probe_unavailable_powershellgallery_v3_for_reads()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(
            requests,
            powerShellGalleryV3StatusCode: HttpStatusCode.Forbidden));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository(
            "PSGallery",
            ManagedModuleCatalogDefaults.PowerShellGalleryV2);

        var versions = await repositoryClient.GetVersionsAsync(repository, "Pester", includePrerelease: false);

        Assert.Equal(new[] { "5.6.1", "5.7.0" }, versions.Select(version => version.Version));
        Assert.All(versions, version =>
        {
            Assert.Equal("PSGallery", version.RepositoryName);
            Assert.Equal("https://www.powershellgallery.com/api/v2", version.RepositorySource);
            Assert.StartsWith("https://www.powershellgallery.com/api/v2/package/Pester/", version.PackageSource, StringComparison.OrdinalIgnoreCase);
        });
        Assert.DoesNotContain(requests, request => request.Url == "https://www.powershellgallery.com/api/v3/index.json");
        Assert.Contains(requests, request => request.Url == "https://www.powershellgallery.com/api/v2/FindPackagesById()?id='Pester'&semVerLevel=2.0.0");
    }

    [Fact]
    public async Task GetVersionsAsync_uses_nuget_v2_find_packages_by_id()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/api/v2");

        var versions = await repositoryClient.GetVersionsAsync(repository, "Company.Tools", includePrerelease: false);

        Assert.Equal(ManagedModuleRepositoryKind.NuGetV2, repository.Kind);
        Assert.Equal(new[] { "1.0.0", "1.1.0" }, versions.Select(version => version.Version));
        var latest = Assert.Single(versions, version => version.Version == "1.1.0");
        Assert.True(latest.RequireLicenseAcceptance);
        Assert.Equal("https://licenses.example.test/company-tools", latest.License);
        var dependency = Assert.Single(latest.Dependencies);
        Assert.Equal("Company.Core", dependency.Id);
        Assert.Equal("[2.0.0, )", dependency.VersionRange);
        Assert.Equal("net8.0", dependency.TargetFramework);
        Assert.All(versions, version =>
        {
            Assert.Equal("Gallery", version.RepositoryName);
            Assert.Equal(repository.Source, version.RepositorySource);
            Assert.StartsWith("https://example.test/api/v2/package/Company.Tools/", version.PackageSource, StringComparison.OrdinalIgnoreCase);
            Assert.False(version.IsPrerelease);
        });
        Assert.Contains(requests, request => request.Url == "https://example.test/api/v2/FindPackagesById()?id='Company.Tools'&semVerLevel=2.0.0");
        Assert.All(requests, request => Assert.Contains("PowerForge-ManagedModule/1.0", request.UserAgent, StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetVersionsAsync_preserves_nuget_v2_unlisted_metadata()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/api/v2");

        var versions = await repositoryClient.GetVersionsAsync(repository, "V2.Unlisted", includePrerelease: false);

        Assert.Equal(new[] { "1.0.0", "1.1.0" }, versions.Select(version => version.Version));
        Assert.True(versions.Single(version => version.Version == "1.0.0").Listed);
        Assert.False(versions.Single(version => version.Version == "1.1.0").Listed);
    }

    [Fact]
    public async Task GetLatestDependencyVersionAsync_allows_unlisted_exact_dependency_version()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests, includeRegistrationBase: true));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var version = await repositoryClient.GetLatestDependencyVersionAsync(
            repository,
            new ManagedModuleDependencyInfo
            {
                Id = "Company.Core",
                VersionRange = "[2.0.0]"
            });

        Assert.NotNull(version);
        Assert.Equal("Company.Core", version!.Name);
        Assert.Equal("2.0.0", version.Version);
        Assert.False(version.Listed);
        Assert.Contains(requests, request => request.Url == "https://example.test/registration/company.core/index.json");
    }

    [Fact]
    public async Task GetLatestVersionAsync_uses_nuget_v2_packages_latest_filter()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/api/v2");

        var version = await repositoryClient.GetLatestVersionAsync(repository, "Company.Tools", includePrerelease: false);

        Assert.NotNull(version);
        Assert.Equal("Company.Tools", version!.Name);
        Assert.Equal("1.1.0", version.Version);
        Assert.Equal("Gallery", version.RepositoryName);
        Assert.Equal(repository.Source, version.RepositorySource);
        Assert.Contains(
            requests,
            request => request.Url == "https://example.test/api/v2/Packages()?$filter=Id%20eq%20'Company.Tools'%20and%20IsLatestVersion&$top=1&semVerLevel=2.0.0");
        Assert.DoesNotContain(requests, request => request.Url.Contains("FindPackagesById()?id='Company.Tools'&$filter=IsLatestVersion", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetLatestVersionAsync_missing_nuget_v2_package_returns_null()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/api/v2");

        var version = await repositoryClient.GetLatestVersionAsync(repository, "Missing.Tools", includePrerelease: false);

        Assert.Null(version);
        Assert.Contains(
            requests,
            request => request.Url == "https://example.test/api/v2/Packages()?$filter=Id%20eq%20'Missing.Tools'%20and%20IsLatestVersion&$top=1&semVerLevel=2.0.0");
    }

    [Fact]
    public async Task GetLatestVersionAsync_uses_package_id_fallback_for_nuget_v2_latest_entries_without_id()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/api/v2");

        var version = await repositoryClient.GetLatestVersionAsync(repository, "NoId.Tools", includePrerelease: false);

        Assert.NotNull(version);
        Assert.Equal("NoId.Tools", version!.Name);
        Assert.Equal("1.1.0", version.Version);
        Assert.Contains(
            requests,
            request => request.Url == "https://example.test/api/v2/Packages()?$filter=Id%20eq%20'NoId.Tools'%20and%20IsLatestVersion&$top=1&semVerLevel=2.0.0");
    }

    [Fact]
    public async Task GetLatestVersionAsync_uses_absolute_latest_filter_for_prerelease()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/api/v2");

        var version = await repositoryClient.GetLatestVersionAsync(repository, "Company.Tools", includePrerelease: true);

        Assert.NotNull(version);
        Assert.Equal("1.2.0-preview1", version!.Version);
        Assert.True(version.IsPrerelease);
        Assert.Contains(
            requests,
            request => request.Url == "https://example.test/api/v2/Packages()?$filter=Id%20eq%20'Company.Tools'%20and%20IsAbsoluteLatestVersion&$top=1&semVerLevel=2.0.0");
    }

    [Fact]
    public async Task DownloadPackageAsync_PartitionsPackageCacheByRepositorySource()
    {
        using var firstFeed = new TemporaryDirectory();
        using var secondFeed = new TemporaryDirectory();
        using var cache = new TemporaryDirectory();
        TestPackageFactory.Create(
            Path.Combine(firstFeed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.Tools.psd1"] = "@{ ModuleVersion = '1.0.0'; Description = 'First feed' }"
            });
        TestPackageFactory.Create(
            Path.Combine(secondFeed.Path, "Company.Tools.1.0.0.nupkg"),
            "Company.Tools",
            "1.0.0",
            files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.Tools.psd1"] = "@{ ModuleVersion = '1.0.0'; Description = 'Second feed' }"
            });
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger());

        var first = await repositoryClient.DownloadPackageAsync(
            new ManagedModuleRepository("First", firstFeed.Path),
            "Company.Tools",
            "1.0.0",
            cache.Path);
        var second = await repositoryClient.DownloadPackageAsync(
            new ManagedModuleRepository("Second", secondFeed.Path),
            "Company.Tools",
            "1.0.0",
            cache.Path);

        Assert.NotEqual(first.PackagePath, second.PackagePath);
        Assert.NotEqual(first.PackageSha256, second.PackageSha256);
        Assert.False(first.FromCache);
        Assert.False(second.FromCache);
    }

    [Fact]
    public void DownloadPackageAsync_package_cache_key_preserves_feed_path_case()
    {
        using var cache = new TemporaryDirectory();
        var upper = BuildCachedPackagePath(
            cache.Path,
            new ManagedModuleRepository("Upper", "https://example.test/Feed"),
            "Company.Tools",
            "1.0.0");
        var lower = BuildCachedPackagePath(
            cache.Path,
            new ManagedModuleRepository("Lower", "https://example.test/feed"),
            "Company.Tools",
            "1.0.0");

        Assert.NotEqual(upper, lower);
    }

    [Fact]
    public async Task GetLatestVersionAsync_uses_powershellgallery_v2_read_api_for_canonical_default()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository(
            "PSGallery",
            "https://www.powershellgallery.com/api/v3/index.json");

        var version = await repositoryClient.GetLatestVersionAsync(repository, "Pester", includePrerelease: false);

        Assert.NotNull(version);
        Assert.Equal("5.7.0", version!.Version);
        Assert.Equal("PSGallery", version.RepositoryName);
        Assert.Equal("https://www.powershellgallery.com/api/v2", version.RepositorySource);
        Assert.DoesNotContain(requests, request => request.Url == "https://www.powershellgallery.com/api/v3/index.json");
        Assert.Contains(
            requests,
            request => request.Url == "https://www.powershellgallery.com/api/v2/Packages()?$filter=Id%20eq%20'Pester'%20and%20IsLatestVersion&$top=1&semVerLevel=2.0.0");
    }

    [Fact]
    public async Task DownloadPackageAsync_uses_powershellgallery_cdn_for_v2_endpoint()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository(
            "PSGallery",
            "https://www.powershellgallery.com/api/v2");

        var result = await repositoryClient.DownloadPackageAsync(repository, "Pester", "5.7.0", temp.Path);

        Assert.True(File.Exists(result.PackagePath));
        Assert.Equal("Pester", result.Metadata!.Id);
        Assert.Equal("5.7.0", result.Metadata.Version);
        Assert.Equal("PSGallery", result.RepositoryName);
        Assert.Equal("https://cdn.powershellgallery.com/packages/pester.5.7.0.nupkg", result.Source);
        Assert.Equal(0, result.RedirectCount);
        Assert.Contains(requests, request => request.Url == "https://cdn.powershellgallery.com/packages/pester.5.7.0.nupkg");
        Assert.DoesNotContain(requests, request => request.Url == "https://www.powershellgallery.com/api/v2/package/Pester/5.7.0");
    }

    [Fact]
    public async Task GetVersionsAsync_follows_nuget_v2_find_packages_next_pages()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/api/v2");

        var versions = await repositoryClient.GetVersionsAsync(repository, "Paged.Tools", includePrerelease: false);

        Assert.Equal(new[] { "2.34.0", "2.38.0" }, versions.Select(version => version.Version));
        Assert.Contains(requests, request => request.Url == "https://example.test/api/v2/FindPackagesById()?id='Paged.Tools'&semVerLevel=2.0.0");
        Assert.Contains(requests, request => request.Url == "https://example.test/api/v2/FindPackagesById()?id='Paged.Tools'&semVerLevel=2.0.0&$skip=100");
    }

    [Fact]
    public async Task SearchPackagesAsync_uses_nuget_v2_search_endpoint()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/api/v2");

        var versions = await repositoryClient.SearchPackagesAsync(repository, "Company.*", includePrerelease: false, take: 10);

        Assert.Equal(new[] { "Company.Core", "Company.Tools" }, versions.Select(version => version.Name));
        Assert.Equal(new[] { "2.0.0", "1.1.0" }, versions.Select(version => version.Version));
        Assert.All(versions, version =>
        {
            Assert.Equal("Gallery", version.RepositoryName);
            Assert.Equal(repository.Source, version.RepositorySource);
            Assert.False(version.IsPrerelease);
        });
        Assert.Contains(
            requests,
            request => request.Url == "https://example.test/api/v2/Packages()?$filter=startswith(Id,'Company.')%20and%20IsLatestVersion&$top=10&$skip=0&semVerLevel=2.0.0");
    }

    [Fact]
    public async Task SearchPackagesAsync_uses_absolute_latest_filter_for_nuget_v2_prerelease_search()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/api/v2");

        await repositoryClient.SearchPackagesAsync(repository, "Company.*", includePrerelease: true, take: 10);

        Assert.Contains(
            requests,
            request => request.Url == "https://example.test/api/v2/Packages()?$filter=startswith(Id,'Company.')%20and%20IsAbsoluteLatestVersion&$top=10&$skip=0&semVerLevel=2.0.0");
    }

    [Fact]
    public async Task GetVersionsAsync_applies_basic_credentials_to_repository_requests()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Private", "https://example.test/v3/index.json");

        await repositoryClient.GetVersionsAsync(
            repository,
            "Company.Tools",
            credential: new RepositoryCredential { UserName = "build", Secret = "token" });

        Assert.All(requests, request =>
        {
            Assert.NotNull(request.Authorization);
            Assert.Equal("Basic", request.Authorization!.Scheme);
            var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(request.Authorization.Parameter!));
            Assert.Equal("build:token", decoded);
        });
    }

    [Fact]
    public async Task GetVersionsAsync_missing_package_returns_empty_with_repository_context()
    {
        using var client = new HttpClient(new ManagedModuleHandler(new List<RecordedRequest>()));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var versions = await repositoryClient.GetVersionsAsync(repository, "Missing.Tools");

        Assert.Empty(versions);
    }

    [Fact]
    public async Task GetLatestVersionAsync_missing_nuget_v3_package_returns_null()
    {
        using var client = new HttpClient(new ManagedModuleHandler(new List<RecordedRequest>()));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var version = await repositoryClient.GetLatestVersionAsync(repository, "Missing.Tools");

        Assert.Null(version);
    }

    [Fact]
    public async Task GetLatestVersionAsync_uses_registration_metadata_for_unlisted_versions()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests, includeRegistrationBase: true));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var version = await repositoryClient.GetLatestVersionAsync(repository, "Unlisted.Tools");
        var versions = await repositoryClient.GetVersionsAsync(repository, "Unlisted.Tools");

        Assert.NotNull(version);
        Assert.Equal("2.0.0", version!.Version);
        Assert.False(versions.Single(item => item.Version == "1.5.0").Listed);
        Assert.True(versions.Single(item => item.Version == "2.0.0").Listed);
        Assert.False(versions.Single(item => item.Version == "3.0.0").Listed);
        Assert.Contains(requests, request => request.Url == "https://example.test/registration/unlisted.tools/index.json");
    }

    [Fact]
    public async Task GetVersionsAsync_malformed_versions_reports_repository_context()
    {
        using var client = new HttpClient(new ManagedModuleHandler(new List<RecordedRequest>()));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var exception = await Assert.ThrowsAsync<ManagedModuleRepositoryException>(() =>
            repositoryClient.GetVersionsAsync(repository, "Malformed.Tools"));

        Assert.Contains("malformed JSON", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Malformed.Tools", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Gallery", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("VersionQuery", exception.Operation);
        Assert.Equal("Gallery", exception.RepositoryName);
        Assert.Null(exception.StatusCode);
        Assert.Contains("valid NuGet v3 JSON", exception.Remediation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetVersionsAsync_retries_transient_repository_failures()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests, serviceIndexFailures: 1));
        var repositoryClient = new ManagedModuleRepositoryClient(
            new NullLogger(),
            client,
            options: new ManagedModuleRepositoryClientOptions
            {
                MaxRetries = 1,
                RetryDelay = TimeSpan.Zero
            });
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var versions = await repositoryClient.GetVersionsAsync(repository, "Company.Tools");

        Assert.Equal(new[] { "1.0.0", "1.1.0" }, versions.Select(version => version.Version));
        Assert.Equal(2, requests.Count(request => request.Url == "https://example.test/v3/index.json"));
        Assert.Equal(3, repositoryClient.RequestCount);
    }

    [Fact]
    public async Task GetVersionsAsync_times_out_slow_repository_requests()
    {
        using var client = new HttpClient(new SlowHandler());
        var repositoryClient = new ManagedModuleRepositoryClient(
            new NullLogger(),
            client,
            options: new ManagedModuleRepositoryClientOptions
            {
                MaxRetries = 0,
                RequestTimeout = TimeSpan.FromMilliseconds(10)
            });
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        await Assert.ThrowsAsync<TimeoutException>(() => repositoryClient.GetVersionsAsync(repository, "Company.Tools"));
    }

    [Fact]
    public async Task GetVersionsAsync_transport_failure_reports_request_uri()
    {
        using var client = new HttpClient(new FailingHandler());
        var repositoryClient = new ManagedModuleRepositoryClient(
            new NullLogger(),
            client,
            options: new ManagedModuleRepositoryClientOptions
            {
                MaxRetries = 0
            });
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            repositoryClient.GetVersionsAsync(repository, "Company.Tools"));

        Assert.Contains("GET https://example.test/v3/index.json", exception.Message, StringComparison.Ordinal);
        Assert.Contains("socket unavailable", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadPackageAsync_writes_package_from_nuget_v3_feed()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var result = await repositoryClient.DownloadPackageAsync(repository, "Company.Tools", "1.1.0", temp.Path);

        Assert.True(File.Exists(result.PackagePath));
        Assert.Equal("Company.Tools", result.Metadata!.Id);
        Assert.Equal("1.1.0", result.Metadata.Version);
        Assert.True(result.BytesWritten > 0);
        Assert.Equal(new FileInfo(result.PackagePath).Length, result.BytesWritten);
        Assert.Equal(ComputeSha256(result.PackagePath), result.PackageSha256);
        Assert.Equal(0, result.RedirectCount);
        Assert.Contains(requests, request => request.Url == "https://example.test/packages/company.tools/1.1.0/company.tools.1.1.0.nupkg");
    }

#if !NET472
    [Fact]
    public async Task DownloadPackageToMemoryAsync_buffers_package_from_nuget_v3_feed()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        using var result = await repositoryClient.DownloadPackageToMemoryAsync(repository, "Company.Tools", "1.1.0");

        Assert.Equal("Company.Tools", result.Download.Metadata!.Id);
        Assert.Equal("1.1.0", result.Download.Metadata.Version);
        Assert.Equal(string.Empty, result.Download.PackagePath);
        Assert.True(result.Download.BytesWritten > 0);
        Assert.Equal(64, result.Download.PackageSha256.Length);
        Assert.True(result.PackageStream.CanSeek);
        Assert.Equal(0, result.PackageStream.Position);
        Assert.Contains(requests, request => request.Url == "https://example.test/packages/company.tools/1.1.0/company.tools.1.1.0.nupkg");
    }

    [Fact]
    public async Task DownloadPackageToMemoryAsync_uses_known_content_length_as_initial_capacity()
    {
        const int expectedCapacity = 1024 * 1024 + 123;
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests, companyToolsPackageContentLength: expectedCapacity));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        using var result = await repositoryClient.DownloadPackageToMemoryAsync(repository, "Company.Tools", "1.1.0");

        Assert.Equal(expectedCapacity, result.PackageStream.Capacity);
        Assert.True(result.PackageStream.Length < expectedCapacity);
    }

    [Fact]
    public async Task DownloadPackageToMemoryAsync_rejects_package_above_memory_cap_by_content_length()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests, companyToolsPackageContentLength: 129L * 1024 * 1024));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        await Assert.ThrowsAsync<ManagedModuleBufferedPackageTooLargeException>(() =>
            repositoryClient.DownloadPackageToMemoryAsync(repository, "Company.Tools", "1.1.0"));
    }

#endif

    [Fact]
    public async Task PlanInstallAsync_enriches_nuget_v3_version_with_package_license_metadata()
    {
        var requests = new List<RecordedRequest>();
        using var moduleRoot = new TemporaryDirectory();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var service = new ManagedModuleInstallService(new NullLogger(), repositoryClient);

        var plan = await service.PlanInstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json"),
            Name = "Company.Tools",
            Version = "1.1.0",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.True(plan.LicenseAcceptanceRequired);
        Assert.Equal("expression:MIT", plan.License);
        Assert.Contains(requests, request => request.Url == "https://example.test/packages/company.tools/1.1.0/company.tools.1.1.0.nupkg");
        Assert.False(Directory.Exists(Path.Combine(moduleRoot.Path, "Company.Tools")));
    }

    [Fact]
    public async Task DownloadPackageAsync_rejects_package_identity_mismatch()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repositoryClient.DownloadPackageAsync(repository, "Mismatch.Tools", "1.0.0", temp.Path));

        Assert.Contains("not requested package", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Mismatch.Tools", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Other.Tools", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(requests, request => request.Url == "https://example.test/packages/mismatch.tools/1.0.0/mismatch.tools.1.0.0.nupkg");
    }

    [Fact]
    public async Task DownloadPackageAsync_rejects_oversized_package_while_streaming()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(
            new NullLogger(),
            client,
            options: new ManagedModuleRepositoryClientOptions { MaxPackageBytes = 4 });
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repositoryClient.DownloadPackageAsync(repository, "Company.Tools", "1.1.0", temp.Path));

        Assert.Contains("package size limit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.nupkg", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DownloadPackageAsync_preserves_api_key_on_same_host_redirect()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/api/v2");

        var result = await repositoryClient.DownloadPackageAsync(
            repository,
            "SameHost.Tools",
            "1.0.0",
            temp.Path,
            new RepositoryCredential { Secret = "token" });

        Assert.True(File.Exists(result.PackagePath));
        Assert.Contains(requests, request => request.Url == "https://example.test/api/v2/package/SameHost.Tools/1.0.0" && request.ApiKey == "token");
        Assert.Contains(requests, request => request.Url == "https://example.test/redirected/samehost.tools.1.0.0.nupkg" && request.ApiKey == "token");
    }

    [Fact]
    public async Task DownloadPackageAsync_writes_exact_package_from_nuget_v2_feed()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/api/v2");

        var result = await repositoryClient.DownloadPackageAsync(repository, "Company.Tools", "1.1.0", temp.Path);

        Assert.Equal(ManagedModuleRepositoryKind.NuGetV2, repository.Kind);
        Assert.True(File.Exists(result.PackagePath));
        Assert.Equal("Company.Tools", result.Metadata!.Id);
        Assert.Equal("1.1.0", result.Metadata.Version);
        Assert.True(result.BytesWritten > 0);
        Assert.Equal(new FileInfo(result.PackagePath).Length, result.BytesWritten);
        Assert.Equal(ComputeSha256(result.PackagePath), result.PackageSha256);
        Assert.Equal(1, result.RedirectCount);
        Assert.Contains(requests, request => request.Url == "https://example.test/api/v2/package/Company.Tools/1.1.0");
        Assert.Contains(requests, request => request.Url == "https://cdn.example.test/packages/company.tools.1.1.0.nupkg");
    }

    [Fact]
    public async Task DownloadPackageAsync_uses_powershellgallery_cdn_for_canonical_default()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository(
            "PSGallery",
            "https://www.powershellgallery.com/api/v3/index.json");

        var result = await repositoryClient.DownloadPackageAsync(repository, "Pester", "5.7.0", temp.Path);

        Assert.True(File.Exists(result.PackagePath));
        Assert.Equal("Pester", result.Metadata!.Id);
        Assert.Equal("5.7.0", result.Metadata.Version);
        Assert.Equal("PSGallery", result.RepositoryName);
        Assert.Equal("https://cdn.powershellgallery.com/packages/pester.5.7.0.nupkg", result.Source);
        Assert.Equal(0, result.RedirectCount);
        Assert.DoesNotContain(requests, request => request.Url == "https://www.powershellgallery.com/api/v3/index.json");
        Assert.Contains(requests, request => request.Url == "https://cdn.powershellgallery.com/packages/pester.5.7.0.nupkg");
        Assert.DoesNotContain(requests, request => request.Url == "https://www.powershellgallery.com/api/v2/package/Pester/5.7.0");
    }

    [Fact]
    public async Task DownloadPackageAsync_falls_back_to_powershellgallery_v2_package_when_cdn_misses()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        using var client = new HttpClient(new ManagedModuleHandler(requests, powerShellGalleryCdnStatusCode: HttpStatusCode.NotFound));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository(
            "PSGallery",
            "https://www.powershellgallery.com/api/v3/index.json");

        var result = await repositoryClient.DownloadPackageAsync(repository, "Pester", "5.7.0", temp.Path);

        Assert.True(File.Exists(result.PackagePath));
        Assert.Equal("Pester", result.Metadata!.Id);
        Assert.Equal("5.7.0", result.Metadata.Version);
        Assert.Equal("PSGallery", result.RepositoryName);
        Assert.Equal("https://www.powershellgallery.com/api/v2/package/Pester/5.7.0", result.Source);
        Assert.DoesNotContain(requests, request => request.Url == "https://www.powershellgallery.com/api/v3/index.json");
        Assert.Contains(requests, request => request.Url == "https://cdn.powershellgallery.com/packages/pester.5.7.0.nupkg");
        Assert.Contains(requests, request => request.Url == "https://www.powershellgallery.com/api/v2/package/Pester/5.7.0");
    }

    [Fact]
    public async Task DownloadPackageAsync_uses_v2_package_endpoint_for_script_items_source()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository(
            "Scripts",
            "https://example.test/api/v2/items/psscript");

        var result = await repositoryClient.DownloadPackageAsync(repository, "Company.Tools", "1.1.0", temp.Path);

        Assert.True(File.Exists(result.PackagePath));
        Assert.Equal("https://example.test/api/v2/package/Company.Tools/1.1.0", result.Source);
        Assert.Contains(requests, request => request.Url == "https://example.test/api/v2/package/Company.Tools/1.1.0");
        Assert.DoesNotContain(requests, request => request.Url == "https://example.test/api/v2/items/psscript/package/Company.Tools/1.1.0");
    }

    [Fact]
    public async Task DownloadPackageAsync_reuses_matching_cache_without_repository_request()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");
        var cachedPackagePath = BuildCachedPackagePath(temp.Path, repository, "Company.Tools", "1.1.0");
        Directory.CreateDirectory(Path.GetDirectoryName(cachedPackagePath)!);
        TestPackageFactory.Create(cachedPackagePath, "Company.Tools", "1.1.0");

        var result = await repositoryClient.DownloadPackageAsync(repository, "Company.Tools", "1.1.0", temp.Path);

        Assert.True(result.FromCache);
        Assert.Equal(0, result.BytesWritten);
        Assert.Equal(0, result.RedirectCount);
        Assert.Equal(ComputeSha256(result.PackagePath), result.PackageSha256);
        Assert.Equal("Company.Tools", result.Metadata!.Id);
        Assert.Empty(requests);
        Assert.Equal(0, repositoryClient.RequestCount);
    }

    [Fact]
    public async Task DownloadPackageAsync_missing_local_version_reports_package_and_repository()
    {
        using var source = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(Path.Combine(source.Path, "Company.Tools.1.0.0.nupkg"), "Company.Tools", "1.0.0");
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger());
        var repository = new ManagedModuleRepository("Local", source.Path);

        var exception = await Assert.ThrowsAsync<ManagedModuleRepositoryException>(() =>
            repositoryClient.DownloadPackageAsync(repository, "Company.Tools", "9.9.9", destination.Path));

        Assert.Contains("Company.Tools", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("9.9.9", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Local", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Download", exception.Operation);
        Assert.Equal("Local", exception.RepositoryName);
        Assert.Contains(".nupkg", exception.Remediation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishPackageAsync_uses_nuget_v3_package_publish_endpoint_with_api_key()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllBytes(packagePath, TestPackageFactory.CreateBytes("Company.Tools", "1.0.0"));
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var result = await repositoryClient.PublishPackageAsync(
            repository,
            packagePath,
            new RepositoryCredential { Secret = "publish-key" });

        Assert.True(result.Published);
        Assert.Equal(201, result.StatusCode);
        var publishRequest = Assert.Single(requests, request => request.Url == "https://example.test/publish/");
        Assert.Equal(HttpMethod.Put, publishRequest.Method);
        Assert.Equal("publish-key", publishRequest.ApiKey);
        Assert.DoesNotContain(requests, request => request.Url == "https://example.test/symbol-publish/");
    }

    [Fact]
    public async Task PublishPackageAsync_treats_trailing_slash_service_index_as_v3_index()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllBytes(packagePath, TestPackageFactory.CreateBytes("Company.Tools", "1.0.0"));
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json/");

        var result = await repositoryClient.PublishPackageAsync(
            repository,
            packagePath,
            new RepositoryCredential { Secret = "publish-key" });

        Assert.True(result.Published);
        Assert.Contains(requests, request => request.Url == "https://example.test/v3/index.json");
        var publishRequest = Assert.Single(requests, request => request.Url == "https://example.test/publish/");
        Assert.Equal(HttpMethod.Put, publishRequest.Method);
        Assert.DoesNotContain(requests, request => request.Url == "https://example.test/v3/index.json/");
    }

    [Fact]
    public async Task PublishPackageAsync_resolves_powershellgallery_publish_endpoint_with_api_key()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllBytes(packagePath, TestPackageFactory.CreateBytes("Company.Tools", "1.0.0"));
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository(
            "PSGallery",
            ManagedModuleCatalogDefaults.PowerShellGalleryV2);

        var result = await repositoryClient.PublishPackageAsync(
            repository,
            packagePath,
            new RepositoryCredential { Secret = "gallery-key" });

        Assert.True(result.Published);
        Assert.Equal(201, result.StatusCode);
        var publishRequest = Assert.Single(requests, request => request.Url == "https://www.powershellgallery.com/api/v2/package");
        Assert.Equal(HttpMethod.Put, publishRequest.Method);
        Assert.Equal("gallery-key", publishRequest.ApiKey);
        Assert.Equal("multipart/form-data", publishRequest.ContentType);
        Assert.Equal(1, publishRequest.MultipartPartCount);
        Assert.Equal("package", publishRequest.MultipartPartName);
        Assert.Equal("Company.Tools.1.0.0.nupkg", publishRequest.MultipartFileName);
        Assert.Equal("application/octet-stream", publishRequest.MultipartPartContentType);
    }

    [Fact]
    public async Task PublishPackageAsync_posts_to_direct_nuget_v2_publish_endpoint()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllBytes(packagePath, TestPackageFactory.CreateBytes("Company.Tools", "1.0.0"));
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("LegacyPush", "https://push.example.test/api/v2/package");

        var result = await repositoryClient.PublishPackageAsync(
            repository,
            packagePath,
            new RepositoryCredential { Secret = "publish-key" });

        Assert.True(result.Published);
        Assert.Equal(201, result.StatusCode);
        var publishRequest = Assert.Single(requests, request => request.Url == "https://push.example.test/api/v2/package");
        Assert.Equal(HttpMethod.Put, publishRequest.Method);
        Assert.Equal("publish-key", publishRequest.ApiKey);
    }

    [Fact]
    public async Task PublishPackageAsync_derives_nuget_v2_package_endpoint_from_feed_root()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllBytes(packagePath, TestPackageFactory.CreateBytes("Company.Tools", "1.0.0"));
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("LegacyPush", "https://push.example.test/api/v2");

        var result = await repositoryClient.PublishPackageAsync(
            repository,
            packagePath,
            new RepositoryCredential { Secret = "publish-key" });

        Assert.True(result.Published);
        Assert.Equal(201, result.StatusCode);
        var publishRequest = Assert.Single(requests, request => request.Url == "https://push.example.test/api/v2/package");
        Assert.Equal(HttpMethod.Put, publishRequest.Method);
        Assert.Equal("publish-key", publishRequest.ApiKey);
    }


    [Fact]
    public async Task PublishPackageAsync_copies_package_to_local_folder_feed()
    {
        using var source = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(source.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllBytes(packagePath, TestPackageFactory.CreateBytes("Company.Tools", "1.0.0"));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger());
        var repository = new ManagedModuleRepository("Local", destination.Path);

        var result = await repositoryClient.PublishPackageAsync(repository, packagePath);

        Assert.True(result.Published);
        Assert.True(File.Exists(Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg")));
    }

    [Fact]
    public async Task PublishPackageAsync_classifies_local_duplicate_without_force()
    {
        using var source = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(source.Path, "Company.Tools.1.0.0.nupkg");
        var destinationPath = Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllBytes(packagePath, TestPackageFactory.CreateBytes("Company.Tools", "1.0.0"));
        File.WriteAllText(destinationPath, "existing");
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger());
        var repository = new ManagedModuleRepository("Local", destination.Path);

        var result = await repositoryClient.PublishPackageAsync(repository, packagePath);

        Assert.False(result.Published);
        Assert.True(result.Duplicate);
        Assert.Equal("existing", File.ReadAllText(destinationPath));
        Assert.Contains("already exists", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishPackageAsync_overwrites_local_duplicate_with_force()
    {
        using var source = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        var packagePath = Path.Combine(source.Path, "Company.Tools.1.0.0.nupkg");
        var destinationPath = Path.Combine(destination.Path, "Company.Tools.1.0.0.nupkg");
        File.WriteAllBytes(packagePath, TestPackageFactory.CreateBytes("Company.Tools", "1.0.0"));
        File.WriteAllText(destinationPath, "existing");
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger());
        var repository = new ManagedModuleRepository("Local", destination.Path);

        var result = await repositoryClient.PublishPackageAsync(repository, packagePath, force: true);

        Assert.True(result.Published);
        Assert.False(result.Duplicate);
        Assert.NotEqual("existing", File.ReadAllText(destinationPath));
    }

    [Fact]
    public async Task PublishPackageAsync_classifies_remote_conflict_as_duplicate()
    {
        using var temp = new TemporaryDirectory();
        var packagePath = Path.Combine(temp.Path, "Company.Duplicate.1.0.0.nupkg");
        File.WriteAllBytes(packagePath, TestPackageFactory.CreateBytes("Company.Duplicate", "1.0.0"));
        using var client = new HttpClient(new ManagedModuleHandler(new List<RecordedRequest>(), conflictPublishes: true));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var result = await repositoryClient.PublishPackageAsync(repository, packagePath);

        Assert.False(result.Published);
        Assert.True(result.Duplicate);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("already exists", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetVersionsAsync_reads_local_folder_feed_and_filters_prerelease()
    {
        using var temp = new TemporaryDirectory();
        TestPackageFactory.Create(Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg"), "Company.Tools", "1.0.0");
        TestPackageFactory.Create(Path.Combine(temp.Path, "Company.Tools.1.1.0-beta1.nupkg"), "Company.Tools", "1.1.0-beta1");
        TestPackageFactory.Create(Path.Combine(temp.Path, "Other.Module.9.0.0.nupkg"), "Other.Module", "9.0.0");
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger());
        var repository = new ManagedModuleRepository("Local", temp.Path);

        var stable = await repositoryClient.GetVersionsAsync(repository, "Company.Tools", includePrerelease: false);
        var all = await repositoryClient.GetVersionsAsync(repository, "Company.Tools", includePrerelease: true);

        Assert.Equal(new[] { "1.0.0" }, stable.Select(version => version.Version));
        Assert.Equal(new[] { "1.0.0", "1.1.0-beta1" }, all.Select(version => version.Version));
        Assert.All(all, version => Assert.Equal("Local", version.RepositoryName));
    }

    [Fact]
    public async Task SearchPackagesAsync_reads_local_folder_feed_with_wildcards()
    {
        using var temp = new TemporaryDirectory();
        TestPackageFactory.Create(Path.Combine(temp.Path, "Company.Tools.1.0.0.nupkg"), "Company.Tools", "1.0.0");
        TestPackageFactory.Create(Path.Combine(temp.Path, "Company.Tools.1.1.0.nupkg"), "Company.Tools", "1.1.0");
        TestPackageFactory.Create(Path.Combine(temp.Path, "Company.Core.2.0.0.nupkg"), "Company.Core", "2.0.0");
        TestPackageFactory.Create(Path.Combine(temp.Path, "Other.Module.9.0.0.nupkg"), "Other.Module", "9.0.0");
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger());
        var repository = new ManagedModuleRepository("Local", temp.Path);

        var results = await repositoryClient.SearchPackagesAsync(repository, "Company.*");

        Assert.Equal(new[] { "Company.Core", "Company.Tools" }, results.Select(result => result.Name));
        Assert.Equal("1.1.0", results.Single(result => result.Name == "Company.Tools").Version);
        Assert.All(results, result => Assert.Equal("Local", result.RepositoryName));
    }

    [Fact]
    public async Task SearchPackagesAsync_uses_nuget_v3_search_query_service()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var results = await repositoryClient.SearchPackagesAsync(repository, "Company.*");

        Assert.Equal(new[] { "Company.Core", "Company.Tools" }, results.Select(result => result.Name));
        Assert.Contains(requests, request => request.Url == "https://example.test/search?q=Company.&prerelease=false&take=100&skip=0&semVerLevel=2.0.0");
    }

    [Fact]
    public async Task SearchPackagesAsync_keeps_paging_after_filtered_empty_nuget_v3_pages()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var results = await repositoryClient.SearchPackagesAsync(repository, "*Tools", includePrerelease: false, take: 1);

        var result = Assert.Single(results);
        Assert.Equal("Company.Tools", result.Name);
        Assert.Contains(requests, request => request.Url == "https://example.test/search?q=Tools&prerelease=false&take=1&skip=0&semVerLevel=2.0.0");
        Assert.Contains(requests, request => request.Url == "https://example.test/search?q=Tools&prerelease=false&take=1&skip=1&semVerLevel=2.0.0");
    }

    [Fact]
    public async Task SearchPackagesAsync_keeps_paging_when_nuget_v3_caps_search_page_size()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var results = await repositoryClient.SearchPackagesAsync(repository, "Company.*", includePrerelease: false, take: 1000);

        Assert.Contains(results, result => result.Name == "Company.Reporting");
        Assert.Contains(requests, request => request.Url == "https://example.test/search?q=Company.&prerelease=false&take=1000&skip=0&semVerLevel=2.0.0");
        Assert.Contains(requests, request => request.Url == "https://example.test/search?q=Company.&prerelease=false&take=1000&skip=1&semVerLevel=2.0.0");
        Assert.Contains(requests, request => request.Url == "https://example.test/search?q=Company.&prerelease=false&take=1000&skip=2&semVerLevel=2.0.0");
    }

    [Fact]
    public async Task SearchPackagesAsync_preserves_positional_cancellation_token_overload()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var results = await repositoryClient.SearchPackagesAsync(
            repository,
            "Company.*",
            false,
            null,
            100,
            CancellationToken.None);

        Assert.Equal(new[] { "Company.Core", "Company.Tools" }, results.Select(result => result.Name));
        Assert.Contains(requests, request => request.Url == "https://example.test/search?q=Company.&prerelease=false&take=100&skip=0&semVerLevel=2.0.0");
    }

    [Fact]
    public async Task SearchPackagesAsync_rejects_flat_container_source_for_wildcards()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Flat", "https://example.test/packages/");

        var exception = await Assert.ThrowsAsync<ManagedModuleRepositoryException>(() => repositoryClient.SearchPackagesAsync(repository, "Company.*"));

        Assert.Equal("SearchServiceDiscovery", exception.Operation);
        Assert.Contains("SearchQueryService", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(requests);
    }

    [Fact]
    public async Task GetLatestDependencyVersionAsync_skips_unlisted_versions()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests, includeRegistrationBase: true));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");
        var dependency = new ManagedModuleDependencyInfo
        {
            Id = "Unlisted.Tools",
            VersionRange = "[1.0.0, )"
        };

        var result = await repositoryClient.GetLatestDependencyVersionAsync(repository, dependency);

        Assert.NotNull(result);
        Assert.Equal("2.0.0", result.Version);
        Assert.True(result.Listed);
    }

    [Fact]
    public async Task SearchPackagesAsync_uses_powershellgallery_v2_read_api_for_canonical_default()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(
            requests,
            powerShellGalleryV3StatusCode: HttpStatusCode.Forbidden));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository(
            "PSGallery",
            "https://www.powershellgallery.com/api/v3/index.json");

        var results = await repositoryClient.SearchPackagesAsync(repository, "Pester");

        var result = Assert.Single(results);
        Assert.Equal("Pester", result.Name);
        Assert.Equal("5.7.0", result.Version);
        Assert.Equal("PSGallery", result.RepositoryName);
        Assert.Equal("https://www.powershellgallery.com/api/v2", result.RepositorySource);
        Assert.DoesNotContain(requests, request => request.Url == "https://www.powershellgallery.com/api/v3/index.json");
        Assert.Contains(
            requests,
            request => request.Url == "https://www.powershellgallery.com/api/v2/Packages()?$filter=substringof('Pester',Id)%20and%20IsLatestVersion&$top=100&$skip=0&semVerLevel=2.0.0");
    }

    [Fact]
    public async Task DownloadPackageAsync_copies_package_from_local_folder_feed()
    {
        using var source = new TemporaryDirectory();
        using var destination = new TemporaryDirectory();
        TestPackageFactory.Create(Path.Combine(source.Path, "Company.Tools.1.2.0.nupkg"), "Company.Tools", "1.2.0");
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger());
        var repository = new ManagedModuleRepository("Local", source.Path);

        var result = await repositoryClient.DownloadPackageAsync(repository, "Company.Tools", "1.2.0", destination.Path);

        Assert.True(File.Exists(result.PackagePath));
        Assert.Equal("Company.Tools", result.Metadata!.Id);
        Assert.Equal("1.2.0", result.Metadata.Version);
        Assert.Equal("Local", result.RepositoryName);
        Assert.Equal(new FileInfo(result.PackagePath).Length, result.BytesWritten);
        Assert.Equal(ComputeSha256(result.PackagePath), result.PackageSha256);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha256 = SHA256.Create();
        return string.Concat(sha256.ComputeHash(stream).Select(static value => value.ToString("x2")));
    }

    private static string BuildCachedPackagePath(
        string destinationDirectory,
        ManagedModuleRepository repository,
        string packageId,
        string version)
        => Path.Combine(
            Path.GetFullPath(destinationDirectory),
            GetRepositoryCacheKey(repository),
            $"{packageId.Trim().ToLowerInvariant()}.{version.Trim().ToLowerInvariant()}.nupkg");

    private static string GetRepositoryCacheKey(ManagedModuleRepository repository)
    {
        using var sha256 = SHA256.Create();
        var source = Encoding.UTF8.GetBytes(repository.Source.Trim());
        var hash = sha256.ComputeHash(source);
        return string.Concat(hash.Take(8).Select(static value => value.ToString("x2")));
    }

    internal sealed class ManagedModuleHandler : HttpMessageHandler
    {
        private readonly List<RecordedRequest> _requests;
        private readonly bool _conflictPublishes;
        private readonly HttpStatusCode? _powerShellGalleryV3StatusCode;
        private readonly HttpStatusCode? _powerShellGalleryCdnStatusCode;
        private readonly long? _companyToolsPackageContentLength;
        private readonly bool _includeRegistrationBase;
        private int _serviceIndexFailures;

        public ManagedModuleHandler(
            List<RecordedRequest> requests,
            bool conflictPublishes = false,
            int serviceIndexFailures = 0,
            HttpStatusCode? powerShellGalleryV3StatusCode = null,
            HttpStatusCode? powerShellGalleryCdnStatusCode = null,
            long? companyToolsPackageContentLength = null,
            bool includeRegistrationBase = false)
        {
            _requests = requests;
            _conflictPublishes = conflictPublishes;
            _serviceIndexFailures = serviceIndexFailures;
            _powerShellGalleryV3StatusCode = powerShellGalleryV3StatusCode;
            _powerShellGalleryCdnStatusCode = powerShellGalleryCdnStatusCode;
            _companyToolsPackageContentLength = companyToolsPackageContentLength;
            _includeRegistrationBase = includeRegistrationBase;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
            var apiKey = request.Headers.TryGetValues("X-NuGet-ApiKey", out var values)
                ? values.FirstOrDefault()
                : null;
            var multipart = request.Content as MultipartFormDataContent;
            var firstPart = multipart?.FirstOrDefault();
            _requests.Add(new RecordedRequest(
                uri.AbsoluteUri,
                request.Method,
                request.Headers.Authorization,
                apiKey,
                request.Headers.UserAgent.ToString(),
                request.Content?.Headers.ContentType?.MediaType,
                multipart?.Count() ?? 0,
                firstPart?.Headers.ContentDisposition?.Name?.Trim('"'),
                firstPart?.Headers.ContentDisposition?.FileName?.Trim('"'),
                firstPart?.Headers.ContentType?.MediaType));

            if (uri.AbsoluteUri == "https://example.test/v3/index.json")
            {
                if (_serviceIndexFailures > 0)
                {
                    _serviceIndexFailures--;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                }

                var registration = _includeRegistrationBase
                    ? ",{\"@id\":\"https://example.test/registration/\",\"@type\":\"RegistrationsBaseUrl/3.6.0\"}"
                    : string.Empty;
                return Json("{\"resources\":[" +
                            "{\"@id\":\"https://example.test/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}," +
                            "{\"@id\":\"https://example.test/search\",\"@type\":\"SearchQueryService/3.5.0\"}," +
                            "{\"@id\":\"https://example.test/symbol-publish/\",\"@type\":\"SymbolPackagePublish/4.9.0\"}," +
                            "{\"@id\":\"https://example.test/publish/\",\"@type\":\"PackagePublish/2.0.0\"}" +
                            registration +
                            "]}");
            }

            if (uri.AbsoluteUri == "https://example.test/Feed/v3/index.json")
            {
                return Json("{\"resources\":[" +
                            "{\"@id\":\"https://example.test/Feed/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}," +
                            "{\"@id\":\"https://example.test/Feed/search\",\"@type\":\"SearchQueryService/3.5.0\"}" +
                            "]}");
            }

            if (uri.AbsoluteUri == "https://example.test/feed/v3/index.json")
            {
                return Json("{\"resources\":[" +
                            "{\"@id\":\"https://example.test/feed/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}," +
                            "{\"@id\":\"https://example.test/feed/search\",\"@type\":\"SearchQueryService/3.5.0\"}" +
                            "]}");
            }

            if (uri.AbsoluteUri == "https://www.powershellgallery.com/api/v3/index.json")
            {
                if (_powerShellGalleryV3StatusCode.HasValue)
                    return Task.FromResult(new HttpResponseMessage(_powerShellGalleryV3StatusCode.Value));

                return Json("{\"resources\":[" +
                            "{\"@id\":\"https://psgallery.test/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}," +
                            "{\"@id\":\"https://psgallery.test/search/\",\"@type\":\"SearchQueryService/3.5.0\"}," +
                            "{\"@id\":\"https://psgallery.test/publish/\",\"@type\":\"PackagePublish/2.0.0\"}" +
                            "]}");
            }

            if (uri.AbsoluteUri == "https://example.test/packages/company.tools/index.json")
                return Json("{\"versions\":[\"1.0.0\",\"1.1.0-beta1\",\"1.1.0\"]}");

            if (uri.AbsoluteUri == "https://example.test/Feed/packages/company.tools/index.json")
                return Json("{\"versions\":[\"1.0.0\"]}");

            if (uri.AbsoluteUri == "https://example.test/feed/packages/company.tools/index.json")
                return Json("{\"versions\":[\"2.0.0\"]}");

            if (uri.AbsoluteUri == "https://example.test/packages/unlisted.tools/index.json")
                return Json("{\"versions\":[\"1.0.0\",\"1.5.0\",\"2.0.0\",\"3.0.0\"]}");

            if (uri.AbsoluteUri == "https://example.test/registration/unlisted.tools/index.json")
                return Json("{\"items\":[{\"items\":[" +
                            "{\"catalogEntry\":{\"version\":\"1.0.0\",\"listed\":true}}," +
                            "{\"catalogEntry\":{\"version\":\"1.5.0\",\"listed\":false}}," +
                            "{\"catalogEntry\":{\"version\":\"2.0.0\",\"listed\":true}}," +
                            "{\"catalogEntry\":{\"version\":\"3.0.0\",\"listed\":false}}" +
                            "]}]}");

            if (uri.AbsoluteUri == "https://example.test/packages/company.core/index.json")
                return Json("{\"versions\":[\"2.0.0\"]}");

            if (uri.AbsoluteUri == "https://example.test/registration/company.core/index.json")
                return Json("{\"items\":[{\"items\":[" +
                            "{\"catalogEntry\":{\"version\":\"2.0.0\",\"listed\":false}}" +
                            "]}]}");

            if (uri.AbsoluteUri == "https://psgallery.test/packages/pester/index.json")
                return Json("{\"versions\":[\"5.6.1\",\"5.7.0-preview1\",\"5.7.0\"]}");

            if (uri.AbsoluteUri == "https://example.test/api/v2/FindPackagesById()?id='Company.Tools'&semVerLevel=2.0.0")
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\">" +
                    "<entry><content><m:properties xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\"><d:Version>1.0.0</d:Version></m:properties></content></entry>" +
                    "<entry><content><m:properties xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\"><d:Version>1.1.0-beta1</d:Version></m:properties></content></entry>" +
                    "<entry><content><m:properties xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\"><d:Version>1.1.0</d:Version><d:RequireLicenseAcceptance>true</d:RequireLicenseAcceptance><d:LicenseUrl>https://licenses.example.test/company-tools</d:LicenseUrl><d:Dependencies>Company.Core:[2.0.0, ):net8.0</d:Dependencies></m:properties></content></entry>" +
                    "</feed>");

            if (uri.AbsoluteUri == "https://example.test/api/v2/FindPackagesById()?id='SameHost.Tools'&semVerLevel=2.0.0")
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\">" +
                    "<entry><content><m:properties xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\"><d:Version>1.0.0</d:Version></m:properties></content></entry>" +
                    "</feed>");

            if (uri.AbsoluteUri == "https://example.test/api/v2/FindPackagesById()?id='V2.Unlisted'&semVerLevel=2.0.0")
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\">" +
                    "<entry><content><m:properties xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\"><d:Version>1.0.0</d:Version><d:Listed>true</d:Listed></m:properties></content></entry>" +
                    "<entry><content><m:properties xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\"><d:Version>1.1.0</d:Version><d:Listed>false</d:Listed></m:properties></content></entry>" +
                    "</feed>");

            if (uri.AbsoluteUri == "https://push.example.test/api/v2/FindPackagesById()?id='Company.Core'&semVerLevel=2.0.0")
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\">" +
                    "<entry><content><m:properties xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\"><d:Version>2.0.0</d:Version></m:properties></content></entry>" +
                    "</feed>");

            if (uri.AbsoluteUri == "https://example.test/api/v2/Packages()?$filter=Id%20eq%20'Company.Tools'%20and%20IsLatestVersion&$top=1&semVerLevel=2.0.0")
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\">" +
                    "<entry><content><m:properties><d:Id>Company.Tools</d:Id><d:Version>1.1.0</d:Version></m:properties></content></entry>" +
                    "</feed>");

            if (uri.AbsoluteUri == "https://example.test/api/v2/Packages()?$filter=Id%20eq%20'NoId.Tools'%20and%20IsLatestVersion&$top=1&semVerLevel=2.0.0")
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\">" +
                    "<entry><content><m:properties><d:Version>1.1.0</d:Version></m:properties></content></entry>" +
                    "</feed>");

            if (uri.AbsoluteUri == "https://example.test/api/v2/Packages()?$filter=Id%20eq%20'Company.Tools'%20and%20IsAbsoluteLatestVersion&$top=1&semVerLevel=2.0.0")
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\">" +
                    "<entry><content><m:properties><d:Id>Company.Tools</d:Id><d:Version>1.2.0-preview1</d:Version></m:properties></content></entry>" +
                    "</feed>");

            if (uri.AbsoluteUri == "https://www.powershellgallery.com/api/v2/FindPackagesById()?id='Pester'&semVerLevel=2.0.0")
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\">" +
                    "<entry><content><m:properties xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\"><d:Version>5.6.1</d:Version></m:properties></content></entry>" +
                    "<entry><content><m:properties xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\"><d:Version>5.7.0-preview1</d:Version></m:properties></content></entry>" +
                    "<entry><content><m:properties xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\"><d:Version>5.7.0</d:Version></m:properties></content></entry>" +
                    "</feed>");

            if (uri.AbsoluteUri == "https://www.powershellgallery.com/api/v2/Packages()?$filter=Id%20eq%20'Pester'%20and%20IsLatestVersion&$top=1&semVerLevel=2.0.0")
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\">" +
                    "<entry><content><m:properties><d:Id>Pester</d:Id><d:Version>5.7.0</d:Version></m:properties></content></entry>" +
                    "</feed>");

            if (uri.AbsoluteUri == "https://example.test/api/v2/FindPackagesById()?id='Paged.Tools'&semVerLevel=2.0.0")
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\">" +
                    "<link rel=\"next\" href=\"https://example.test/api/v2/FindPackagesById()?id='Paged.Tools'&amp;semVerLevel=2.0.0&amp;$skip=100\" />" +
                    "<entry><content><m:properties xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\"><d:Version>2.34.0</d:Version></m:properties></content></entry>" +
                    "</feed>");

            if (uri.AbsoluteUri == "https://example.test/api/v2/FindPackagesById()?id='Paged.Tools'&semVerLevel=2.0.0&$skip=100")
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\">" +
                    "<entry><content><m:properties xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\"><d:Version>2.38.0</d:Version></m:properties></content></entry>" +
                    "</feed>");

            if (uri.AbsoluteUri == "https://example.test/api/v2/Packages()?$filter=startswith(Id,'Company.')%20and%20IsLatestVersion&$top=10&$skip=0&semVerLevel=2.0.0")
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\">" +
                    "<entry><content><m:properties><d:Id>Company.Tools</d:Id><d:Version>1.1.0-beta1</d:Version></m:properties></content></entry>" +
                    "<entry><content><m:properties><d:Id>Company.Tools</d:Id><d:Version>1.1.0</d:Version></m:properties></content></entry>" +
                    "<entry><content><m:properties><d:Id>Company.Core</d:Id><d:Version>2.0.0</d:Version></m:properties></content></entry>" +
                    "<entry><content><m:properties><d:Id>Other.Module</d:Id><d:Version>9.0.0</d:Version></m:properties></content></entry>" +
                    "</feed>");

            if (uri.AbsoluteUri == "https://example.test/api/v2/Packages()?$filter=startswith(Id,'Company.')%20and%20IsAbsoluteLatestVersion&$top=10&$skip=0&semVerLevel=2.0.0")
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\">" +
                    "<entry><content><m:properties><d:Id>Company.Tools</d:Id><d:Version>1.2.0-preview1</d:Version></m:properties></content></entry>" +
                    "</feed>");

            if (uri.AbsoluteUri == "https://www.powershellgallery.com/api/v2/Packages()?$filter=substringof('Pester',Id)%20and%20IsLatestVersion&$top=100&$skip=0&semVerLevel=2.0.0")
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\">" +
                    "<entry><content><m:properties><d:Id>Pester</d:Id><d:Version>5.7.0-preview1</d:Version></m:properties></content></entry>" +
                    "<entry><content><m:properties><d:Id>Pester</d:Id><d:Version>5.7.0</d:Version></m:properties></content></entry>" +
                    "</feed>");

            if (uri.AbsoluteUri == "https://example.test/packages/malformed.tools/index.json")
                return Json("{\"versions\":[\"1.0.0\"");

            if (uri.AbsoluteUri == "https://example.test/packages/company.tools/1.1.0/company.tools.1.1.0.nupkg")
            {
                var bytes = TestPackageFactory.CreateBytes(
                    "Company.Tools",
                    "1.1.0",
                    requireLicenseAcceptance: true,
                    files: CreateCompanyToolsFiles("1.1.0"));
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                };
                if (_companyToolsPackageContentLength.HasValue)
                    response.Content.Headers.ContentLength = _companyToolsPackageContentLength.Value;
                return Task.FromResult(response);
            }

            if (uri.AbsoluteUri == "https://example.test/packages/mismatch.tools/1.0.0/mismatch.tools.1.0.0.nupkg")
            {
                var bytes = TestPackageFactory.CreateBytes("Other.Tools", "1.0.0");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                });
            }

            if (uri.AbsoluteUri == "https://example.test/api/v2/package/Company.Tools/1.1.0")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers =
                    {
                        Location = new Uri("https://cdn.example.test/packages/company.tools.1.1.0.nupkg")
                    }
                });

            if (uri.AbsoluteUri == "https://example.test/api/v2/package/SameHost.Tools/1.0.0")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers =
                    {
                        Location = new Uri("https://example.test/redirected/samehost.tools.1.0.0.nupkg")
                    }
                });

            if (uri.AbsoluteUri == "https://www.powershellgallery.com/api/v2/package/Pester/5.7.0")
            {
                var bytes = TestPackageFactory.CreateBytes("Pester", "5.7.0");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                });
            }

            if (uri.AbsoluteUri == "https://cdn.powershellgallery.com/packages/pester.5.7.0.nupkg")
            {
                if (_powerShellGalleryCdnStatusCode.HasValue)
                    return Task.FromResult(new HttpResponseMessage(_powerShellGalleryCdnStatusCode.Value));

                var bytes = TestPackageFactory.CreateBytes("Pester", "5.7.0");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                });
            }

            if (uri.AbsoluteUri == "https://cdn.example.test/packages/company.tools.1.1.0.nupkg")
            {
                var bytes = TestPackageFactory.CreateBytes(
                    "Company.Tools",
                    "1.1.0",
                    files: CreateCompanyToolsFiles("1.1.0"));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                });
            }

            if (uri.AbsoluteUri == "https://example.test/redirected/samehost.tools.1.0.0.nupkg")
            {
                var bytes = TestPackageFactory.CreateBytes("SameHost.Tools", "1.0.0");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                });
            }

            if (uri.AbsoluteUri == "https://example.test/search?q=Company.&prerelease=false&take=100&skip=0&semVerLevel=2.0.0")
                return Json("{\"data\":[" +
                            "{\"id\":\"Company.Tools\",\"version\":\"1.1.0\",\"listed\":true}," +
                            "{\"id\":\"Company.Core\",\"version\":\"2.0.0\",\"listed\":true}," +
                            "{\"id\":\"Other.Module\",\"version\":\"9.0.0\",\"listed\":true}" +
                            "]}");

            if (uri.AbsoluteUri == "https://example.test/search?q=Company.&prerelease=false&take=1000&skip=0&semVerLevel=2.0.0")
                return Json("{\"totalHits\":3,\"data\":[{\"id\":\"Company.Noise\",\"version\":\"1.0.0\",\"listed\":true}]}");

            if (uri.AbsoluteUri == "https://example.test/search?q=Company.&prerelease=false&take=1000&skip=1&semVerLevel=2.0.0")
                return Json("{\"totalHits\":3,\"data\":[{\"id\":\"Company.Reporting\",\"version\":\"1.0.0\",\"listed\":true}]}");

            if (uri.AbsoluteUri == "https://example.test/search?q=Company.&prerelease=false&take=1000&skip=2&semVerLevel=2.0.0")
                return Json("{\"totalHits\":3,\"data\":[{\"id\":\"Company.Final\",\"version\":\"1.0.0\",\"listed\":true}]}");

            if (uri.AbsoluteUri == "https://example.test/search?q=Company.Tools&prerelease=false&take=20&skip=0&semVerLevel=2.0.0")
                return Json("{\"data\":[{\"id\":\"Company.Tools\",\"version\":\"1.1.0\",\"listed\":true}]}");

            if (uri.AbsoluteUri == "https://example.test/search?q=Tools&prerelease=false&take=1&skip=0&semVerLevel=2.0.0")
                return Json("{\"data\":[{\"id\":\"Tools.Extras\",\"version\":\"1.0.0\",\"listed\":true}]}");

            if (uri.AbsoluteUri == "https://example.test/search?q=Tools&prerelease=false&take=1&skip=1&semVerLevel=2.0.0")
                return Json("{\"data\":[{\"id\":\"Company.Tools\",\"version\":\"1.0.0\",\"listed\":true}]}");

            if (uri.AbsoluteUri == "https://example.test/search?q=Unlisted.Tools&prerelease=false&take=20&skip=0&semVerLevel=2.0.0")
                return Json("{\"data\":[{\"id\":\"Unlisted.Tools\",\"version\":\"1.5.0\",\"listed\":true}]}");

            if (uri.AbsoluteUri == "https://example.test/publish/" && request.Method == HttpMethod.Put)
            {
                if (_conflictPublishes)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
            }

            if (uri.AbsoluteUri == "https://psgallery.test/publish/" && request.Method == HttpMethod.Put)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));

            if (uri.AbsoluteUri == "https://www.powershellgallery.com/api/v2/package" && request.Method == HttpMethod.Put)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));

            if (uri.AbsoluteUri == "https://push.example.test/api/v2/package" && request.Method == HttpMethod.Put)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(string json)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        private static Task<HttpResponseMessage> Xml(string xml)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml, Encoding.UTF8, "application/xml")
            });

        private static IReadOnlyDictionary<string, string> CreateCompanyToolsFiles(string version)
            => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Company.Tools.psd1"] = "@{ ModuleVersion = '" + version + "' }",
                ["Public/Get-CompanyTool.ps1"] = "function Get-CompanyTool { 'ok' }"
            };
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("socket unavailable");
    }

    internal sealed class RecordedRequest
    {
        public RecordedRequest(
            string url,
            HttpMethod method,
            AuthenticationHeaderValue? authorization,
            string? apiKey,
            string userAgent,
            string? contentType,
            int multipartPartCount,
            string? multipartPartName,
            string? multipartFileName,
            string? multipartPartContentType)
        {
            Url = url;
            Method = method;
            Authorization = authorization;
            ApiKey = apiKey;
            UserAgent = userAgent;
            ContentType = contentType;
            MultipartPartCount = multipartPartCount;
            MultipartPartName = multipartPartName;
            MultipartFileName = multipartFileName;
            MultipartPartContentType = multipartPartContentType;
        }

        public string Url { get; }

        public HttpMethod Method { get; }

        public AuthenticationHeaderValue? Authorization { get; }

        public string? ApiKey { get; }

        public string UserAgent { get; }

        public string? ContentType { get; }

        public int MultipartPartCount { get; }

        public string? MultipartPartName { get; }

        public string? MultipartFileName { get; }

        public string? MultipartPartContentType { get; }
    }
}
