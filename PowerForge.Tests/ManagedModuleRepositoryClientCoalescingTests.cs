using System.Collections.Concurrent;
using System.Net;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ManagedModuleRepositoryClientCoalescingTests
{
    [Fact]
    public async Task GetVersionsAsync_coalesces_concurrent_anonymous_remote_queries()
    {
        using var handler = new CoalescingHandler();
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var first = repositoryClient.GetVersionsAsync(repository, "Company.Tools", cancellationToken: cancellation.Token);
        await handler.WaitForVersionRequestAsync();
        var second = repositoryClient.GetVersionsAsync(repository, "Company.Tools", cancellationToken: cancellation.Token);
        handler.ReleaseVersionRequest();
        await Task.WhenAll(first, second);
        var firstResult = await first;
        var secondResult = await second;

        Assert.Equal(new[] { "1.0.0", "1.1.0" }, firstResult.Select(version => version.Version));
        Assert.Equal(new[] { "1.0.0", "1.1.0" }, secondResult.Select(version => version.Version));
        Assert.Equal(1, handler.Count("https://example.test/v3/index.json"));
        Assert.Equal(1, handler.Count("https://example.test/packages/company.tools/index.json"));
    }

    [Fact]
    public async Task GetVersionsAsync_keeps_credentialed_remote_queries_independent()
    {
        using var handler = new CoalescingHandler();
        using var client = new HttpClient(handler);
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");
        var credential = new RepositoryCredential { UserName = "build", Secret = "token" };

        var first = repositoryClient.GetVersionsAsync(repository, "Company.Tools", credential: credential);
        await handler.WaitForVersionRequestAsync();
        var second = repositoryClient.GetVersionsAsync(repository, "Company.Tools", credential: credential);
        handler.ReleaseVersionRequest();
        await Task.WhenAll(first, second);

        Assert.Equal(2, handler.Count("https://example.test/packages/company.tools/index.json"));
    }

    [Fact]
    public async Task SearchPackagesAsync_coalesces_concurrent_anonymous_remote_queries()
    {
        using var handler = new CoalescingHandler();
        using var client = new HttpClient(handler);
        var repositoryClient = new ManagedModuleRepositoryClient(new NullLogger(), client);
        var repository = new ManagedModuleRepository("Gallery", "https://example.test/v3/index.json");

        var first = repositoryClient.SearchPackagesAsync(repository, "Company.*");
        await handler.WaitForSearchRequestAsync();
        var second = repositoryClient.SearchPackagesAsync(repository, "Company.*");
        handler.ReleaseSearchRequest();
        await Task.WhenAll(first, second);
        var firstResult = await first;
        var secondResult = await second;

        Assert.Equal(new[] { "Company.Tools" }, firstResult.Select(version => version.Name));
        Assert.Equal(new[] { "Company.Tools" }, secondResult.Select(version => version.Name));
        Assert.Equal(1, handler.Count("https://example.test/search/?q=Company.&prerelease=false&take=100&semVerLevel=2.0.0"));
    }

    private sealed class CoalescingHandler : HttpMessageHandler, IDisposable
    {
        private readonly ConcurrentQueue<string> _requests = new();
        private readonly TaskCompletionSource<object?> _versionRequestSeen = CreateCompletionSource();
        private readonly TaskCompletionSource<object?> _releaseVersionRequest = CreateCompletionSource();
        private readonly TaskCompletionSource<object?> _searchRequestSeen = CreateCompletionSource();
        private readonly TaskCompletionSource<object?> _releaseSearchRequest = CreateCompletionSource();

        public Task WaitForVersionRequestAsync()
            => _versionRequestSeen.Task;

        public void ReleaseVersionRequest()
            => _releaseVersionRequest.TrySetResult(null);

        public Task WaitForSearchRequestAsync()
            => _searchRequestSeen.Task;

        public void ReleaseSearchRequest()
            => _releaseSearchRequest.TrySetResult(null);

        public int Count(string url)
            => _requests.Count(request => request.Equals(url, StringComparison.OrdinalIgnoreCase));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            _requests.Enqueue(uri);

            if (uri == "https://example.test/v3/index.json")
            {
                return Json("{\"resources\":[" +
                            "{\"@id\":\"https://example.test/packages/\",\"@type\":\"PackageBaseAddress/3.0.0\"}," +
                            "{\"@id\":\"https://example.test/search/\",\"@type\":\"SearchQueryService/3.5.0\"}" +
                            "]}");
            }

            if (uri == "https://example.test/packages/company.tools/index.json")
            {
                _versionRequestSeen.TrySetResult(null);
                await _releaseVersionRequest.Task.ConfigureAwait(false);
                return Json("{\"versions\":[\"1.0.0\",\"1.1.0\"]}");
            }

            if (uri == "https://example.test/search/?q=Company.&prerelease=false&take=100&semVerLevel=2.0.0")
            {
                _searchRequestSeen.TrySetResult(null);
                await _releaseSearchRequest.Task.ConfigureAwait(false);
                return Json("{\"data\":[{\"id\":\"Company.Tools\",\"version\":\"1.1.0\"}]}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static TaskCompletionSource<object?> CreateCompletionSource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static HttpResponseMessage Json(string json)
            => new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };

        void IDisposable.Dispose()
        {
            ReleaseVersionRequest();
            ReleaseSearchRequest();
        }
    }
}
