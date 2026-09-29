using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class GitHubArtifactCleanupServiceTests
{
    [Fact]
    public void Prune_DryRun_UsesSafeDefaultsAndDoesNotDelete()
    {
        var artifacts = new[]
        {
            Artifact(id: 1, name: "test-results-run", daysAgo: 25),
            Artifact(id: 2, name: "test-results-run", daysAgo: 18),
            Artifact(id: 3, name: "test-results-run", daysAgo: 1),
            Artifact(id: 4, name: "github-pages", daysAgo: 30),
            Artifact(id: 5, name: "keep-me-forever", daysAgo: 40),
            Artifact(id: 6, name: "seo-doctor-report", daysAgo: 12)
        };

        var handler = new FakeGitHubArtifactsHandler(artifacts);
        using var client = new HttpClient(handler);
        var service = new GitHubArtifactCleanupService(new NullLogger(), client);

        var result = service.Prune(new GitHubArtifactCleanupSpec
        {
            Repository = "EvotecIT/IntelligenceX",
            Token = "test-token",
            KeepLatestPerName = 1,
            MaxAgeDays = 7,
            MaxDelete = 10,
            DryRun = true
        });

        Assert.True(result.Success);
        Assert.True(result.DryRun);
        Assert.Equal(6, result.ScannedArtifacts);
        Assert.Equal(5, result.MatchedArtifacts);
        Assert.Equal(2, result.PlannedDeletes);
        Assert.Equal(0, result.DeletedArtifacts);
        Assert.Equal(0, result.FailedDeletes);
        Assert.DoesNotContain(handler.DeletedArtifactIds, _ => true);
        Assert.Equal(new long[] { 1, 2 }, result.Planned.Select(p => p.Id).OrderBy(v => v).ToArray());
    }

    [Fact]
    public void Prune_Apply_DeletesArtifactsAndReportsFailures()
    {
        var artifacts = new[]
        {
            Artifact(id: 11, name: "test-results", daysAgo: 20),
            Artifact(id: 12, name: "test-results", daysAgo: 15)
        };

        var handler = new FakeGitHubArtifactsHandler(artifacts, failDeleteIds: new[] { 12L });
        using var client = new HttpClient(handler);
        var service = new GitHubArtifactCleanupService(new NullLogger(), client);

        var result = service.Prune(new GitHubArtifactCleanupSpec
        {
            Repository = "EvotecIT/CodeGlyphX",
            Token = "test-token",
            IncludeNames = new[] { "test-results*" },
            KeepLatestPerName = 0,
            MaxAgeDays = null,
            MaxDelete = 10,
            DryRun = false,
            FailOnDeleteError = true
        });

        Assert.False(result.Success);
        Assert.Equal(2, result.PlannedDeletes);
        Assert.Equal(1, result.DeletedArtifacts);
        Assert.Equal(1, result.FailedDeletes);
        Assert.Contains(11L, handler.DeletedArtifactIds);
        Assert.Contains(12L, handler.DeletedArtifactIds);
        Assert.Single(result.Failed);
        Assert.Equal(12, result.Failed[0].Id);
        Assert.NotNull(result.Failed[0].DeleteError);
    }

    [Fact]
    public void Prune_ExcludePattern_RemovesMatchesFromCandidates()
    {
        var artifacts = new[]
        {
            Artifact(id: 21, name: "github-pages", daysAgo: 45),
            Artifact(id: 22, name: "test-results", daysAgo: 45)
        };

        var handler = new FakeGitHubArtifactsHandler(artifacts);
        using var client = new HttpClient(handler);
        var service = new GitHubArtifactCleanupService(new NullLogger(), client);

        var result = service.Prune(new GitHubArtifactCleanupSpec
        {
            Repository = "EvotecIT/HtmlForgeX",
            Token = "test-token",
            ExcludeNames = new[] { "github-pages" },
            KeepLatestPerName = 0,
            MaxAgeDays = null,
            MaxDelete = 10,
            DryRun = true
        });

        Assert.True(result.Success);
        Assert.Single(result.Planned);
        Assert.Equal(22, result.Planned[0].Id);
    }

    [Fact]
    public void Prune_ExpiredArtifacts_AreDeletionCandidatesEvenInsideKeepWindow()
    {
        var artifacts = new[]
        {
            Artifact(id: 31, name: "github-pages", daysAgo: 1, expired: true),
            Artifact(id: 32, name: "github-pages", daysAgo: 2, expired: false)
        };

        var handler = new FakeGitHubArtifactsHandler(artifacts);
        using var client = new HttpClient(handler);
        var service = new GitHubArtifactCleanupService(new NullLogger(), client);

        var result = service.Prune(new GitHubArtifactCleanupSpec
        {
            Repository = "EvotecIT/TestimoX",
            Token = "test-token",
            KeepLatestPerName = 1,
            MaxAgeDays = 1,
            MaxDelete = 10,
            DryRun = true
        });

        Assert.True(result.Success);
        Assert.Equal(2, result.MatchedArtifacts);
        Assert.Single(result.Planned);
        Assert.Equal(31, result.Planned[0].Id);
        Assert.Equal("expired", result.Planned[0].Reason);
        Assert.Equal(1, result.KeptByRecentWindow);
    }

    [Fact]
    public void Prune_ExactNames_UsesFilteredPagesWhenRepositoryListingFails()
    {
        var artifacts = new[]
        {
            Artifact(id: 101, name: "officeimo-markup-vsix", daysAgo: 15),
            Artifact(id: 102, name: "officeimo-markup-vsix", daysAgo: 12),
            Artifact(id: 103, name: "officeimo-markup-vsix", daysAgo: 3),
            Artifact(id: 104, name: "officeimo-markup-vsix", daysAgo: 1),
            Artifact(id: 105, name: "other-artifact", daysAgo: 20)
        };

        var handler = new FakeGitHubArtifactsHandler(artifacts, failUnfilteredListing: true);
        using var client = new HttpClient(handler);
        var service = new GitHubArtifactCleanupService(new NullLogger(), client);

        var result = service.Prune(new GitHubArtifactCleanupSpec
        {
            Repository = "EvotecIT/OfficeIMO",
            Token = "test-token",
            ExactIncludeNames = new[] { "officeimo-markup-vsix" },
            KeepLatestPerName = 1,
            MaxAgeDays = 7,
            MaxDelete = 10,
            PageSize = 2,
            DryRun = true
        });

        Assert.True(result.Success);
        Assert.Equal(4, result.ScannedArtifacts);
        Assert.Equal(new long[] { 101, 102 }, result.Planned.Select(item => item.Id).OrderBy(id => id));
        Assert.Equal(2, handler.ListingRequests);
        Assert.Empty(handler.DeletedArtifactIds);
    }

    [Fact]
    public void Prune_ExactNames_DoesNotDeleteWhenLaterPageFails()
    {
        var artifacts = new[]
        {
            Artifact(id: 111, name: "officeimo-markup-vsix", daysAgo: 15),
            Artifact(id: 112, name: "officeimo-markup-vsix", daysAgo: 12),
            Artifact(id: 113, name: "officeimo-markup-vsix", daysAgo: 9)
        };

        var handler = new FakeGitHubArtifactsHandler(artifacts, failUnfilteredListing: true, failNamedPage: 2);
        using var client = new HttpClient(handler);
        var service = new GitHubArtifactCleanupService(new NullLogger(), client);

        Assert.Throws<InvalidOperationException>(() => service.Prune(new GitHubArtifactCleanupSpec
        {
            Repository = "EvotecIT/OfficeIMO",
            Token = "test-token",
            ExactIncludeNames = new[] { "officeimo-markup-vsix" },
            KeepLatestPerName = 0,
            MaxAgeDays = null,
            PageSize = 2,
            DryRun = false
        }));
        Assert.Empty(handler.DeletedArtifactIds);
    }

    [Fact]
    public void Prune_ExactNames_RejectsEmptyNameBeforeListingOrDeleting()
    {
        var handler = new FakeGitHubArtifactsHandler(new[] { Artifact(id: 121, name: "other-artifact", daysAgo: 15) });
        using var client = new HttpClient(handler);
        var service = new GitHubArtifactCleanupService(new NullLogger(), client);

        Assert.Throws<InvalidOperationException>(() => service.Prune(new GitHubArtifactCleanupSpec
        {
            Repository = "EvotecIT/OfficeIMO",
            Token = "test-token",
            ExactIncludeNames = new[] { "" },
            KeepLatestPerName = 0,
            DryRun = false
        }));
        Assert.Equal(0, handler.ListingRequests);
        Assert.Empty(handler.DeletedArtifactIds);
    }

    [Fact]
    public void Prune_ExactNames_DoesNotDeleteWhenArtifactTimestampIsMissing()
    {
        var artifacts = new[]
        {
            Artifact(id: 131, name: "officeimo-markup-vsix", daysAgo: 15),
            Artifact(id: 132, name: "officeimo-markup-vsix", daysAgo: 1)
        };
        var handler = new FakeGitHubArtifactsHandler(artifacts, missingTimestampId: 132);
        using var client = new HttpClient(handler);
        var service = new GitHubArtifactCleanupService(new NullLogger(), client);

        Assert.Throws<InvalidOperationException>(() => service.Prune(new GitHubArtifactCleanupSpec
        {
            Repository = "EvotecIT/OfficeIMO",
            Token = "test-token",
            ExactIncludeNames = new[] { "officeimo-markup-vsix" },
            KeepLatestPerName = 1,
            MaxAgeDays = 7,
            DryRun = false
        }));
        Assert.Empty(handler.DeletedArtifactIds);
    }

    private static FakeArtifact Artifact(long id, string name, int daysAgo, bool expired = false)
    {
        var timestamp = DateTimeOffset.UtcNow.AddDays(-daysAgo);
        return new FakeArtifact
        {
            Id = id,
            Name = name,
            SizeInBytes = 1024 + id,
            CreatedAt = timestamp,
            UpdatedAt = timestamp,
            Expired = expired
        };
    }

    private sealed class FakeGitHubArtifactsHandler : HttpMessageHandler
    {
        private readonly FakeArtifact[] _artifacts;
        private readonly HashSet<long> _failDeleteIds;
        private readonly bool _failUnfilteredListing;
        private readonly int? _failNamedPage;
        private readonly long? _missingTimestampId;

        public List<long> DeletedArtifactIds { get; } = new();
        public int ListingRequests { get; private set; }

        public FakeGitHubArtifactsHandler(FakeArtifact[] artifacts, IEnumerable<long>? failDeleteIds = null, bool failUnfilteredListing = false, int? failNamedPage = null, long? missingTimestampId = null)
        {
            _artifacts = artifacts ?? Array.Empty<FakeArtifact>();
            _failDeleteIds = failDeleteIds is null
                ? new HashSet<long>()
                : new HashSet<long>(failDeleteIds);
            _failUnfilteredListing = failUnfilteredListing;
            _failNamedPage = failNamedPage;
            _missingTimestampId = missingTimestampId;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get &&
                request.RequestUri is not null &&
                request.RequestUri.AbsolutePath.Contains("/actions/artifacts", StringComparison.OrdinalIgnoreCase))
            {
                ListingRequests++;
                var name = GetQueryParameter(request.RequestUri, "name");
                _ = int.TryParse(GetQueryParameter(request.RequestUri, "page"), out var page);
                _ = int.TryParse(GetQueryParameter(request.RequestUri, "per_page"), out var pageSize);
                page = Math.Max(1, page);
                pageSize = Math.Max(1, pageSize);
                if ((name is null && _failUnfilteredListing) || (name is not null && page == _failNamedPage))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

                var matching = _artifacts.Where(artifact => name is null || string.Equals(artifact.Name, name, StringComparison.Ordinal)).ToArray();
                var payload = new
                {
                    total_count = matching.Length,
                    artifacts = matching.Skip((page - 1) * pageSize).Take(pageSize).Select(a => new
                    {
                        id = a.Id,
                        name = a.Name,
                        size_in_bytes = a.SizeInBytes,
                        expired = a.Expired,
                        created_at = a.CreatedAt.ToString("O"),
                        updated_at = a.Id == _missingTimestampId ? null : a.UpdatedAt.ToString("O"),
                        workflow_run = new { id = 1000 + a.Id }
                    }).ToArray()
                };

                var json = JsonSerializer.Serialize(payload);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                });
            }

            if (request.Method == HttpMethod.Delete &&
                request.RequestUri is not null &&
                request.RequestUri.AbsolutePath.Contains("/actions/artifacts/", StringComparison.OrdinalIgnoreCase))
            {
                var idText = request.RequestUri.Segments.LastOrDefault()?.Trim('/');
                _ = long.TryParse(idText, out var artifactId);
                DeletedArtifactIds.Add(artifactId);

                if (_failDeleteIds.Contains(artifactId))
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

        private static string? GetQueryParameter(Uri uri, string key)
        {
            foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = part.Split('=', 2);
                if (string.Equals(Uri.UnescapeDataString(pair[0]), key, StringComparison.Ordinal))
                    return pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            }
            return null;
        }
    }

    private sealed class FakeArtifact
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public long SizeInBytes { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public bool Expired { get; set; }
    }
}
