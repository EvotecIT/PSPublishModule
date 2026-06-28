using System.Net;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleInstallLatestVersionTests
{
    [Fact]
    public async Task PlanInstallAsync_uses_latest_endpoint_for_unbounded_nuget_v2_request()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new LatestPackageHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var service = new ManagedModuleInstallService(new NullLogger(), repositoryClient);
        using var moduleRoot = new TemporaryDirectory();

        var plan = await service.PlanInstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Gallery", "https://example.test/api/v2"),
            Name = "Company.Tools",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal("1.1.0", plan.Version);
        Assert.Contains(requests, request => request.Url == LatestPackageHandler.LatestStableUrl);
        Assert.DoesNotContain(requests, request => request.Url.Contains("FindPackagesById", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InstallAsync_uses_latest_endpoint_for_unbounded_nuget_v2_request()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new LatestPackageHandler(requests));
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var service = new ManagedModuleInstallService(new NullLogger(), repositoryClient);
        using var moduleRoot = new TemporaryDirectory();

        var result = await service.InstallAsync(new ManagedModuleInstallRequest
        {
            Repository = new ManagedModuleRepository("Gallery", "https://example.test/api/v2"),
            Name = "Company.Tools",
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot.Path
        });

        Assert.Equal(ManagedModuleInstallStatus.Installed, result.Status);
        Assert.Equal("1.1.0", result.Version);
        Assert.True(File.Exists(Path.Combine(moduleRoot.Path, "Company.Tools", "1.1.0", "Company.Tools.psd1")));
        Assert.Contains(requests, request => request.Url == LatestPackageHandler.LatestStableUrl);
        Assert.Contains(requests, request => request.Url == LatestPackageHandler.PackageUrl);
        Assert.DoesNotContain(requests, request => request.Url.Contains("FindPackagesById", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class LatestPackageHandler : HttpMessageHandler
    {
        public const string LatestStableUrl = "https://example.test/api/v2/Packages()?$filter=Id%20eq%20'Company.Tools'%20and%20IsLatestVersion&$top=1";
        public const string PackageUrl = "https://example.test/api/v2/package/Company.Tools/1.1.0";

        private readonly List<RecordedRequest> _requests;

        public LatestPackageHandler(List<RecordedRequest> requests)
        {
            _requests = requests;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(new RecordedRequest(request.RequestUri?.AbsoluteUri ?? string.Empty));
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (uri == LatestStableUrl)
            {
                return Xml(
                    "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                    "<feed xmlns=\"http://www.w3.org/2005/Atom\" xmlns:d=\"http://schemas.microsoft.com/ado/2007/08/dataservices\" xmlns:m=\"http://schemas.microsoft.com/ado/2007/08/dataservices/metadata\">" +
                    "<entry><content><m:properties><d:Id>Company.Tools</d:Id><d:Version>1.1.0</d:Version></m:properties></content></entry>" +
                    "</feed>");
            }

            if (uri == PackageUrl)
            {
                using var temp = new TemporaryDirectory();
                var packagePath = Path.Combine(temp.Path, "Company.Tools.1.1.0.nupkg");
                TestPackageFactory.Create(
                    packagePath,
                    "Company.Tools",
                    "1.1.0",
                    files: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Company.Tools.psd1"] = "@{ ModuleVersion = '1.1.0' }"
                    });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(File.ReadAllBytes(packagePath))
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Xml(string xml)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(xml)
            });
    }

    private sealed class RecordedRequest
    {
        public RecordedRequest(string url)
        {
            Url = url;
        }

        public string Url { get; }
    }
}
