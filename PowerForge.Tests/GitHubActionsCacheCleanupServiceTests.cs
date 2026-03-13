using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class GitHubActionsCacheCleanupServiceTests
{
    [Fact]
    public void Prune_DryRun_UsesAgeThresholdAndUsageSnapshot()
    {
        var caches = new[]
        {
            Cache(id: 1, key: "ubuntu-nuget", daysAgo: 30),
            Cache(id: 2, key: "ubuntu-nuget", daysAgo: 10),
            Cache(id: 3, key: "ubuntu-nuget", daysAgo: 1),
            Cache(id: 4, key: "windows-nuget", daysAgo: 20),
            Cache(id: 5, key: "keep-me", daysAgo: 40)
        };

        var handler = new FakeGitHubCachesHandler(caches, activeCachesCount: 48, activeCachesBytes: 9_950_000_000L);
        using var client = new HttpClient(handler);
        var service = new GitHubActionsCacheCleanupService(new NullLogger(), client);

        var result = service.Prune(new GitHubActionsCacheCleanupSpec
        {
            Repository = "EvotecIT/PSPublishModule",
            Token = "test-token",
            ExcludeKeys = new[] { "keep-me" },
            KeepLatestPerKey = 1,
            MaxAgeDays = 7,
            DryRun = true
        });

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.NotNull(result.UsageBefore);
        Assert.Equal(48, result.UsageBefore!.ActiveCachesCount);
        Assert.Equal(5, result.ScannedCaches);
        Assert.Equal(4, result.MatchedCaches);
        Assert.Equal(2, result.PlannedDeletes);
        Assert.Equal(new long[] { 1, 2 }, result.Planned.Select(c => c.Id).OrderBy(v => v).ToArray());
        Assert.DoesNotContain(handler.DeletedCacheIds, _ => true);
    }

    [Fact]
    public void Prune_Apply_DeletesCachesAndReportsFailures()
    {
        var caches = new[]
        {
            Cache(id: 11, key: "ubuntu-node", daysAgo: 25),
            Cache(id: 12, key: "ubuntu-node", daysAgo: 18)
        };

        var handler = new FakeGitHubCachesHandler(caches, failDeleteIds: new[] { 12L });
        using var client = new HttpClient(handler);
        var service = new GitHubActionsCacheCleanupService(new NullLogger(), client);

        var result = service.Prune(new GitHubActionsCacheCleanupSpec
        {
            Repository = "EvotecIT/HtmlForgeX",
            Token = "test-token",
            KeepLatestPerKey = 0,
            MaxAgeDays = null,
            DryRun = false,
            FailOnDeleteError = true
        });

        Assert.False(result.Success);
        Assert.Equal(2, result.PlannedDeletes);
        Assert.Equal(1, result.DeletedCaches);
        Assert.Equal(1, result.FailedDeletes);
        Assert.Contains(11L, handler.DeletedCacheIds);
        Assert.Contains(12L, handler.DeletedCacheIds);
        Assert.Single(result.Failed);
        Assert.Equal(12L, result.Failed[0].Id);
        Assert.NotNull(result.Failed[0].DeleteError);
    }

    [Fact]
    public void Prune_IncludePattern_FiltersKeys()
    {
        var caches = new[]
        {
            Cache(id: 21, key: "ubuntu-nuget", daysAgo: 15),
            Cache(id: 22, key: "windows-nuget", daysAgo: 15),
            Cache(id: 23, key: "ubuntu-pnpm", daysAgo: 15)
        };

        var handler = new FakeGitHubCachesHandler(caches);
        using var client = new HttpClient(handler);
        var service = new GitHubActionsCacheCleanupService(new NullLogger(), client);

        var result = service.Prune(new GitHubActionsCacheCleanupSpec
        {
            Repository = "EvotecIT/CodeGlyphX",
            Token = "test-token",
            IncludeKeys = new[] { "ubuntu-*" },
            KeepLatestPerKey = 0,
            MaxAgeDays = null,
            DryRun = true
        });

        Assert.Equal(new long[] { 21, 23 }, result.Planned.Select(c => c.Id).OrderBy(v => v).ToArray());
    }

    private static FakeCache Cache(long id, string key, int daysAgo)
    {
        var timestamp = DateTimeOffset.UtcNow.AddDays(-daysAgo);
        return new FakeCache
        {
            Id = id,
            Key = key,
            Ref = "refs/heads/main",
            Version = "v1",
            SizeInBytes = 1024 + id,
            CreatedAt = timestamp,
            LastAccessedAt = timestamp
        };
    }

    private sealed class FakeGitHubCachesHandler : HttpMessageHandler
    {
        private readonly FakeCache[] _caches;
        private readonly HashSet<long> _failDeleteIds;
        private readonly int _activeCachesCount;
        private readonly long _activeCachesBytes;

        public List<long> DeletedCacheIds { get; } = new();

        public FakeGitHubCachesHandler(
            FakeCache[] caches,
            int activeCachesCount = 0,
            long activeCachesBytes = 0,
            IEnumerable<long>? failDeleteIds = null)
        {
            _caches = caches ?? Array.Empty<FakeCache>();
            _activeCachesCount = activeCachesCount == 0 ? _caches.Length : activeCachesCount;
            _activeCachesBytes = activeCachesBytes == 0 ? _caches.Sum(c => c.SizeInBytes) : activeCachesBytes;
            _failDeleteIds = failDeleteIds is null ? new HashSet<long>() : new HashSet<long>(failDeleteIds);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (request.Method == HttpMethod.Get && path.Contains("/actions/cache/usage", StringComparison.OrdinalIgnoreCase))
            {
                var usage = JsonSerializer.Serialize(new
                {
                    active_caches_count = _activeCachesCount,
                    active_caches_size_in_bytes = _activeCachesBytes
                });

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(usage, Encoding.UTF8, "application/json")
                });
            }

            if (request.Method == HttpMethod.Get && path.Contains("/actions/caches", StringComparison.OrdinalIgnoreCase))
            {
                var payload = JsonSerializer.Serialize(new
                {
                    total_count = _caches.Length,
                    actions_caches = _caches.Select(c => new
                    {
                        id = c.Id,
                        key = c.Key,
                        @ref = c.Ref,
                        version = c.Version,
                        size_in_bytes = c.SizeInBytes,
                        created_at = c.CreatedAt.ToString("O"),
                        last_accessed_at = c.LastAccessedAt.ToString("O")
                    }).ToArray()
                });

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                });
            }

            if (request.Method == HttpMethod.Delete && path.Contains("/actions/caches/", StringComparison.OrdinalIgnoreCase))
            {
                var idText = request.RequestUri?.Segments.LastOrDefault()?.Trim('/');
                _ = long.TryParse(idText, out var cacheId);
                DeletedCacheIds.Add(cacheId);

                if (_failDeleteIds.Contains(cacheId))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("{\"message\":\"delete failed\"}", Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeCache
    {
        public long Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Ref { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public long SizeInBytes { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastAccessedAt { get; set; }
    }
}
