using System.Net;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class GitHubSponsorsClientTests
{
    [Fact]
    public void GetSponsors_PaginatesAndSeparatesCurrentFromFormerWithoutPrivateData()
    {
        var handler = new SponsorsHandler((activeOnly, cursor) =>
        {
            if (activeOnly && cursor is null)
                return ConnectionResponse(UserNode("alice", "Alice", "User", "Gold", 30), hasNextPage: true, "current-1");
            if (activeOnly)
                return ConnectionResponse(UserNode("acme", "Acme Ltd", "Organization", null, null), hasNextPage: false, null);

            return ConnectionResponse(string.Join(',',
                UserNode("alice", "Alice", "User", "Gold", 30),
                UserNode("acme", "Acme Ltd", "Organization", null, null),
                UserNode("former", "Former Friend", "User", "Bronze", 5)), hasNextPage: false, null);
        });
        using var client = new HttpClient(handler);

        var records = new GitHubSponsorsClient(client).GetSponsors(new GitHubSponsorsQuery
        {
            SponsorableLogin = "owner",
            Token = "secret-token",
            IncludeFormer = true,
            PageSize = 1
        });

        Assert.Equal(3, records.Length);
        Assert.Equal(2, records.Count(record => record.Status == GitHubSponsorStatus.Current));
        var former = Assert.Single(records, record => record.Status == GitHubSponsorStatus.Former);
        Assert.Equal("former", former.Login);
        Assert.Equal(GitHubSponsorEntityType.Organization, Assert.Single(records, record => record.Login == "acme").EntityType);
        Assert.Equal(3, handler.RequestBodies.Count);
        Assert.All(handler.RequestBodies, body => Assert.Contains("includePrivate: false", body, StringComparison.Ordinal));
        Assert.All(handler.RequestBodies, body => Assert.DoesNotContain("monthlyPriceInDollars", body, StringComparison.Ordinal));
        Assert.All(handler.AuthorizationHeaders, header => Assert.Equal("Bearer secret-token", header));
        var publicJson = JsonSerializer.Serialize(records);
        Assert.DoesNotContain("FundingTier", publicJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Gold\"", publicJson, StringComparison.Ordinal);
        Assert.DoesNotContain(":30", publicJson, StringComparison.Ordinal);
    }

    [Fact]
    public void GetSponsorSources_RetainsFundingOnlyForInternalOptInRecognition()
    {
        var handler = new SponsorsHandler((_, _) => ConnectionResponse(UserNode("alice", "Alice", "User", "Gold", 30), false, null));
        using var client = new HttpClient(handler);

        var records = new GitHubSponsorsClient(client).GetSponsorSources(new GitHubSponsorsQuery
        {
            SponsorableLogin = "owner",
            Token = "token",
            IncludeFormer = false
        }, includeFundingTierData: true);

        Assert.Equal(30, Assert.Single(records).FundingTierMonthlyDollars);
        Assert.Contains("monthlyPriceInDollars", Assert.Single(handler.RequestBodies), StringComparison.Ordinal);
    }

    [Fact]
    public void GetSponsorSources_DoesNotRequestFundingTiersForFormerConnection()
    {
        var handler = new SponsorsHandler((activeOnly, _) => activeOnly
            ? ConnectionResponse(UserNode("alice", "Alice", "User", "Gold", 30), false, null)
            : ConnectionResponse(string.Join(',',
                UserNode("alice", "Alice", "User", "Gold", 30),
                UserNode("former", "Former Friend", "User", "Bronze", 5)), false, null));
        using var client = new HttpClient(handler);

        var records = new GitHubSponsorsClient(client).GetSponsorSources(new GitHubSponsorsQuery
        {
            SponsorableLogin = "owner",
            Token = "token",
            IncludeFormer = true
        }, includeFundingTierData: true);

        Assert.Equal(2, handler.RequestBodies.Count);
        Assert.Contains("monthlyPriceInDollars", handler.RequestBodies[0], StringComparison.Ordinal);
        Assert.DoesNotContain("monthlyPriceInDollars", handler.RequestBodies[1], StringComparison.Ordinal);
        Assert.Equal(30, Assert.Single(records, record => record.Sponsor.Status == GitHubSponsorStatus.Current).FundingTierMonthlyDollars);
        Assert.Null(Assert.Single(records, record => record.Sponsor.Status == GitHubSponsorStatus.Former).FundingTierMonthlyDollars);
    }

    [Fact]
    public void GetSponsors_RejectsHttpEndpointBeforeSendingBearerToken()
    {
        var handler = new SponsorsHandler((_, _) => throw new InvalidOperationException("HTTP handler must not be reached."));
        using var client = new HttpClient(handler);

        var exception = Assert.Throws<InvalidOperationException>(() => new GitHubSponsorsClient(client).GetSponsors(new GitHubSponsorsQuery
        {
            SponsorableLogin = "owner",
            Token = "secret-token",
            GraphQlEndpoint = "http://example.test/graphql",
            IncludeFormer = false
        }));

        Assert.Contains("must use HTTPS", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.AuthorizationHeaders);
    }

    [Fact]
    public void GetSponsors_SurfacesGraphQlErrors()
    {
        var handler = new SponsorsHandler((_, _) => """{"errors":[{"message":"Resource not accessible"}]}""");
        using var client = new HttpClient(handler);

        var exception = Assert.Throws<InvalidOperationException>(() => new GitHubSponsorsClient(client).GetSponsors(new GitHubSponsorsQuery
        {
            SponsorableLogin = "owner",
            Token = "token",
            IncludeFormer = false
        }));

        Assert.Contains("Resource not accessible", exception.Message, StringComparison.Ordinal);
    }

    private static string UserNode(string login, string name, string type, string? tierName, int? amount)
    {
        var tier = amount is null
            ? "null"
            : $"{{\"name\":{JsonSerializer.Serialize(tierName)},\"monthlyPriceInDollars\":{amount.Value}}}";
        return $"{{\"sponsorEntity\":{{\"__typename\":\"{type}\",\"login\":\"{login}\",\"name\":\"{name}\",\"avatarUrl\":\"https://avatars.example/{login}\",\"url\":\"https://github.com/{login}\"}},\"tier\":{tier}}}";
    }

    private static string ConnectionResponse(string nodes, bool hasNextPage, string? endCursor)
    {
        var cursor = endCursor is null ? "null" : JsonSerializer.Serialize(endCursor);
        return "{\"data\":{\"owner\":{\"__typename\":\"User\",\"sponsorshipsAsMaintainer\":{\"nodes\":[" + nodes +
               "],\"pageInfo\":{\"hasNextPage\":" + hasNextPage.ToString().ToLowerInvariant() +
               ",\"endCursor\":" + cursor + "}}}}}";
    }

    private sealed class SponsorsHandler(Func<bool, string?, string> responseFactory) : HttpMessageHandler
    {
        internal List<string> RequestBodies { get; } = new();
        internal List<string> AuthorizationHeaders { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(body);
            AuthorizationHeaders.Add(request.Headers.Authorization?.ToString() ?? string.Empty);
            using var document = JsonDocument.Parse(body);
            var variables = document.RootElement.GetProperty("variables");
            var activeOnly = variables.GetProperty("activeOnly").GetBoolean();
            var after = variables.GetProperty("after");
            var cursor = after.ValueKind == JsonValueKind.String ? after.GetString() : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseFactory(activeOnly, cursor), Encoding.UTF8, "application/json")
            };
        }
    }
}
