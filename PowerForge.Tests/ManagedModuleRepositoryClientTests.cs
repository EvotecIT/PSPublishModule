using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleRepositoryClientTests
{
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
        Assert.Equal(2, repositoryClient.RequestCount);
    }

    [Fact]
    public async Task GetVersionsAsync_resolves_powershellgallery_service_metadata()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository(
            "PSGallery",
            "https://www.powershellgallery.com/api/v3/index.json");

        var versions = await repositoryClient.GetVersionsAsync(repository, "Pester", includePrerelease: false);

        Assert.Equal(new[] { "5.6.1", "5.7.0" }, versions.Select(version => version.Version));
        Assert.All(versions, version =>
        {
            Assert.Equal("PSGallery", version.RepositoryName);
            Assert.Equal(repository.Source, version.RepositorySource);
            Assert.StartsWith("https://psgallery.test/packages/pester/", version.PackageSource, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains(requests, request => request.Url == "https://www.powershellgallery.com/api/v3/index.json");
        Assert.Contains(requests, request => request.Url == "https://psgallery.test/packages/pester/index.json");
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
    public async Task GetVersionsAsync_missing_package_reports_repository_context()
    {
        using var client = new HttpClient(new ManagedModuleHandler(new List<RecordedRequest>()));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repositoryClient.GetVersionsAsync(repository, "Missing.Tools"));

        Assert.Contains("version query failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("404", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Missing.Tools", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Gallery", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetVersionsAsync_malformed_versions_reports_repository_context()
    {
        using var client = new HttpClient(new ManagedModuleHandler(new List<RecordedRequest>()));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repositoryClient.GetVersionsAsync(repository, "Malformed.Tools"));

        Assert.Contains("malformed JSON", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Malformed.Tools", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Gallery", exception.Message, StringComparison.OrdinalIgnoreCase);
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
    public void CreateDefaultHttpMessageHandler_applies_explicit_proxy_options()
    {
        var proxyAddress = new Uri("http://proxy.example.test:8080");
        var handler = Assert.IsType<HttpClientHandler>(ManagedModuleRepositoryClient.CreateDefaultHttpMessageHandler(
            new ManagedModuleRepositoryClientOptions
            {
                ProxyAddress = proxyAddress,
                BypassProxyOnLocal = false,
                ProxyCredential = new RepositoryCredential
                {
                    UserName = "proxy-user",
                    Secret = "proxy-secret"
                }
            }));

        Assert.True(handler.UseProxy);
        Assert.NotNull(handler.Proxy);
        Assert.Equal(proxyAddress, handler.Proxy!.GetProxy(new Uri("https://example.test/v3/index.json")));
        var credential = Assert.IsType<NetworkCredential>(handler.Proxy.Credentials);
        Assert.Equal("proxy-user", credential.UserName);
        Assert.Equal("proxy-secret", credential.Password);
    }

    [Fact]
    public void CreateDefaultHttpMessageHandler_can_disable_proxy()
    {
        var handler = Assert.IsType<HttpClientHandler>(ManagedModuleRepositoryClient.CreateDefaultHttpMessageHandler(
            new ManagedModuleRepositoryClientOptions
            {
                UseProxy = false,
                ProxyAddress = new Uri("http://proxy.example.test:8080")
            }));

        Assert.False(handler.UseProxy);
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
        Assert.Contains(requests, request => request.Url == "https://example.test/packages/company.tools/1.1.0/company.tools.1.1.0.nupkg");
    }

    [Fact]
    public async Task DownloadPackageAsync_reuses_matching_cache_without_repository_request()
    {
        var requests = new List<RecordedRequest>();
        using var temp = new TemporaryDirectory();
        TestPackageFactory.Create(Path.Combine(temp.Path, "Company.Tools.1.1.0.nupkg"), "Company.Tools", "1.1.0");
        using var client = new HttpClient(new ManagedModuleHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var result = await repositoryClient.DownloadPackageAsync(repository, "Company.Tools", "1.1.0", temp.Path);

        Assert.True(result.FromCache);
        Assert.Equal(0, result.BytesWritten);
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

        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            repositoryClient.DownloadPackageAsync(repository, "Company.Tools", "9.9.9", destination.Path));

        Assert.Contains("Company.Tools", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("9.9.9", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Local", exception.Message, StringComparison.OrdinalIgnoreCase);
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
            "https://www.powershellgallery.com/api/v3/index.json");

        var result = await repositoryClient.PublishPackageAsync(
            repository,
            packagePath,
            new RepositoryCredential { Secret = "gallery-key" });

        Assert.True(result.Published);
        Assert.Equal(201, result.StatusCode);
        var publishRequest = Assert.Single(requests, request => request.Url == "https://psgallery.test/publish/");
        Assert.Equal(HttpMethod.Put, publishRequest.Method);
        Assert.Equal("gallery-key", publishRequest.ApiKey);
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
        Assert.Contains(requests, request => request.Url == "https://example.test/search/?q=Company.&prerelease=false&take=100");
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
    }

    private sealed class ManagedModuleHandler : HttpMessageHandler
    {
        private readonly List<RecordedRequest> _requests;
        private readonly bool _conflictPublishes;
        private int _serviceIndexFailures;

        public ManagedModuleHandler(
            List<RecordedRequest> requests,
            bool conflictPublishes = false,
            int serviceIndexFailures = 0)
        {
            _requests = requests;
            _conflictPublishes = conflictPublishes;
            _serviceIndexFailures = serviceIndexFailures;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
            var apiKey = request.Headers.TryGetValues("X-NuGet-ApiKey", out var values)
                ? values.FirstOrDefault()
                : null;
            _requests.Add(new RecordedRequest(uri.AbsoluteUri, request.Method, request.Headers.Authorization, apiKey));

            if (uri.AbsoluteUri == "https://example.test/v3/index.json")
            {
                if (_serviceIndexFailures > 0)
                {
                    _serviceIndexFailures--;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                }

                return Json("{\"resources\":[" +
                            "{\"@id\":\"https://example.test/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}," +
                            "{\"@id\":\"https://example.test/search/\",\"@type\":\"SearchQueryService/3.5.0\"}," +
                            "{\"@id\":\"https://example.test/publish/\",\"@type\":\"PackagePublish/2.0.0\"}" +
                            "]}");
            }

            if (uri.AbsoluteUri == "https://www.powershellgallery.com/api/v3/index.json")
                return Json("{\"resources\":[" +
                            "{\"@id\":\"https://psgallery.test/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}," +
                            "{\"@id\":\"https://psgallery.test/search/\",\"@type\":\"SearchQueryService/3.5.0\"}," +
                            "{\"@id\":\"https://psgallery.test/publish/\",\"@type\":\"PackagePublish/2.0.0\"}" +
                            "]}");

            if (uri.AbsoluteUri == "https://example.test/packages/company.tools/index.json")
                return Json("{\"versions\":[\"1.0.0\",\"1.1.0-beta1\",\"1.1.0\"]}");

            if (uri.AbsoluteUri == "https://psgallery.test/packages/pester/index.json")
                return Json("{\"versions\":[\"5.6.1\",\"5.7.0-preview1\",\"5.7.0\"]}");

            if (uri.AbsoluteUri == "https://example.test/packages/malformed.tools/index.json")
                return Json("{\"versions\":[\"1.0.0\"");

            if (uri.AbsoluteUri == "https://example.test/packages/company.tools/1.1.0/company.tools.1.1.0.nupkg")
            {
                var bytes = TestPackageFactory.CreateBytes("Company.Tools", "1.1.0");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                });
            }

            if (uri.AbsoluteUri == "https://example.test/search/?q=Company.&prerelease=false&take=100")
                return Json("{\"data\":[" +
                            "{\"id\":\"Company.Tools\",\"version\":\"1.1.0\",\"listed\":true}," +
                            "{\"id\":\"Company.Core\",\"version\":\"2.0.0\",\"listed\":true}," +
                            "{\"id\":\"Other.Module\",\"version\":\"9.0.0\",\"listed\":true}" +
                            "]}");

            if (uri.AbsoluteUri == "https://example.test/publish/" && request.Method == HttpMethod.Put)
            {
                if (_conflictPublishes)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));
            }

            if (uri.AbsoluteUri == "https://psgallery.test/publish/" && request.Method == HttpMethod.Put)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(string json)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class RecordedRequest
    {
        public RecordedRequest(string url, HttpMethod method, AuthenticationHeaderValue? authorization, string? apiKey)
        {
            Url = url;
            Method = method;
            Authorization = authorization;
            ApiKey = apiKey;
        }

        public string Url { get; }

        public HttpMethod Method { get; }

        public AuthenticationHeaderValue? Authorization { get; }

        public string? ApiKey { get; }
    }
}
