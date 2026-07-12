using System.Net;
using System.Net.Http.Headers;
using System.Text;
using PowerForge;

namespace PowerForge.Tests;

public sealed class NuGetPackageVersionResolverTests
{
    [Fact]
    public void ResolveLatestPackageVersion_PreservesPrereleaseOrdering()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Sample.Package.2.0.0-beta.2.nupkg"), string.Empty);
            File.WriteAllText(Path.Combine(root.FullName, "Sample.Package.2.0.0-beta.10.nupkg"), string.Empty);
            File.WriteAllText(Path.Combine(root.FullName, "Sample.Package.1.9.9.nupkg"), string.Empty);
            var resolver = new NuGetPackageVersionResolver(new NullLogger());

            var latest = resolver.ResolveLatestPackageVersion(
                "Sample.Package",
                new[] { root.FullName },
                credential: null,
                credentialsBySource: null,
                includePrerelease: true);

            Assert.Equal("2.0.0-beta.10", latest);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ResolveLatest_applies_source_scoped_credentials_only_to_matching_source()
    {
        var requests = new List<RecordedRequest>();
        using var client = new HttpClient(new RecordingHandler(requests));
        var resolver = new NuGetPackageVersionResolver(new NullLogger(), client);
        var sources = new[]
        {
            "https://api.nuget.org/v3/index.json",
            "https://nuget.pkg.github.com/EvotecIT/index.json"
        };
        var credentialsBySource = new Dictionary<string, RepositoryCredential>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://nuget.pkg.github.com/EvotecIT/index.json"] = new()
            {
                UserName = "EvotecIT",
                Secret = "github-token"
            }
        };

        var latest = resolver.ResolveLatest(
            "Sample.Package",
            sources,
            credential: null,
            credentialsBySource,
            includePrerelease: false);

        Assert.Equal(new Version(1, 0, 8), latest);
        Assert.All(
            requests.Where(request => string.Equals(request.Host, "api.nuget.org", StringComparison.OrdinalIgnoreCase)),
            request => Assert.Null(request.Authorization));
        var githubRequests = requests
            .Where(request => string.Equals(request.Host, "nuget.pkg.github.com", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.NotEmpty(githubRequests);
        Assert.All(githubRequests, request =>
        {
            Assert.NotNull(request.Authorization);
            Assert.Equal("Basic", request.Authorization!.Scheme);
            var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(request.Authorization.Parameter!));
            Assert.Equal("EvotecIT:github-token", decoded);
        });
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<RecordedRequest> _requests;

        public RecordingHandler(List<RecordedRequest> requests)
        {
            _requests = requests;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required.");
            _requests.Add(new RecordedRequest(uri.Host, uri.AbsoluteUri, request.Headers.Authorization));

            var body = uri.AbsoluteUri switch
            {
                "https://api.nuget.org/v3/index.json" => CreateIndex("https://api.nuget.org/v3-flatcontainer/"),
                "https://api.nuget.org/v3-flatcontainer/sample.package/index.json" => CreateVersions("1.0.2"),
                "https://nuget.pkg.github.com/EvotecIT/index.json" => CreateIndex("https://nuget.pkg.github.com/EvotecIT/download/"),
                "https://nuget.pkg.github.com/EvotecIT/download/sample.package/index.json" => CreateVersions("1.0.8"),
                _ => null
            };

            if (body is null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }

        private static string CreateIndex(string packageBaseAddress)
            => "{\"resources\":[{\"@id\":\"" + packageBaseAddress + "\",\"@type\":\"PackageBaseAddress/3.0.0\"}]}";

        private static string CreateVersions(string version)
            => "{\"versions\":[\"" + version + "\"]}";
    }

    private sealed class RecordedRequest
    {
        public RecordedRequest(string host, string url, AuthenticationHeaderValue? authorization)
        {
            Host = host;
            Url = url;
            Authorization = authorization;
        }

        public string Host { get; }

        public string Url { get; }

        public AuthenticationHeaderValue? Authorization { get; }
    }
}
