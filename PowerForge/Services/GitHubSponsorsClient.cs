using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Retrieves public GitHub Sponsors records through the GitHub GraphQL API.
/// </summary>
public sealed class GitHubSponsorsClient
{
    private const string SponsorsQueryWithoutFundingTier = """
        query($login: String!, $first: Int!, $after: String, $activeOnly: Boolean!) {
          owner: repositoryOwner(login: $login) {
            __typename
            ... on User {
              sponsorshipsAsMaintainer(first: $first, after: $after, includePrivate: false, activeOnly: $activeOnly) {
                nodes {
                  sponsorEntity {
                    __typename
                    ... on User { login name avatarUrl url }
                    ... on Organization { login name avatarUrl url }
                  }
                }
                pageInfo { hasNextPage endCursor }
              }
            }
            ... on Organization {
              sponsorshipsAsMaintainer(first: $first, after: $after, includePrivate: false, activeOnly: $activeOnly) {
                nodes {
                  sponsorEntity {
                    __typename
                    ... on User { login name avatarUrl url }
                    ... on Organization { login name avatarUrl url }
                  }
                }
                pageInfo { hasNextPage endCursor }
              }
            }
          }
        }
        """;

    private const string SponsorsQueryWithFundingTier = """
        query($login: String!, $first: Int!, $after: String, $activeOnly: Boolean!) {
          owner: repositoryOwner(login: $login) {
            __typename
            ... on User {
              sponsorshipsAsMaintainer(first: $first, after: $after, includePrivate: false, activeOnly: $activeOnly) {
                nodes {
                  sponsorEntity {
                    __typename
                    ... on User { login name avatarUrl url }
                    ... on Organization { login name avatarUrl url }
                  }
                  tier { monthlyPriceInDollars }
                }
                pageInfo { hasNextPage endCursor }
              }
            }
            ... on Organization {
              sponsorshipsAsMaintainer(first: $first, after: $after, includePrivate: false, activeOnly: $activeOnly) {
                nodes {
                  sponsorEntity {
                    __typename
                    ... on User { login name avatarUrl url }
                    ... on Organization { login name avatarUrl url }
                  }
                  tier { monthlyPriceInDollars }
                }
                pageInfo { hasNextPage endCursor }
              }
            }
          }
        }
        """;

    private static readonly HttpClient SharedClient = CreateSharedClient();
    private readonly HttpClient _client;

    /// <summary>
    /// Creates a GitHub Sponsors client.
    /// </summary>
    /// <param name="client">Optional HTTP client for custom transports and tests.</param>
    public GitHubSponsorsClient(HttpClient? client = null)
    {
        _client = client ?? SharedClient;
    }

    /// <summary>
    /// Retrieves public current sponsors and, when requested, public former sponsors.
    /// Private sponsorships are never requested.
    /// </summary>
    /// <param name="query">Sponsors query.</param>
    /// <returns>Normalized public sponsor records.</returns>
    public GitHubSponsorRecord[] GetSponsors(GitHubSponsorsQuery query)
        => GetSponsorSources(query, includeFundingTierData: false)
            .Select(item => ClonePublicRecord(item.Sponsor))
            .ToArray();

    /// <summary>
    /// Retrieves sponsor identities and optionally retains funding amounts for in-process recognition mapping.
    /// Funding amounts must never cross the public result boundary.
    /// </summary>
    internal GitHubSponsorSourceRecord[] GetSponsorSources(GitHubSponsorsQuery query, bool includeFundingTierData)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));

        var login = (query.SponsorableLogin ?? string.Empty).Trim();
        var token = (query.Token ?? string.Empty).Trim();
        if (login.Length == 0) throw new InvalidOperationException("Sponsorable login is required.");
        if (token.Length == 0) throw new InvalidOperationException("GitHub token is required.");

        var endpoint = NormalizeEndpoint(query.GraphQlEndpoint);
        var pageSize = Math.Max(1, Math.Min(100, query.PageSize));
        var current = GetConnection(endpoint, token, login, activeOnly: true, pageSize, includeFundingTierData);
        var currentKeys = new HashSet<string>(current.Select(item => item.Sponsor.Key), StringComparer.OrdinalIgnoreCase);

        var records = current
            .GroupBy(item => item.Sponsor.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        if (query.IncludeFormer)
        {
            // Historical tier prices are not needed: current sponsors already carry the
            // optional tier data from the active-only query, while former sponsors are
            // deliberately recognized without a historical tier classification.
            var allPublic = GetConnection(endpoint, token, login, activeOnly: false, pageSize, includeFundingTierData: false);
            records.AddRange(allPublic
                .Where(item => !currentKeys.Contains(item.Sponsor.Key))
                .GroupBy(item => item.Sponsor.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var item = group.First();
                    item.Sponsor.Status = GitHubSponsorStatus.Former;
                    return item;
                }));
        }

        return records
            .OrderBy(item => item.Sponsor.Status)
            .ThenBy(item => item.Sponsor.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Sponsor.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private List<GitHubSponsorSourceRecord> GetConnection(
        Uri endpoint,
        string token,
        string login,
        bool activeOnly,
        int pageSize,
        bool includeFundingTierData)
    {
        var records = new List<GitHubSponsorSourceRecord>();
        string? cursor = null;

        while (true)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            request.Content = new StringContent(
                CreateRequestBody(login, pageSize, cursor, activeOnly, includeFundingTierData),
                Encoding.UTF8,
                "application/json");

            using var response = _client.SendAsync(request).ConfigureAwait(false).GetAwaiter().GetResult();
            var body = response.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"GitHub GraphQL request failed with HTTP {(int)response.StatusCode}: {TrimForMessage(body)}");

            using var document = JsonDocument.Parse(body);
            ThrowOnGraphQlErrors(document.RootElement);
            var connection = GetSponsorConnection(document.RootElement, login);

            if (connection.TryGetProperty("nodes", out var nodes) && nodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in nodes.EnumerateArray())
                {
                    var parsed = ParseSponsor(
                        node,
                        activeOnly ? GitHubSponsorStatus.Current : GitHubSponsorStatus.Former,
                        includeFundingTierData);
                    if (parsed is not null)
                        records.Add(parsed);
                }
            }

            if (!connection.TryGetProperty("pageInfo", out var pageInfo) || pageInfo.ValueKind != JsonValueKind.Object)
                break;

            var hasNextPage = pageInfo.TryGetProperty("hasNextPage", out var hasNext) && hasNext.ValueKind == JsonValueKind.True;
            cursor = pageInfo.TryGetProperty("endCursor", out var endCursor) && endCursor.ValueKind == JsonValueKind.String
                ? endCursor.GetString()
                : null;

            if (!hasNextPage)
                break;
            if (string.IsNullOrWhiteSpace(cursor))
                throw new InvalidOperationException("GitHub Sponsors pagination reported another page without an end cursor.");
        }

        return records;
    }

    private static JsonElement GetSponsorConnection(JsonElement root, string login)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("GitHub GraphQL response did not contain a data object.");

        if (data.TryGetProperty("owner", out var owner) && owner.ValueKind == JsonValueKind.Object &&
            owner.TryGetProperty("sponsorshipsAsMaintainer", out var connection) && connection.ValueKind == JsonValueKind.Object)
            return connection;

        throw new InvalidOperationException($"GitHub sponsorable account '{login}' was not found or its public sponsorships are unavailable.");
    }

    private static GitHubSponsorSourceRecord? ParseSponsor(
        JsonElement node,
        GitHubSponsorStatus status,
        bool includeFundingTierData)
    {
        if (!node.TryGetProperty("sponsorEntity", out var sponsor) || sponsor.ValueKind != JsonValueKind.Object)
            return null;

        var login = ReadString(sponsor, "login");
        if (string.IsNullOrWhiteSpace(login))
            return null;

        var typeName = ReadString(sponsor, "__typename");
        var name = ReadString(sponsor, "name");
        int? monthlyPrice = null;
        if (includeFundingTierData && node.TryGetProperty("tier", out var tier) && tier.ValueKind == JsonValueKind.Object)
        {
            if (tier.TryGetProperty("monthlyPriceInDollars", out var price) && price.ValueKind == JsonValueKind.Number && price.TryGetInt32(out var parsedPrice))
                monthlyPrice = parsedPrice;
        }

        return new GitHubSponsorSourceRecord
        {
            Sponsor = new GitHubSponsorRecord
            {
                Key = login!,
                Login = login,
                DisplayName = string.IsNullOrWhiteSpace(name) ? login! : name!.Trim(),
                ProfileUrl = ReadString(sponsor, "url"),
                AvatarUrl = ReadString(sponsor, "avatarUrl"),
                Status = status,
                EntityType = string.Equals(typeName, "Organization", StringComparison.OrdinalIgnoreCase)
                    ? GitHubSponsorEntityType.Organization
                    : GitHubSponsorEntityType.User
            },
            FundingTierMonthlyDollars = monthlyPrice
        };
    }

    private static string CreateRequestBody(
        string login,
        int pageSize,
        string? cursor,
        bool activeOnly,
        bool includeFundingTierData)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("query", includeFundingTierData ? SponsorsQueryWithFundingTier : SponsorsQueryWithoutFundingTier);
            writer.WritePropertyName("variables");
            writer.WriteStartObject();
            writer.WriteString("login", login);
            writer.WriteNumber("first", pageSize);
            if (cursor is null) writer.WriteNull("after"); else writer.WriteString("after", cursor);
            writer.WriteBoolean("activeOnly", activeOnly);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void ThrowOnGraphQlErrors(JsonElement root)
    {
        if (!root.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array || errors.GetArrayLength() == 0)
            return;

        var messages = errors.EnumerateArray()
            .Select(error => ReadString(error, "message"))
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToArray();
        var detail = messages.Length == 0 ? "Unknown GraphQL error." : string.Join(" ", messages);
        throw new InvalidOperationException($"GitHub GraphQL returned an error: {TrimForMessage(detail)}");
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static Uri NormalizeEndpoint(string? endpoint)
    {
        var value = string.IsNullOrWhiteSpace(endpoint) ? "https://api.github.com/graphql" : endpoint!.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            throw new InvalidOperationException($"Invalid GitHub GraphQL endpoint: {endpoint}");
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("GitHub GraphQL endpoint must use HTTPS because the request carries a bearer token.");
        return uri;
    }

    private static GitHubSponsorRecord ClonePublicRecord(GitHubSponsorRecord source)
        => new()
        {
            Key = source.Key,
            Login = source.Login,
            DisplayName = source.DisplayName,
            ProfileUrl = source.ProfileUrl,
            AvatarUrl = source.AvatarUrl,
            Status = source.Status,
            EntityType = source.EntityType,
            RecognitionTierKey = source.RecognitionTierKey
        };

    private static string TrimForMessage(string? text)
    {
        var value = (text ?? string.Empty).Trim();
        return value.Length <= 2000 ? value : value.Substring(0, 2000) + "...";
    }

    private static HttpClient CreateSharedClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PowerForge-GitHubSponsors/1.0");
        return client;
    }
}
