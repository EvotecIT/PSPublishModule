using System.Net.Http.Headers;
using System.Text.Json;

namespace PowerForge.Web;

public static partial class WebEcosystemStatsGenerator
{
    private const int GitHubSearchPageSize = 100;
    private const int GitHubSearchResultLimit = 1000;

    private static void PopulateGitHubOpenIssueCounts(
        HttpClient http,
        WebEcosystemStatsOptions options,
        string organization,
        List<WebEcosystemGitHubRepository> repositories,
        List<string> warnings)
    {
        if (repositories.Count == 0)
            return;

        var counts = repositories.ToDictionary(
            static repository => repository.FullName,
            static _ => 0,
            StringComparer.OrdinalIgnoreCase);

        var page = 1;
        var fetched = 0;
        int? totalCount = null;

        while (fetched < GitHubSearchResultLimit)
        {
            var query = Uri.EscapeDataString($"org:{organization} is:issue is:open");
            var url = $"{GitHubApiBase}/search/issues?q={query}&per_page={GitHubSearchPageSize}&page={page}";
            using var request = CreateGitHubRequest(url, options.GitHubToken);
            using var response = Send(http, request, warnings, "GitHub open issue count");
            if (response is null)
                throw new InvalidOperationException("GitHub returned repository data but accurate open issue counts could not be retrieved.");

            using var stream = response.Content.ReadAsStream();
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("items", out var itemsElement) ||
                itemsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("GitHub open issue search response did not include an items array.");
            }

            if (document.RootElement.TryGetProperty("incomplete_results", out var incompleteElement) &&
                incompleteElement.ValueKind is JsonValueKind.True)
            {
                throw new InvalidOperationException("GitHub open issue search returned incomplete results.");
            }

            if (!totalCount.HasValue &&
                document.RootElement.TryGetProperty("total_count", out var totalCountElement) &&
                totalCountElement.TryGetInt32(out var parsedTotalCount))
            {
                totalCount = parsedTotalCount;
                if (parsedTotalCount > GitHubSearchResultLimit)
                {
                    throw new InvalidOperationException(
                        $"GitHub reports {parsedTotalCount} open issues for {organization}, above the search API's {GitHubSearchResultLimit}-result accuracy limit.");
                }
            }

            var pageCount = 0;
            foreach (var issueElement in itemsElement.EnumerateArray())
            {
                pageCount++;
                fetched++;
                var repositoryUrl = ReadString(issueElement, "repository_url");
                var fullName = ParseGitHubRepositoryFullName(repositoryUrl);
                if (fullName is not null && counts.ContainsKey(fullName))
                    counts[fullName]++;
            }

            if (pageCount < GitHubSearchPageSize || (totalCount.HasValue && fetched >= totalCount.Value))
                break;

            page++;
        }

        foreach (var repository in repositories)
            repository.OpenIssues = counts[repository.FullName];
    }

    private static HttpRequestMessage CreateGitHubRequest(string url, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static string? ParseGitHubRepositoryFullName(string? repositoryUrl)
    {
        if (string.IsNullOrWhiteSpace(repositoryUrl) ||
            !Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length != 3 || !segments[0].Equals("repos", StringComparison.OrdinalIgnoreCase))
            return null;

        return $"{segments[1]}/{segments[2]}";
    }
}
