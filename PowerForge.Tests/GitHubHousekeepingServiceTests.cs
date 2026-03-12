using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class GitHubHousekeepingServiceTests
{
    [Fact]
    public void Run_DryRun_AggregatesConfiguredSections()
    {
        var artifactHandler = new FakeGitHubArtifactsHandler(new[]
        {
            Artifact(id: 1, name: "test-results", daysAgo: 20),
            Artifact(id: 2, name: "test-results", daysAgo: 10)
        });
        var cacheHandler = new FakeGitHubCachesHandler(new[]
        {
            Cache(id: 11, key: "ubuntu-nuget", daysAgo: 20),
            Cache(id: 12, key: "ubuntu-nuget", daysAgo: 5)
        });

        using var artifactClient = new HttpClient(artifactHandler);
        using var cacheClient = new HttpClient(cacheHandler);
        var service = new GitHubHousekeepingService(
            new NullLogger(),
            new GitHubArtifactCleanupService(new NullLogger(), artifactClient),
            new GitHubActionsCacheCleanupService(new NullLogger(), cacheClient),
            new RunnerHousekeepingService(new NullLogger()));

        var result = service.Run(new GitHubHousekeepingSpec
        {
            Repository = "EvotecIT/PSPublishModule",
            Token = "test-token",
            DryRun = true,
            Runner = new GitHubHousekeepingRunnerSpec { Enabled = false },
            Artifacts = new GitHubHousekeepingArtifactSpec
            {
                Enabled = true,
                KeepLatestPerName = 0,
                MaxAgeDays = null
            },
            Caches = new GitHubHousekeepingCacheSpec
            {
                Enabled = true,
                KeepLatestPerKey = 0,
                MaxAgeDays = null
            }
        });

        Assert.True(result.Success);
        Assert.Equal(new[] { "artifacts", "caches" }, result.RequestedSections);
        Assert.Equal(new[] { "artifacts", "caches" }, result.CompletedSections);
        Assert.Empty(result.FailedSections);
        Assert.NotNull(result.Artifacts);
        Assert.NotNull(result.Caches);
        Assert.Equal(2, result.Artifacts!.PlannedDeletes);
        Assert.Equal(2, result.Caches!.PlannedDeletes);
    }

    [Fact]
    public void Run_RemoteCleanupWithoutToken_FailsGracefully()
    {
        var service = new GitHubHousekeepingService(new NullLogger());

        var result = service.Run(new GitHubHousekeepingSpec
        {
            Repository = "EvotecIT/OfficeIMO",
            Token = string.Empty,
            DryRun = true,
            Artifacts = new GitHubHousekeepingArtifactSpec { Enabled = true },
            Caches = new GitHubHousekeepingCacheSpec { Enabled = false },
            Runner = new GitHubHousekeepingRunnerSpec { Enabled = false }
        });

        Assert.False(result.Success);
        Assert.Contains("artifacts", result.FailedSections);
        Assert.Contains("token", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static FakeArtifact Artifact(long id, string name, int daysAgo)
    {
        var timestamp = DateTimeOffset.UtcNow.AddDays(-daysAgo);
        return new FakeArtifact
        {
            Id = id,
            Name = name,
            SizeInBytes = 1024 + id,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
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

    private sealed class FakeGitHubArtifactsHandler : HttpMessageHandler
    {
        private readonly FakeArtifact[] _artifacts;

        public FakeGitHubArtifactsHandler(FakeArtifact[] artifacts)
        {
            _artifacts = artifacts ?? Array.Empty<FakeArtifact>();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri is not null &&
                request.RequestUri.AbsolutePath.Contains("/actions/artifacts", StringComparison.OrdinalIgnoreCase))
            {
                var payload = new
                {
                    total_count = _artifacts.Length,
                    artifacts = _artifacts.Select(a => new
                    {
                        id = a.Id,
                        name = a.Name,
                        size_in_bytes = a.SizeInBytes,
                        expired = false,
                        created_at = a.CreatedAt.ToString("O"),
                        updated_at = a.UpdatedAt.ToString("O"),
                        workflow_run = new { id = 1000 + a.Id }
                    }).ToArray()
                };

                var json = JsonSerializer.Serialize(payload);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }

            if (request.Method == HttpMethod.Delete)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeGitHubCachesHandler : HttpMessageHandler
    {
        private readonly FakeCache[] _caches;

        public FakeGitHubCachesHandler(FakeCache[] caches)
        {
            _caches = caches ?? Array.Empty<FakeCache>();
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (request.Method == HttpMethod.Get && path.Contains("/actions/cache/usage", StringComparison.OrdinalIgnoreCase))
            {
                var usage = JsonSerializer.Serialize(new
                {
                    active_caches_count = _caches.Length,
                    active_caches_size_in_bytes = _caches.Sum(c => c.SizeInBytes)
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

            if (request.Method == HttpMethod.Delete)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class FakeArtifact
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public long SizeInBytes { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
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
