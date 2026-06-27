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

        public ManagedModuleHandler(List<RecordedRequest> requests)
        {
            _requests = requests;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
            _requests.Add(new RecordedRequest(uri.AbsoluteUri, request.Headers.Authorization));

            if (uri.AbsoluteUri == "https://example.test/v3/index.json")
                return Json("{\"resources\":[{\"@id\":\"https://example.test/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}]}");

            if (uri.AbsoluteUri == "https://example.test/packages/company.tools/index.json")
                return Json("{\"versions\":[\"1.0.0\",\"1.1.0-beta1\",\"1.1.0\"]}");

            if (uri.AbsoluteUri == "https://example.test/packages/company.tools/1.1.0/company.tools.1.1.0.nupkg")
            {
                var bytes = TestPackageFactory.CreateBytes("Company.Tools", "1.1.0");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(bytes)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Json(string json)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
    }

    private sealed class RecordedRequest
    {
        public RecordedRequest(string url, AuthenticationHeaderValue? authorization)
        {
            Url = url;
            Authorization = authorization;
        }

        public string Url { get; }

        public AuthenticationHeaderValue? Authorization { get; }
    }
}
