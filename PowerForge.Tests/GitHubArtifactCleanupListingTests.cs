using System.Net;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class GitHubArtifactCleanupListingTests
{
    [Theory]
    [InlineData("count-change")]
    [InlineData("duplicate")]
    [InlineData("short-page")]
    [InlineData("excess-count")]
    public void Prune_ChangingListing_RestartsAndUsesOnlyCompleteFreshInventory(string change)
    {
        using var handler = new ListingHandler();
        AddUnstableAttempt(handler, change);
        handler.Add(1, Page(2, Artifact(11, 1), Artifact(12, 30)));
        using var client = new HttpClient(handler);

        var result = new GitHubArtifactCleanupService(new NullLogger(), client).Prune(Spec());

        Assert.True(result.Success);
        Assert.Equal(2, result.ScannedArtifacts);
        Assert.Equal(1, result.KeptByRecentWindow);
        Assert.Equal(12, Assert.Single(result.Deleted).Id);
        Assert.Equal(new long[] { 12 }, handler.DeletedIds);
        Assert.Equal(new[] { 1, 2, 1 }, handler.Pages);
        Assert.Empty(handler.Responses);
    }

    [Theory]
    [InlineData("count-change")]
    [InlineData("duplicate")]
    [InlineData("short-page")]
    [InlineData("excess-count")]
    public void Prune_PersistentListingInstability_StopsAfterBoundedAttemptsWithoutDeleting(string change)
    {
        using var handler = new ListingHandler();
        for (var attempt = 0; attempt < 3; attempt++)
            AddUnstableAttempt(handler, change);
        using var client = new HttpClient(handler);

        var failure = Assert.Throws<InvalidOperationException>(() =>
            new GitHubArtifactCleanupService(new NullLogger(), client).Prune(Spec()));

        Assert.Contains("no deletions were attempted", failure.Message);
        Assert.Equal(new[] { 1, 2, 1, 2, 1, 2 }, handler.Pages);
        Assert.Empty(handler.Responses);
        Assert.Empty(handler.DeletedIds);
    }

    [Fact]
    public void Prune_LaterExactNameChanges_RestartsAllNamesBeforePlanning()
    {
        using var handler = new ListingHandler();
        handler.Add(1, Page(1, Artifact(999, 60, "first")), "first");
        handler.Add(1, Page(3, Artifact(998, 60, "second"), Artifact(997, 50, "second")), "second");
        handler.Add(2, Page(2, Artifact(996, 40, "second")), "second");
        handler.Add(1, Page(1, Artifact(11, 1, "first")), "first");
        handler.Add(1, Page(2, Artifact(21, 1, "second"), Artifact(22, 30, "second")), "second");
        using var client = new HttpClient(handler);
        var spec = Spec();
        spec.IncludeNames = Array.Empty<string>();
        spec.ExactIncludeNames = new[] { "first", "second" };

        var result = new GitHubArtifactCleanupService(new NullLogger(), client).Prune(spec);

        Assert.Equal(3, result.ScannedArtifacts);
        Assert.Equal(22, Assert.Single(result.Deleted).Id);
        Assert.Equal(new long[] { 22 }, handler.DeletedIds);
        Assert.Empty(handler.Responses);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Prune_MalformedOrFailedListing_IsNotRetriedOrDeleted(bool malformed)
    {
        using var handler = new ListingHandler();
        handler.Add(1, malformed ? "{\"total_count\":1,\"artifacts\":[{\"id\":7,\"name\":\"reports\"}]}" : "{}",
            status: malformed ? HttpStatusCode.OK : HttpStatusCode.Forbidden);
        using var client = new HttpClient(handler);

        Assert.Throws<InvalidOperationException>(() =>
            new GitHubArtifactCleanupService(new NullLogger(), client).Prune(Spec()));

        Assert.Equal(new[] { 1 }, handler.Pages);
        Assert.Empty(handler.Responses);
        Assert.Empty(handler.DeletedIds);
    }

    private static GitHubArtifactCleanupSpec Spec() => new()
    {
        Repository = "owner/repository",
        Token = "synthetic-token",
        IncludeNames = new[] { "reports" },
        KeepLatestPerName = 1,
        MaxAgeDays = 7,
        PageSize = 2,
        DryRun = false
    };

    private static void AddUnstableAttempt(ListingHandler handler, string change)
    {
        handler.Add(1, Page(3, Artifact(999, 60), Artifact(998, 50)));
        handler.Add(2, change switch
        {
            "count-change" => Page(2, Artifact(997, 40)),
            "duplicate" => Page(3, Artifact(998, 50)),
            "short-page" => Page(3),
            "excess-count" => Page(3, Artifact(997, 40), Artifact(996, 30)),
            _ => throw new ArgumentException(nameof(change))
        });
    }

    private static object Artifact(long id, int ageDays, string name = "reports") => new
    {
        id,
        name,
        size_in_bytes = 100,
        created_at = DateTimeOffset.UtcNow.AddDays(-ageDays).ToString("O"),
        updated_at = DateTimeOffset.UtcNow.AddDays(-ageDays).ToString("O"),
        expired = false
    };

    private static string Page(int count, params object[] artifacts) =>
        JsonSerializer.Serialize(new { total_count = count, artifacts });

    private sealed class ListingHandler : HttpMessageHandler
    {
        public Queue<(int Page, string? Name, string Body, HttpStatusCode Status)> Responses { get; } = new();
        public List<int> Pages { get; } = new();
        public List<long> DeletedIds { get; } = new();

        public void Add(int page, string body, string? name = null, HttpStatusCode status = HttpStatusCode.OK) =>
            Responses.Enqueue((page, name, body, status));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Delete)
            {
                // No deletion can precede the last complete inventory, including all exact names.
                Assert.Empty(Responses);
                DeletedIds.Add(long.Parse(request.RequestUri!.Segments.Last()));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            Assert.Equal(HttpMethod.Get, request.Method);
            var response = Responses.Dequeue();
            var query = request.RequestUri!.Query;
            Assert.Contains($"page={response.Page}", query);
            Assert.Equal(response.Name is null, !query.Contains("&name="));
            if (response.Name is not null) Assert.Contains($"&name={response.Name}", query);
            Pages.Add(response.Page);
            return Task.FromResult(new HttpResponseMessage(response.Status)
            {
                Content = new StringContent(response.Body, Encoding.UTF8, "application/json")
            });
        }
    }
}
